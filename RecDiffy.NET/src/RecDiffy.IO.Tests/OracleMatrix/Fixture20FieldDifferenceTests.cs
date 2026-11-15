using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using RecDiffy.Results;

namespace RecDiffy.IO.Tests.OracleMatrix;

// Fixture 2.0's field-level acceptance (spec v3.1 §12.3 AC-3): every expected_differences entry is a field difference with
// the oracle's internal value, counterparty value and delta, and the engine states no other difference unless a stated one
// explains it. Fixture20OracleTests checks the oracle's delta and the root of a break; these add the two values, the
// unstated-difference check and each field's outcome.
public class Fixture20FieldDifferenceTests
{
    private static readonly OracleDataset Dataset = OracleDatasets.Fixture20;

    private sealed record Stated(string Case, string Field, decimal Internal, decimal Counterparty, decimal Delta);

    // The oracle's difference fields as profile fields.
    private static string? Field(string name) => name switch
    {
        "price" => "Price",
        "quantity" => "Quantity",
        "settle_date" => "SettleDate",
        "net_cash" => "NetCash",
        "accrued" => "Accrued",
        "coupon" => "Coupon",
        _ => null,
    };

    // Every difference the oracle states, with its values when they are numbers. A date difference (settle_date) is named
    // by its field: it has no decimal values.
    private sealed record Difference(string Case, string Field, decimal? Internal, decimal? Counterparty, decimal? Delta);

    private static readonly Lazy<IReadOnlyList<Difference>> All = new(() =>
    [
        .. JsonNode.Parse(File.ReadAllBytes(Dataset.Oracle))!["cases"]!.AsArray().OfType<JsonNode>().SelectMany(item =>
            item["expected_differences"]!.AsArray().OfType<JsonNode>().Select(difference => new Difference(
                item["case_id"]!.GetValue<string>(),
                difference["field"]!.GetValue<string>(),
                Decimal(difference, "internal"),
                Decimal(difference, "counterparty"),
                Decimal(difference, "delta")))),
    ]);

    private static readonly Lazy<IReadOnlyList<Stated>> Differences = new(() =>
    [
        .. All.Value.Where(difference => difference.Delta is not null)
            .Select(difference => new Stated(difference.Case, difference.Field, difference.Internal!.Value, difference.Counterparty!.Value, difference.Delta!.Value)),
    ]);

