using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class OpenWorkbookTests
{
    [Fact]
    public async Task Choosing_data_in_an_open_workbook_skips_the_file_dialog()
    {
        var excel = FakeExcel.ReadingTable();
        var dialog = new FakeDialog(@"C:\data\Other.xlsx");
        var side = Make.Side("Left", excel, dialog);

        await side.ChooseOpenWorkbookDataCommand.ExecuteAsync(null);

        Assert.Equal(0, dialog.Shown);
        Assert.Equal((0, 1, 1), (excel.Picks, excel.OpenPicks, excel.Reads));
        Assert.Equal(new WorkbookSource("Book.xlsx", @"C:\data\Book.xlsx", "Trades", "$A$1:$C$3"), side.Source);
        Assert.True(side.HasData);
    }

    [Fact]
    public async Task With_no_workbook_open_the_reason_is_shown()
    {
        var excel = new FakeExcel(
            new RangeChoice.Rejected("No workbook is open in Excel. Open one first, or use Excel file… to choose a file."),
            _ => throw new InvalidOperationException("must not read"));
        var side = Make.Side("Left", excel, new FakeDialog(null));

        await side.ChooseOpenWorkbookDataCommand.ExecuteAsync(null);

        Assert.Equal("No workbook is open in Excel. Open one first, or use Excel file… to choose a file.", side.Problem);
        Assert.Equal(new NoSource(), side.Source);
    }

    [Fact]
    public async Task A_workbook_that_was_never_saved_is_kept_without_a_path()
    {
        var unsaved = FakeExcel.Range with { Workbook = "Book1", Path = null };
        var excel = new FakeExcel(new RangeChoice.Picked(unsaved), range => Task.FromResult<SourceRead>(new SourceRead.Read(range, FakeExcel.Table, SourcePreview.Of(FakeExcel.Table))));
        var side = Make.Side("Left", excel, new FakeDialog(null));

        await side.ChooseOpenWorkbookDataCommand.ExecuteAsync(null);

        Assert.Equal(new WorkbookSource("Book1", null, "Trades", "$A$1:$C$3"), side.Source);
    }

    [Fact]
    public async Task A_failed_read_clears_the_source_so_the_setup_never_points_at_data_that_was_not_read()
    {
        var excel = FakeExcel.Reading(new SourceRead.Rejected("Cell B3 of [Book.xlsx]Trades holds #N/A."));
        var side = Make.Side("Left", excel, new FakeDialog(null));

        await side.ChooseOpenWorkbookDataCommand.ExecuteAsync(null);

        Assert.Equal(new NoSource(), side.Source);
        Assert.False(side.HasData);
    }
}
