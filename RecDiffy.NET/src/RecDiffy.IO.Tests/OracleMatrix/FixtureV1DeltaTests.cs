using System.Globalization;
using RecDiffy.Results;
using RecDiffy.Tests.Acceptance;

namespace RecDiffy.IO.Tests.OracleMatrix;

// Fixture v1's Expected Results tab names the field of each break and, in InternalValue and CounterpartyValue, the two
// values (spec v3.1 §12.3 AC-3). FixtureV1OracleTests checks the field a break is blamed on; these check the numbers:
// every break whose two values are numbers has a field difference with the left value, the right value and their delta.
// Dates and codes are named, not measured.
public class FixtureV1DeltaTests
{
    private static readonly OracleDataset Dataset = OracleDatasets.FixtureV1;

    private static readonly Lazy<IReconciliationResult> Result = new(() => FixtureV1.Run(
        FileSources.FromExcel("left", Dataset.Workbook, new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(1) }),
        FileSources.FromExcel("right", Dataset.Workbook, new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(2) })));

    private sealed record Stated(string Case, string Column, decimal Internal, decimal Counterparty);

    // The break columns that are numbers in the Expected Results tab, as the profile names their fields.
    private static string? Field(string column) => column switch
    {
        "PriceOrRate" => "Price",
        "Quantity" or "FixedRate" => column,
        _ => null,
    };

    // The tab writes Excel's binary floats (0.033100000000000004), so each value is read at the field's scale: 2 for a
    // quantity, 6 for a rate or price (spec v3.1 §7.3). A counterparty quantity is signed in the tab, and quantities are compared unsigned.
    private static decimal Normal(string column, decimal value) =>
        column is "Quantity" ? decimal.Round(decimal.Abs(value), 2, MidpointRounding.AwayFromZero) : decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    private static IReadOnlyList<Stated> Numeric() =>
    [
        .. XlsxSheet.Read(Dataset.Workbook, 3)
            .Where(row => Text(row, "ExpectedOutcome") == "FIELD_BREAK")
            .Select(row => (Id: Text(row, "CaseID"), Column: Text(row, "BreakField"), Internal: Number(row, "InternalValue"), Counterparty: Number(row, "CounterpartyValue")))
            .Where(row => row.Id is not null && row.Column is not null && row.Internal is not null && row.Counterparty is not null)
            .Select(row => new Stated(row.Id!, row.Column!, Normal(row.Column!, row.Internal!.Value), Normal(row.Column!, row.Counterparty!.Value))),
    ];

    [Fact]
    public void The_tab_states_ten_numeric_breaks_five_quantity_three_price_and_two_fixed_rate() =>
        Assert.Equal(
            ["FixedRate:2", "PriceOrRate:3", "Quantity:5"],
            Numeric().GroupBy(item => item.Column, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key}:{group.Count()}"),
            StringComparer.Ordinal);

    [Fact]
    public void Every_numeric_break_has_a_field_difference_with_the_stated_values_and_their_delta()
    {
        var cases = Oracles.FixtureV1(Dataset.Workbook).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var entries = Result.Value.Entries.ToDictionary(entry => entry.Id);
        var rows = Result.Value.Rows.ToDictionary(row => (row.Row.Source, row.Row.RowKey));

        var disagreements = Numeric().Select(stated => Disagreement(stated, cases[stated.Case], entries, rows)).OfType<string>().ToArray();

        OracleAssert.None(disagreements);
    }

    private static string? Disagreement(Stated stated, OracleCase item, Dictionary<GroupId, ResultEntry> entries, Dictionary<(string Source, string RowKey), RowResult> rows)
    {
        var entry = item.Left.Select(key => rows.GetValueOrDefault(("left", key))?.EntryId).FirstOrDefault(id => id is not null) is { } id ? entries.GetValueOrDefault(id) : null;
        var field = Field(stated.Column);
        return (entry, field) switch
        {
            (null, _) => $"{stated.Case}: no entry",
            (_, null) => $"{stated.Case}: {stated.Column} has no profile field",
            ({ } found, { } name) => found.Diffs.Where(diff => diff.Field.Name == name).ToArray() switch
            {
                [var diff] when (diff.Left.Number, diff.Right.Number, diff.Delta.Number) == (stated.Internal, stated.Counterparty, stated.Counterparty - stated.Internal) => null,
                [var diff] => $"{stated.Case}: {name} is {diff.Left.Number.ToInvariant()} to {diff.Right.Number.ToInvariant()} (delta {diff.Delta.Number.ToInvariant()}), the tab says {stated.Internal.ToInvariant()} to {stated.Counterparty.ToInvariant()}",
                _ => $"{stated.Case}: {name} has no single difference",
            },
        };
    }

    private static string? Text(IReadOnlyDictionary<string, object?> row, string column) => row.GetValueOrDefault(column) switch
    {
        null => null,
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        var value => value.ToString(),
    };

    // A cell that is a number, or text that reads as an invariant number. A date or a code is not one.
    private static decimal? Number(IReadOnlyDictionary<string, object?> row, string column) => row.GetValueOrDefault(column) switch
    {
        decimal number => number,
        string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number,
        _ => null,
    };
}
