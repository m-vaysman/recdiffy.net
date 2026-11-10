using ExcelDna.Integration;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.Tests;

public class ExcelBlocksTests
{
    private static readonly SheetRow Heading = new(SheetRowKind.Heading, [new SheetValue.Text("Outcome"), new SheetValue.Text("Left key")]);

    [Fact]
    public void Each_page_is_split_into_blocks_that_say_where_they_start_and_how_many_results_are_written_after_them()
    {
        var sheet = Sheet(
            new SheetPage([Heading, Result("A"), Result("B"), Result("C"), Result("D")], 1),
            new SheetPage([Heading, Result("E"), Plain("note"), Result("F")], 1));

        var blocks = ExcelBlocks.Split(sheet, 2);

        Assert.Equal(
            [(0, 0, 2, 1), (0, 2, 2, 3), (0, 4, 1, 4), (1, 0, 2, 5), (1, 2, 2, 6)],
            blocks.Select(block => (block.Page, block.FirstRow, block.Rows.Count, block.ResultsAfter)));
    }

    [Fact]
    public void A_block_is_one_rectangle_and_short_rows_are_filled_with_empty_cells()
    {
        var block = new SheetBlock(0, 0, [Heading, Plain("only")], 0);

        var values = ExcelBlocks.Values(block);

        Assert.Equal((2, 2), (values.GetLength(0), values.GetLength(1)));
        Assert.Equal(["Outcome", "Left key", "only", ExcelEmpty.Value], values.Cast<object>());
    }

    [Fact]
    public void Five_thousand_rows_go_to_Excel_at_a_time()
    {
        var sheet = Sheet(new SheetPage([Heading, .. Enumerable.Range(0, 12_000).Select(index => Result($"T-{index}"))], 1));

        var blocks = ExcelBlocks.Split(sheet, ExcelBlocks.BlockRows);

        Assert.Equal([5_000, 5_000, 2_001], blocks.Select(block => block.Rows.Count));
        Assert.Equal(12_000, blocks[^1].ResultsAfter);
    }

    [Fact]
    public void A_date_run_spans_text_and_empty_cells_and_stops_at_a_number_or_another_kind_of_date()
    {
        var day = new SheetValue.Date(new DateTime(2026, 11, 5), HasTime: false);
        var moment = new SheetValue.Date(new DateTime(2026, 11, 5, 9, 30, 0), HasTime: true);
        var page = new SheetPage(
            [
                Row(moment),
                Row(new SheetValue.Text("label")),
                Row(new SheetValue.Number(3)),
                Row(day),
                Row(SheetValue.Empty),
                Row(new SheetValue.Text("2026-11-05, 2026-11-06")),
                Row(day),
                Row(moment),
            ],
            0);

        Assert.Equal(
            [new DateRun(0, 0, 0, true), new DateRun(0, 3, 6, false), new DateRun(0, 7, 7, true)],
            ExcelBlocks.DateRuns(page));
    }

    [Fact]
    public void Dates_are_found_in_each_column()
    {
        var day = new SheetValue.Date(new DateTime(2026, 11, 5), HasTime: false);
        var page = new SheetPage(
            [
                new SheetRow(SheetRowKind.Result, [new SheetValue.Text("Matched · exact"), day, SheetValue.Empty, day]),
                new SheetRow(SheetRowKind.Result, [new SheetValue.Text("Left only"), day]),
            ],
            0);

        Assert.Equal([new DateRun(1, 0, 1, false), new DateRun(3, 0, 0, false)], ExcelBlocks.DateRuns(page));
    }

    [Fact]
    public void Neighbouring_heading_rows_are_one_run()
    {
        var page = new SheetPage([Heading, Plain("a"), Heading, Heading, Result("b"), Heading], 0);

        Assert.Equal([new RowRun(0, 0), new RowRun(2, 3), new RowRun(5, 5)], ExcelBlocks.HeadingRuns(page));
    }

    private static ResultSheet Sheet(params IReadOnlyList<SheetPage> pages) =>
        new("RecDiffy", pages, pages.Sum(page => page.Rows.Count(row => row.Kind is SheetRowKind.Result)), new SheetCell(0, 1), ResultSheets.CompleteStatus, IsStale: false);

    private static SheetRow Result(string key) => new(SheetRowKind.Result, [new SheetValue.Text("Matched · exact"), new SheetValue.Text(key)]);

    private static SheetRow Plain(string text) => new(SheetRowKind.Plain, [new SheetValue.Text(text)]);

    private static SheetRow Row(SheetValue value) => new(SheetRowKind.Result, [value]);
}
