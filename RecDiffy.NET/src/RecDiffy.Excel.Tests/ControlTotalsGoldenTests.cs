using System.Globalization;
using RecDiffy.Excel.Tests.Support;
using RecDiffy.Results;

namespace RecDiffy.Excel.Tests;

// MO1 is real engine output on fixture-v1, so it is a numeric golden source: the reference workbook in docs/report-specs
// was built from the same run and saved by LibreOffice with its values.
public class ControlTotalsGoldenTests
{
    private const string Sheet = "MO1_ControlTotals";
    private const string ReferenceSheet = "MO1_Control_Totals";

    private static string ReferenceWorkbook => Path.Combine(Paths.Specs, "reference-workbooks", "RecDiffy_Engine_MO1_Control_Totals_fixture-v1.xlsx");

    [Fact]
    public void Engine_side_by_side_tab_equals_BuildSideBySide_for_every_currency()
    {
        var book = Book.Read(Write());
        var tab = book["Engine_SideBySide"];
        var expected = FixtureV1.Result.Rows
            .Where(row => row.Disposition is not Disposition.Suppressed)
            .Select(row => FixtureV1.Result.Canonical(row.Row).Text("Currency"))
            .Where(currency => currency.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .SelectMany(currency => FixtureV1.Result
                .BuildSideBySide(new SideBySideSpec(["Counterparty"], ["Asset"], [new MeasureSpec("Quantity")], new FilterSpec("Currency", currency)))
                .Lines.Select(line => (Key: $"{currency}|{line.Row}|{line.Column}", Cell: line.Cells["Quantity"])))
            .ToDictionary(line => line.Key, line => line.Cell, StringComparer.Ordinal);
        var actual = Rows(tab, 5)
            .ToDictionary(
                row => $"{row["A"]}|{row["B"]}|{row["C"]}",
                row => new SideBySideCell(Number(row["D"]), Number(row["E"]), Number(row["F"]), Number(row["G"]), Number(row["H"]), Number(row["I"])),
                StringComparer.Ordinal);

        Assert.Equal(expected.Count, actual.Count);
        Assert.All(expected, pair => Assert.Equal(pair.Value, actual[pair.Key]));
    }

    [Fact]
    public void Engine_side_by_side_tab_equals_the_reference_workbook()
    {
        var actual = Rows(Book.Read(Write())["Engine_SideBySide"], 5).ToDictionary(Key3, row => string.Join("|", Columns("D", "I").Select(column => Number(row[column]))), StringComparer.Ordinal);
        var expected = Rows(Book.Read(ReferenceWorkbook)["Engine_SideBySide"], 5).ToDictionary(Key3, row => string.Join("|", Columns("D", "I").Select(column => Number(row[column]))), StringComparer.Ordinal);

        Assert.Equal(expected, actual);
    }

    [LibreOfficeFact]
    public void Recalculated_report_equals_the_reference_workbook_line_by_line()
    {
        var recalculated = Book.Read(LibreOffice.Recalculate([Write()])[0]);
        var reference = Book.Read(ReferenceWorkbook);

        var actual = Parts(recalculated[Sheet]);
        var expected = Parts(reference[ReferenceSheet]);

        Assert.Equal(expected.Part1.Keys.Order(StringComparer.Ordinal), actual.Part1.Keys.Order(StringComparer.Ordinal));
        Assert.All(expected.Part1, pair => Assert.Equal(pair.Value, actual.Part1[pair.Key]));
        Assert.Equal(expected.Part2.Keys.Order(StringComparer.Ordinal), actual.Part2.Keys.Order(StringComparer.Ordinal));
        Assert.All(expected.Part2, pair => Assert.Equal(pair.Value, actual.Part2[pair.Key]));
        Assert.Equal(expected.Part3.Order(StringComparer.Ordinal), actual.Part3.Order(StringComparer.Ordinal));
    }

    private static string Write()
    {
        var outcome = Assert.Single(FixtureV1.Result.WriteAll(Paths.Scratch("mo1"), FixtureV1.Options(ReportKind.ControlTotals)));
        Assert.True(outcome.Written, outcome.SkipReason);
        return outcome.Path!;
    }

    private sealed record ControlParts(Dictionary<string, string> Part1, Dictionary<string, string> Part2, List<string> Part3);

    // Part 1 lines keyed by counterparty|asset, Part 2 lines by currency|counterparty|asset (the currency blocks may be in a different order),
    // Part 3 as entry|ours rows|theirs rows|quantities.
    private static ControlParts Parts(IReadOnlyDictionary<string, BookCell> sheet)
    {
        var rows = Rows(sheet, 1).ToArray();
        int Title(string prefix) => rows.Single(row => row["A"].StartsWith(prefix, StringComparison.Ordinal)).Number;
        var (part1, part2, part3) = (Title("Part 1"), Title("Part 2"), Title("Part 3"));
        var part1Rows = rows.Where(row => row.Number > part1 + 1 && row.Number < part2 && row["A"].Length > 0);
        var part2Rows = rows.Where(row => row.Number > part2 + 1 && row.Number < part3 && (row["C"].Length > 0 || row["B"].StartsWith("Subtotal", StringComparison.Ordinal)));
        var part3Rows = rows.Where(row => row.Number > part3 + 1 && row["H"].Length > 0);
        return new ControlParts(
            part1Rows.ToDictionary(row => $"{row["A"]}|{row["B"]}", row => string.Join("|", Columns("C", "K").Select(column => row[column])), StringComparer.Ordinal),
            part2Rows.ToDictionary(row => $"{row["A"]}|{row["B"]}|{row["C"]}", row => string.Join("|", Columns("D", "Q").Select(column => row[column])), StringComparer.Ordinal),
            [.. part3Rows.Select(row => string.Join("|", row["B"], row["C"], row["F"], row["G"], row["H"], row["I"]))]);
    }

    private static string Key3(Row row) => $"{row["A"]}|{row["B"]}|{row["C"]}";

    private static IEnumerable<string> Columns(string first, string last) =>
        Enumerable.Range(first[0], last[0] - first[0] + 1).Select(code => ((char)code).ToString());

    private sealed record Row(int Number, IReadOnlyDictionary<string, string> Cells)
    {
        public string this[string column] => Cells.TryGetValue(column, out var value) ? value : "";
    }

    private static IEnumerable<Row> Rows(IReadOnlyDictionary<string, BookCell> sheet, int first) =>
        sheet.Values
            .Select(cell => (Row: int.Parse(new string([.. cell.Address.SkipWhile(char.IsLetter)]), CultureInfo.InvariantCulture), Column: new string([.. cell.Address.TakeWhile(char.IsLetter)]), cell.Value))
            .Where(cell => cell.Row >= first && cell.Value is { Length: > 0 })
            .GroupBy(cell => cell.Row)
            .OrderBy(group => group.Key)
            .Select(group => new Row(group.Key, group.ToDictionary(cell => cell.Column, cell => Normalize(cell.Value!), StringComparer.Ordinal)));

    // Numbers compare as decimals so 26 and 26.0 agree.
    private static string Normalize(string value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number.ToString("0.############", CultureInfo.InvariantCulture) : value;

    private static decimal Number(string value) => decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
}
