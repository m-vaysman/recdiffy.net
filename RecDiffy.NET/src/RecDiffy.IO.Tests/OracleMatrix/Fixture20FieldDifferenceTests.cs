using System.Globalization;
using System.Text.Json.Nodes;
using RecDiffy.Results;

namespace RecDiffy.IO.Tests.OracleMatrix;

// Fixture 2.0's field-level acceptance (spec v3.1 §12.3 AC-3): every expected_differences entry is a field difference with
// the oracle's internal value, counterparty value and delta, and the engine states no other difference unless a stated one
// explains it. Fixture20OracleTests checks the oracle's delta and the root of a break; these add the two values, the
// unstated-difference check and each field's outcome. Each check leaves out only the cases it fails, by id (OracleDefects).
public class Fixture20FieldDifferenceTests
{
    private static readonly OracleDataset Dataset = OracleDatasets.Fixture20;

    // The signed-cash fee cases: a signed BUY whose Fees difference the stated net cash is not attributed to (#433).
    private static readonly HashSet<string> SignedFeeCases = new(StringComparer.Ordinal)
    {
        "CASE-B30F53E282BBF4EB",
        "CASE-464C3900AB52BBF9",
        "CASE-CD0C3AD6F2EDA44E",
        "CASE-91DE8E3917A9B141",
        "CASE-E56D4A056DC1D66E",
    };

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

    // The oracle's root causes as profile fields. A fee break's root cause is COMMISSION, and the engine's root is the inferred Fees.
    private static string? Root(string name) => Fixture20OracleTests.Field(name);

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

    // The values, not only the delta, for every stated number and every case. Net cash is signed in this fixture, and both
    // sides are in the internal view after cash.toPerspective.
    [Fact]
    public void Every_stated_difference_is_a_field_difference_with_the_oracles_two_values_and_delta()
    {
        var disagreements = Differences.Value.Select(stated => (Stated: stated, Entry: EntryOf(stated.Case), Field: Field(stated.Field)))
            .Select(row => (row.Stated, Diffs: row.Entry?.Diffs.Where(diff => diff.Field.Name == row.Field).ToArray() ?? []))
            .Select(row => row.Diffs switch
            {
                [var diff] when (diff.Left.Number, diff.Right.Number, diff.Delta.Number) == (row.Stated.Internal, row.Stated.Counterparty, row.Stated.Delta) => null,
                [var diff] => $"{row.Stated.Case}: {row.Stated.Field} is {diff.Left.Number.ToInvariant()} to {diff.Right.Number.ToInvariant()} (delta {diff.Delta.Number.ToInvariant()}), the oracle says {row.Stated.Internal.ToInvariant()} to {row.Stated.Counterparty.ToInvariant()} ({row.Stated.Delta.ToInvariant()})",
                _ => $"{row.Stated.Case}: {row.Stated.Field} has no single field difference",
            })
            .OfType<string>()
            .ToArray();

        OracleAssert.None(disagreements);
    }

    // Fees is a field the oracle never states (the street schema has no commission column): the engine infers it, so a fee
    // break shows a Fees difference that the stated net cash difference is attributed to. A difference is explained when
    // the oracle states it, or when a difference the oracle states is attributed to it. Any other is a defect.
    // Left out: the signed-cash fee cases, whose net cash trading.netCash can't attribute to Fees (#433), and the 8 dealer-view
    // bonds whose fee is inferred before their cash is negated (#438).
    [Fact]
    public void The_engine_states_no_difference_the_oracle_does_not_unless_a_stated_one_is_attributed_to_it()
    {
        var stated = All.Value.GroupBy(item => item.Case, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => Field(item.Field)).OfType<string>().ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

        var disagreements = Dataset.Cases()
            .Where(item => SignedFeeCases.Contains(item.Id) is false && OracleDefects.DealerViewExact.Contains(item.Id) is false)
            .Select(item => (Case: item, Entry: EntryOf(item.Id)))
            .Where(pair => pair.Entry is not null)
            .SelectMany(pair => Unstated(pair.Entry!, stated.GetValueOrDefault(pair.Case.Id) ?? []).Select(diff => $"{pair.Case.Id}: {diff}"))
            .ToArray();

        OracleAssert.None(disagreements);
    }