    private static decimal? Decimal(JsonNode item, string name) =>
        decimal.TryParse(item[name]?.GetValue<string>(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static ResultEntry? EntryOf(string caseId)
    {
        var item = Dataset.Cases().Single(candidate => candidate.Id == caseId);
        var rows = Fixture20OracleTests.Outcome.Rows.ToDictionary(row => (row.Row.Source, row.Row.RowKey));
        var ids = item.Left.Select(key => rows[("left", key)].EntryId).Concat(item.Right.Select(key => rows[("right", key)].EntryId)).Distinct().ToArray();
        return ids is [{ } id] ? Fixture20OracleTests.Outcome.Entries.Single(entry => entry.Id == id) : null;
    }

    [Fact]
    public void The_oracle_states_59_differences_in_38_cases_and_54_of_them_are_numbers() =>
        Assert.Equal((59, 38, 54), (All.Value.Count, All.Value.Select(stated => stated.Case).Distinct(StringComparer.Ordinal).Count(), Differences.Value.Count));

    // The values, not only the delta. Net cash is signed in this fixture, and both sides are in the internal view after cash.toPerspective.
    [Fact]
    public void Every_stated_difference_is_a_field_difference_with_the_oracles_two_values_and_delta()
    {
        var disagreements = Differences.Value.Select(stated => (Stated: stated, Entry: EntryOf(stated.Case), Field: Field(stated.Field)))
            .Select(row => (row.Stated, Diffs: row.Entry?.Diffs.Where(diff => diff.Field.Name == row.Field).ToArray() ?? []))
            .Select(row => row.Diffs switch
            {
                [var diff] when (diff.Left.Number, diff.Right.Number, diff.Delta.Number) == (row.Stated.Internal, row.Stated.Counterparty, row.Stated.Delta) => null,
                [var diff] => $"{row.Stated.Case}: {row.Stated.Field} is {diff.Left.Number} to {diff.Right.Number} (delta {diff.Delta.Number}), the oracle says {row.Stated.Internal} to {row.Stated.Counterparty} ({row.Stated.Delta})",
                _ => $"{row.Stated.Case}: {row.Stated.Field} has no single field difference",
            })
            .OfType<string>()
            .ToArray();

        OracleAssert.None(disagreements);
    }

    // Fees is a field the oracle never states (the street schema has no commission column): the engine infers it, so a fee
    // break shows a Fees difference that the stated net cash difference is attributed to. Any other unstated difference is a defect.
    // Left out, with their reason: a signed BUY, whose net cash trading.netCash can't attribute to its Fees (#433), and the swap coupon.
    [Fact]
    public void The_engine_states_no_difference_the_oracle_does_not_unless_a_stated_one_is_attributed_to_it()
    {
        var stated = All.Value.GroupBy(item => item.Case, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => Field(item.Field)).OfType<string>().ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var cases = Dataset.Cases().Where(item => item.Id != "CASE-BCC487E33A290260" && Deferral.SignedCash.Contains(item.Id) is false).ToArray();

        var disagreements = cases
            .Select(item => (Case: item, Entry: EntryOf(item.Id)))
            .Where(pair => pair.Entry is not null)
            .SelectMany(pair => Unstated(pair.Entry!, stated.GetValueOrDefault(pair.Case.Id) ?? []).Select(diff => $"{pair.Case.Id}: {diff}"))
            .ToArray();

        OracleAssert.None(disagreements);
    }

    private static IEnumerable<string> Unstated(ResultEntry entry, HashSet<string> stated)
    {
        var attributedTo = entry.Diffs.SelectMany(diff => diff.AttributedTo).ToHashSet(StringComparer.Ordinal);
        return entry.Diffs
            .Where(diff => diff.Outcome is not FieldOutcome.Exact && stated.Contains(diff.Field.Name) is false && attributedTo.Contains(diff.Field.Name) is false)
            .Select(diff => $"{diff.Field.Name} {diff.Outcome} {diff.Delta.Number} is not stated by the oracle");
    }

    // A near-match differs only within tolerance. A break has a Break root, and every stated difference that is not the root is attributed to it.
    // Left out, with their reason: a signed BUY, whose net cash trading.netCash can't explain (#433), and the swap coupon.
    [Fact]
    public void Every_difference_of_a_near_match_is_WithinTolerance_and_a_break_has_a_Break_root()
    {
        var disagreements = Dataset.Cases()
            .Where(item => item.Kind is (CaseKind.WithinTolerance or CaseKind.Break) && item.Id != "CASE-BCC487E33A290260" && Deferral.SignedCash.Contains(item.Id) is false)
            .Select(item => (Case: item, Entry: EntryOf(item.Id)))
            .Select(pair => (pair.Case, Outcomes: pair.Entry?.Diffs.Where(diff => diff.Outcome is not FieldOutcome.Exact).Select(diff => diff.Outcome).ToArray() ?? []))
            .Select(pair => (pair.Case.Kind, pair.Outcomes) switch
            {
                (CaseKind.WithinTolerance, var outcomes) when outcomes.Length > 0 && outcomes.All(outcome => outcome is FieldOutcome.WithinTolerance) => null,
                (CaseKind.Break, var outcomes) when outcomes.Contains(FieldOutcome.Break) => null,
                (_, var outcomes) => $"{pair.Case.Id} ({pair.Case.Kind}): field outcomes [{string.Join(", ", outcomes)}]",
            })
            .OfType<string>()
            .ToArray();

        OracleAssert.None(disagreements);
    }

    [Fact]
    public void The_cases_the_outcome_check_covers_are_the_near_matches_and_breaks_with_no_signed_cash_or_swap_coupon() =>
        Assert.Equal(
            38 - 14,
            Dataset.Cases().Count(item => item.Kind is (CaseKind.WithinTolerance or CaseKind.Break) && item.Id != "CASE-BCC487E33A290260" && Deferral.SignedCash.Contains(item.Id) is false));
}
