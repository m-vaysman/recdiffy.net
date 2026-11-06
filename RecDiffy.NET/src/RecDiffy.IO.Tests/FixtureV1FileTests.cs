using System.Globalization;
using RecDiffy.Tests.Acceptance;

namespace RecDiffy.IO.Tests;

// fixture-v1 end to end from files, with the profile, reference data and 310 expected cases of WorkbookAcceptanceTests.
public class FixtureV1FileTests
{
    private static readonly ExcelOptions LeftSheet = new() { HasHeader = true, Sheet = ExcelSheet.Numbered(1) };
    private static readonly ExcelOptions RightSheet = new() { HasHeader = true, Sheet = ExcelSheet.Numbered(2) };

    [Fact]
    public void The_workbook_read_through_FromExcel_satisfies_all_310_cases()
    {
        var result = FixtureV1.Run(FileSources.FromExcel("left", TestFiles.FixtureV1, LeftSheet), FileSources.FromExcel("right", TestFiles.FixtureV1, RightSheet));

        Assert.Equal(500, result.Rows.Count);
        Assert.Equal(310, FixtureV1.Satisfied(result));
    }

    [Theory]
    [InlineData(',')]
    [InlineData('\t')]
    public void Csv_files_written_from_the_fixture_sheets_satisfy_all_310_cases(char delimiter)
    {
        var (left, right) = CsvFiles(TestFiles.Folder(), delimiter);
        var options = new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Of(delimiter) };

        var result = FixtureV1.Run(FileSources.FromCsv("left", left, options), FileSources.FromCsv("right", right, options));

        Assert.Equal(500, result.Rows.Count);
        Assert.Equal(310, FixtureV1.Satisfied(result));
    }

    // Each side is its own source: any mix of formats gives the result the same data gives in matching formats.
    [Fact]
    public void Mixed_formats_on_each_side_give_the_same_result_as_matching_formats()
    {
        var (left, right) = CsvFiles(TestFiles.Folder(), ',');
        var csv = new CsvOptions { HasHeader = true, Delimiter = CsvDelimiter.Comma };

        var excelBoth = FixtureV1.Fingerprint(FixtureV1.Run(FileSources.FromExcel("left", TestFiles.FixtureV1, LeftSheet), FileSources.FromExcel("right", TestFiles.FixtureV1, RightSheet)));
        var csvBoth = FixtureV1.Fingerprint(FixtureV1.Run(FileSources.FromCsv("left", left, csv), FileSources.FromCsv("right", right, csv)));
        var csvLeft = FixtureV1.Fingerprint(FixtureV1.Run(FileSources.FromCsv("left", left, csv), FileSources.FromExcel("right", TestFiles.FixtureV1, RightSheet)));
        var excelLeft = FixtureV1.Fingerprint(FixtureV1.Run(FileSources.FromExcel("left", TestFiles.FixtureV1, LeftSheet), FileSources.FromCsv("right", right, csv)));

        Assert.Equal(excelBoth, csvBoth);
        Assert.Equal(excelBoth, csvLeft);
        Assert.Equal(excelBoth, excelLeft);
    }

    // A file against an existing source: one side from a file, the other from objects.
    [Fact]
    public void A_file_on_one_side_and_objects_on_the_other_give_the_same_result()
    {
        var rows = XlsxSheet.Read(TestFiles.FixtureV1, 2);
        var objects = RecordSources.FromObjects("right", rows, Map(XlsxSheet.Read(TestFiles.FixtureV1, 2).SelectMany(row => row.Keys).Distinct(StringComparer.Ordinal)));

        var mixed = FixtureV1.Run(FileSources.FromExcel("left", TestFiles.FixtureV1, LeftSheet), objects);

        Assert.Equal(310, FixtureV1.Satisfied(mixed));
    }

    // CSV text written from the workbook cells as the independent test reader sees them: numbers in invariant form,
    // dates as yyyy-MM-dd. Column order follows the sheet's header row.
    private static (string Left, string Right) CsvFiles(string folder, char delimiter) =>
        (Write(folder, "left.csv", 1, delimiter), Write(folder, "right.csv", 2, delimiter));

    private static string Write(string folder, string name, int sheet, char delimiter)
    {
        var headers = FileSources.PeekExcel(TestFiles.FixtureV1, new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(sheet) }, 0, 0).Headers;
        var rows = XlsxSheet.Read(TestFiles.FixtureV1, sheet);
        IReadOnlyList<string?>[] lines = [headers, .. rows.Select(row => headers.Select(header => Text(row.GetValueOrDefault(header))).ToArray())];
        return TestFiles.Csv(folder, name, lines, delimiter);
    }

    private static string? Text(object? value) => value switch
    {
        null => null,
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static SourceMap<IReadOnlyDictionary<string, object?>> Map(IEnumerable<string> columns) =>
        columns.Aggregate(new SourceMap<IReadOnlyDictionary<string, object?>>(), (map, column) => map.Text(column, row => Text(row.GetValueOrDefault(column))));
}
