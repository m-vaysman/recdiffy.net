using RecDiffy.Results;

namespace RecDiffy.IO.Tests;

public class ExcelSourceTests
{
    private const string Fields = """
        { "name": "Id", "type": "Identifier", "role": "Key" },
        { "name": "Day", "type": "Date", "role": "Economic" },
        { "name": "Amount", "type": "Decimal", "scale": 2, "role": "Economic" }
        """;

    private const string Rules = """
        "identify": [ { "rule": "identifier.map", "from": "Id", "to": "Id", "scheme": "ID" } ],
        "parse": [ { "rule": "date.parse", "from": "Day", "to": "Day" }, { "rule": "decimal.parse", "from": "Amount", "to": "Amount" } ]
        """;

    private static readonly Sheet First = new("First", [["Id", "Day", "Amount"], ["F-1", new DateOnly(2026, 11, 2), 10.5m]]);
    private static readonly Sheet Second = new("Second", [["Id", "Day", "Amount"], ["S-1", new DateOnly(2026, 11, 3), 20m], ["S-2", new DateOnly(2026, 11, 4), 30m]]);

    [Fact]
    public void A_sheet_is_chosen_by_name()
    {
        var book = TestFiles.Xlsx(TestFiles.Folder(), "book.xlsx", First, Second);

        Assert.Equal(["S-1", "S-2"], Ids(Read(book, ExcelSheet.Named("Second"))), StringComparer.Ordinal);
    }