    private static IEnumerable<string> Unstated(ResultEntry entry, HashSet<string> stated)
    {
        var attributedFromStated = entry.Diffs.Where(diff => stated.Contains(diff.Field.Name)).SelectMany(diff => diff.AttributedTo).ToHashSet(StringComparer.Ordinal);
        return entry.Diffs
            .Where(diff => diff.Outcome is not FieldOutcome.Exact && stated.Contains(diff.Field.Name) is false && attributedFromStated.Contains(diff.Field.Name) is false)
            .Select(diff => $"{diff.Field.Name} {diff.Outcome.ToInvariant()} {diff.Delta.Number.ToInvariant()} is not stated by the oracle");
    }

    // A near-match differs only within tolerance. Left out: the 9 signed near-matches (#433).
    [Fact]
    public void Every_difference_of_a_near_match_is_WithinTolerance()
    {
        var disagreements = Dataset.Cases()
            .Where(item => item.Kind is CaseKind.WithinTolerance && OracleDefects.SignedNearMatches.Contains(item.Id) is false)
            .Select(item => (Case: item, Outcomes: EntryOf(item.Id)?.Diffs.Where(diff => diff.Outcome is not FieldOutcome.Exact).Select(diff => diff.Outcome).ToArray() ?? []))
            .Where(pair => pair.Outcomes.Length == 0 || pair.Outcomes.Any(outcome => outcome is not FieldOutcome.WithinTolerance))
            .Select(pair => $"{pair.Case.Id}: field outcomes [{string.Join(", ", pair.Outcomes.Select(outcome => outcome.ToInvariant()))}]")
            .ToArray();

        OracleAssert.None(disagreements);
    }

    // A break has a Break difference in each root field the oracle names, and every other difference the oracle states is a
    // Break attributed to a root. Left out: the signed breaks (#433) and the swap coupon, whose engine roots differ.
    [Fact]
    public void A_break_is_Break_in_its_root_and_each_other_stated_difference_is_attributed_to_a_root()
    {
        var stated = All.Value.ToLookup(item => item.Case, StringComparer.Ordinal);
        var disagreements = Dataset.Cases()
            .Where(item => item.Kind is CaseKind.Break && OracleDefects.SignedBreaks.Contains(item.Id) is false && item.Id != OracleDefects.SwapCouponCase)
            .Select(item => (Case: item, Entry: EntryOf(item.Id), Roots: item.BreakFields.Select(Root).OfType<string>().ToArray()))
            .SelectMany(row => RootProblems(row.Case, row.Entry, row.Roots, stated[row.Case.Id].Select(item => Field(item.Field)).OfType<string>().ToArray()))
            .ToArray();

        OracleAssert.None(disagreements);
    }

    private static IEnumerable<string> RootProblems(OracleCase item, ResultEntry? entry, string[] roots, string[] statedFields)
    {
        var diffs = (entry?.Diffs ?? []).ToDictionary(diff => diff.Field.Name, StringComparer.Ordinal);
        var notBreak = roots.Where(root => diffs.TryGetValue(root, out var diff) is false || diff.Outcome is not FieldOutcome.Break).Select(root => $"{item.Id}: root {root} is not a Break");
        var unattributed = statedFields.Except(roots, StringComparer.Ordinal)
            .Where(field => diffs.TryGetValue(field, out var diff) is false || diff.Outcome is not FieldOutcome.Break || diff.AttributedTo.Intersect(roots, StringComparer.Ordinal).Any() is false)
            .Select(field => $"{item.Id}: {field} is not a Break attributed to a root");
        return [.. notBreak, .. unattributed];
    }
}
