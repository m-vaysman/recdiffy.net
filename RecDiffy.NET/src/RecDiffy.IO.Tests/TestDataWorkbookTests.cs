namespace RecDiffy.IO.Tests;

public class TestDataWorkbookTests
{
    public static TheoryData<string> Workbooks =>
    [
        .. Directory.EnumerateFiles(TestFiles.TestData, "*.xlsx", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(TestFiles.TestData, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal),
    ];

    // Every sheet of every workbook under test-data goes through FromExcel and a run. The two input tabs (left and right)
    // of every workbook read every row. An oracle tab after them either reads, or is refused with a clear message
    // because its header row leaves a column unnamed: no sheet fails any other way.
    [Theory]
    [MemberData(nameof(Workbooks))]
    public void Every_sheet_reads_or_is_refused_for_its_header_row(string workbook)
    {
        var path = Path.Combine(TestFiles.TestData, workbook);
        var sheets = FileSources.ExcelSheetNames(path);

        var outcomes = sheets.Select((name, index) => (Name: name, Outcome: Record.Exception(() => Rows(path, index + 1)))).ToArray();

        Assert.Equal(3, sheets.Count);
        Assert.All(outcomes.Take(2), outcome => Assert.Null(outcome.Outcome));
        Assert.All(outcomes.Skip(2).Where(outcome => outcome.Outcome is not null), outcome =>
            Assert.Contains("have no name", Assert.IsType<InvalidDataException>(outcome.Outcome).Message, StringComparison.Ordinal));
    }

    private static int Rows(string path, int sheet)
    {
        var options = new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(sheet) };
        var result = Probe.Run(FileSources.FromExcel("left", path, options), Probe.Empty(), """{ "name": "Unused", "type": "Text", "role": "Text" }""", "");
        Assert.Equal(FileSources.PeekExcel(path, options, 0, 0).RowCount, result.Rows.Length);
        return result.Rows.Length;
    }
}