    // Sheet numbers are 1-based on purpose: 1 is the first tab, as users count tabs in Excel.
    [Fact]
    public void Sheet_1_is_the_first_tab_and_sheet_2_the_second()
    {
        var book = TestFiles.Xlsx(TestFiles.Folder(), "book.xlsx", First, Second);

        Assert.Equal(["F-1"], Ids(Read(book, ExcelSheet.Numbered(1))), StringComparer.Ordinal);
        Assert.Equal(["S-1", "S-2"], Ids(Read(book, ExcelSheet.Numbered(2))), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void A_sheet_number_out_of_range_fails_and_lists_the_sheets(int number)
    {
        var book = TestFiles.Xlsx(TestFiles.Folder(), "book.xlsx", First, Second);

        var failure = Assert.Throws<ArgumentOutOfRangeException>(() => Read(book, ExcelSheet.Numbered(number)));

        Assert.Contains($"Sheet number {number} is out of range", failure.Message, StringComparison.Ordinal);
        Assert.Contains("sheet numbers start at 1", failure.Message, StringComparison.Ordinal);
        Assert.Contains("2 sheet(s): 1 'First', 2 'Second'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sheet_name_that_does_not_exist_fails_and_lists_the_sheets_without_falling_back()
    {
        var book = TestFiles.Xlsx(TestFiles.Folder(), "book.xlsx", First, Second);

        var failure = Assert.Throws<ArgumentException>(() => Read(book, ExcelSheet.Named("Third")));

        Assert.Contains("Sheet 'Third' was not found", failure.Message, StringComparison.Ordinal);
        Assert.Contains("1 'First', 2 'Second'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hidden_tab_counts_in_sheet_numbers_and_can_be_read()
    {
        var book = TestFiles.Xlsx(TestFiles.Folder(), "book.xlsx", First, Second with { Hidden = true });

        Assert.Equal(["S-1", "S-2"], Ids(Read(book, ExcelSheet.Numbered(2))), StringComparer.Ordinal);
        Assert.Equal(["S-1", "S-2"], Ids(Read(book, ExcelSheet.Named("Second"))), StringComparer.Ordinal);
    }

    [Fact]
    public void Data_starting_at_A1_is_read_with_row_1_as_the_header()
    {
        var book = TestFiles.Xlsx(TestFiles.Folder(), "book.xlsx", First);

        var peek = FileSources.PeekExcel(book, new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(1) }, 10, 0);

        Assert.Equal(["Id", "Day", "Amount"], peek.Headers, StringComparer.Ordinal);
        Assert.Equal(1, peek.RowCount);
    }

    // Data must start at A1. A header written in column B leaves A1 empty, so the header row names no column A: refused.
    [Fact]
    public void A_header_that_does_not_start_at_A1_is_refused()
    {
        var book = TestFiles.Xlsx(TestFiles.Folder(), "book.xlsx", new Sheet("Shifted", [[null, "Id", "Day", "Amount"], [null, "F-1", new DateOnly(2026, 11, 2), 10.5m]]));

        var failure = Assert.Throws<InvalidDataException>(() => Read(book, ExcelSheet.Numbered(1)));

        Assert.Contains("column(s) 1 have no name", failure.Message, StringComparison.Ordinal);
    }

    // Row 1 is the header whatever it holds. A blank first row is not skipped to find one further down: without a
    // header it is a row of empty cells, and with one it names no columns, which is refused.
    [Fact]
    public void A_blank_first_row_is_not_skipped_to_find_the_header()
    {
        var book = TestFiles.Xlsx(TestFiles.Folder(), "book.xlsx", First with { FirstRow = 2 });

        var headerless = FileSources.PeekExcel(book, new ExcelOptions { HasHeader = false, Sheet = ExcelSheet.Numbered(1) }, 10, 0);
        var failure = Assert.Throws<InvalidDataException>(() => Read(book, ExcelSheet.Numbered(1)));

        Assert.Equal(3, headerless.RowCount);
        Assert.All(headerless.Rows[0].Values, Assert.Null);
        Assert.Contains("is empty", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_date_formatted_cell_reaches_the_profile_as_a_date()
    {
        var book = TestFiles.Xlsx(TestFiles.Folder(), "book.xlsx", First);

        var peek = FileSources.PeekExcel(book, new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(1) }, 10, 0);
        var row = Assert.Single(Read(book, ExcelSheet.Numbered(1)));

        Assert.IsType<DateTime>(peek.Rows[0].Values[1]);
        Assert.Equal(new DateOnly(2026, 11, 2), row["Day"].Date);
    }

    [Fact]
    public void A_text_id_keeps_its_leading_zeros()
    {
        var book = TestFiles.Xlsx(TestFiles.Folder(), "book.xlsx", new Sheet("Ids", [["Id", "Day", "Amount"], ["00123", new DateOnly(2026, 1, 1), 1m]]));

        Assert.Equal(["00123"], Ids(Read(book, ExcelSheet.Numbered(1))), StringComparer.Ordinal);
    }

    // A workbook stores numbers as text, and the engine never interprets a double (#158). Whole numbers and fractions
    // both reach the engine as the decimal the workbook stores, and round to the field's scale.
    [Fact]
    public void Numbers_reach_the_engine_as_the_decimal_the_workbook_stores()
    {
        var book = TestFiles.Xlsx(TestFiles.Folder(), "book.xlsx", new Sheet("Numbers", [["Id", "Day", "Amount"], ["A", new DateOnly(2026, 1, 1), 2940000], ["B", new DateOnly(2026, 1, 1), 1.094d]]));

        var peek = FileSources.PeekExcel(book, new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(1) }, 10, 0);
        var rows = Read(book, ExcelSheet.Numbered(1));

        Assert.Equal([2940000m, 1.094m], peek.Rows.Select(row => Assert.IsType<decimal>(row.Values[2])));
        Assert.Equal([2940000m, 1.09m], rows.Select(row => row["Amount"].Number));
    }

    [Fact]
    public void A_headerless_sheet_is_mapped_by_position_and_row_1_is_data()
    {
        var book = TestFiles.Xlsx(TestFiles.Folder(), "book.xlsx", new Sheet("Data", [["F-1", new DateOnly(2026, 11, 2), 10.5m], ["F-2", new DateOnly(2026, 11, 3), 11m]]));
        const string positional = """
            "identify": [ { "rule": "identifier.map", "from": "Column1", "to": "Id", "scheme": "ID" } ],
            "parse": [ { "rule": "date.parse", "from": "Column2", "to": "Day" }, { "rule": "decimal.parse", "from": "Column3", "to": "Amount" } ]
            """;

        var rows = Probe.Left(FileSources.FromExcel("left", book, new ExcelOptions { HasHeader = false, Sheet = ExcelSheet.Numbered(1) }), Fields, positional);

        Assert.Equal(["F-1", "F-2"], rows.Select(row => row.Text("Id")), StringComparer.Ordinal);
    }

    [Fact]
    public void A_stream_is_read_once_in_the_stated_format_and_left_open()
    {
        var book = TestFiles.Xlsx(TestFiles.Folder(), "book.xlsx", First, Second);
        using var stream = new MemoryStream(File.ReadAllBytes(book));
        var source = FileSources.FromExcel("left", stream, ExcelFormat.Xlsx, new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(2) });

        Assert.Equal(["S-1", "S-2"], Probe.Left(source, Fields, Rules).Select(row => row.Text("Id")), StringComparer.Ordinal);
        Assert.True(stream.CanRead);
        Assert.Throws<InvalidOperationException>(() => Probe.Left(source, Fields, Rules));
    }

    private static IReadOnlyList<CanonicalRow> Read(string book, ExcelSheet sheet) =>
        Probe.Left(FileSources.FromExcel("left", book, new ExcelOptions { HasHeader = true, Sheet = sheet }), Fields, Rules);

    private static IEnumerable<string> Ids(IReadOnlyList<CanonicalRow> rows) => rows.Select(row => row.Text("Id"));
}
