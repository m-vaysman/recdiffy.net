using System.ComponentModel;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class SourcesViewModelTests
{
    [Theory]
    [InlineData("Left", "", "Left")]
    [InlineData("Left", "   ", "Left")]
    [InlineData("Left", "Your records", "Left · Your records")]
    [InlineData("Right", "  Broker confirmations  ", "Right · Broker confirmations")]
    public void A_side_heading_shows_the_alias_only_when_there_is_one(string side, string alias, string expected) =>
        Assert.Equal(expected, SideViewModel.SideHeading(side, alias));

    [Fact]
    public void Typing_an_alias_updates_that_side_only()
    {
        var sources = Make.Sources(FakeExcel.ReadingTable(), new FakeDialog(null));
        var changed = Changes(sources.Left);

        sources.Left.Alias = "Your records";

        Assert.Equal("Left · Your records", sources.Left.Heading);
        Assert.Equal("Right", sources.Right.Heading);
        Assert.Equal([nameof(SideViewModel.Alias), nameof(SideViewModel.Heading)], changed);
    }

    [Fact]
    public async Task Choosing_excel_data_reads_the_picked_range_and_summarises_it()
    {
        var excel = FakeExcel.ReadingTable();
        var side = Make.Side("Left", excel, new FakeDialog(@"C:\data\Book.xlsx"));

        await side.ChooseExcelDataCommand.ExecuteAsync(null);

        Assert.Same(FakeExcel.Table, side.Table);
        Assert.True(side.HasData);
        Assert.Equal("Book.xlsx · Trades · $A$1:$C$3 · 2 rows · 3 columns", side.Summary);
        Assert.Null(side.Problem);
        Assert.False(side.IsReading);
        Assert.Equal(1, side.Progress);
    }

    [Fact]
    public async Task Cancelling_the_file_dialog_asks_excel_for_nothing()
    {
        var excel = FakeExcel.ReadingTable();
        var side = Make.Side("Left", excel, new FakeDialog(null));

        await side.ChooseExcelDataCommand.ExecuteAsync(null);

        Assert.Equal(0, excel.Picks);
        Assert.False(side.HasData);
    }

    [Fact]
    public async Task Cancelling_the_range_picker_reads_nothing_and_changes_nothing()
    {
        var excel = new FakeExcel(new RangeChoice.Canceled(), _ => throw new InvalidOperationException("must not read"));
        var side = Make.Side("Left", excel, new FakeDialog(@"C:\data\Book.xlsx"));

        await side.ChooseExcelDataCommand.ExecuteAsync(null);

        Assert.Equal(0, excel.Reads);
        Assert.False(side.HasData);
        Assert.Null(side.Problem);
    }

    [Fact]
    public async Task A_rejected_pick_shows_the_reason()
    {
        var excel = new FakeExcel(new RangeChoice.Rejected("Pick one rectangular range, not several."), _ => throw new InvalidOperationException("must not read"));
        var side = Make.Side("Left", excel, new FakeDialog(@"C:\data\Book.xlsx"));

        await side.ChooseExcelDataCommand.ExecuteAsync(null);

        Assert.Equal("Pick one rectangular range, not several.", side.Problem);
        Assert.False(side.HasData);
    }

    [Fact]
    public async Task A_rejected_read_shows_the_reason_and_drops_the_earlier_table()
    {
        var side = Make.Side("Left", FakeExcel.ReadingTable(), new FakeDialog(@"C:\data\Book.xlsx"));
        await side.ChooseExcelDataCommand.ExecuteAsync(null);
        var rejecting = Make.Side("Left", FakeExcel.Reading(new SourceRead.Rejected("Cell B3 of [Book.xlsx]Trades holds #N/A.")), new FakeDialog(@"C:\data\Book.xlsx"));

        await rejecting.ChooseExcelDataCommand.ExecuteAsync(null);

        Assert.True(side.HasData);
        Assert.False(rejecting.HasData);
        Assert.Equal(string.Empty, rejecting.Summary);
        Assert.Equal("Cell B3 of [Book.xlsx]Trades holds #N/A.", rejecting.Problem);
    }

    [Fact]
    public async Task The_sources_show_which_side_is_reading_until_the_read_finishes()
    {
        var pending = new TaskCompletionSource<SourceRead>();
        var excel = new FakeExcel(new RangeChoice.Picked(FakeExcel.Range), _ => pending.Task);
        var sources = Make.Sources(excel, new FakeDialog(@"C:\data\Book.xlsx"));

        var choosing = sources.Right.ChooseExcelDataCommand.ExecuteAsync(null);

        Assert.True(sources.IsReading);
        Assert.Same(sources.Right, sources.Reading);
        Assert.Equal("Reading the right data", sources.ReadingMessage);
        pending.SetResult(new SourceRead.Read(FakeExcel.Range, FakeExcel.Table, SourcePreview.Of(FakeExcel.Table)));
        await choosing;
        Assert.False(sources.IsReading);
        Assert.Equal(string.Empty, sources.ReadingMessage);
    }

    [Fact]
    public async Task Sources_are_complete_once_both_sides_hold_data()
    {
        var sources = Make.Sources(FakeExcel.ReadingTable(), new FakeDialog(@"C:\data\Book.xlsx"));
        Assert.False(sources.IsComplete);

        await sources.Left.ChooseExcelDataCommand.ExecuteAsync(null);
        Assert.False(sources.IsComplete);

        await sources.Right.ChooseExcelDataCommand.ExecuteAsync(null);
        Assert.True(sources.IsComplete);
    }

    private static List<string?> Changes(INotifyPropertyChanged source)
    {
        var names = new List<string?>();
        source.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        return names;
    }
}
