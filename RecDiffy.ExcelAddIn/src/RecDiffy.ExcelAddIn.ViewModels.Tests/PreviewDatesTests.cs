using System.ComponentModel;
using System.Globalization;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class PreviewDatesTests
{
    private static readonly SourcePreview Dated = new(
        ["Ref", "Trade date", "Booked", "Cutoff", "Quantity"],
        [["T-1", new DateTime(2026, 11, 5), new DateTime(2026, 11, 5, 14, 30, 0), new TimeOnly(17, 30), 1500.5m]],
        1);

    [Theory]
    [InlineData("en-US", "11/5/2026")]
    [InlineData("de-DE", "05.11.2026")]
    [InlineData("en-GB", "05/11/2026")]
    [InlineData("ja-JP", "2026/11/05")]
    [InlineData("", "11/05/2026")]
    public void A_date_is_shown_in_the_sides_culture(string culture, string expected) =>
        Assert.Equal(expected, Dated.Render(CultureInfo.GetCultureInfo(culture)).Rows[0][1]);

    [Fact]
    public void Times_follow_the_culture_and_numbers_stay_as_RecDiffy_reads_them()
    {
        var german = Dated.Render(CultureInfo.GetCultureInfo("de-DE")).Rows[0];

        Assert.Equal(["T-1", "05.11.2026", "05.11.2026 14:30", "17:30", "1500.5"], german);
        Assert.Equal("5:30 PM", Dated.Render(CultureInfo.GetCultureInfo("en-US")).Rows[0][3]);
    }

    [Fact]
    public void A_time_with_seconds_shows_them() =>
        Assert.Equal("17:30:15", SourcePreview.Text(new TimeOnly(17, 30, 15), CultureInfo.GetCultureInfo("de-DE")));

    [Fact]
    public async Task Changing_a_sides_culture_redraws_its_preview_dates()
    {
        var excel = new FakeExcel(
            new RangeChoice.Picked(FakeExcel.Range),
            range => Task.FromResult<SourceRead>(new SourceRead.Read(range, FakeExcel.Table, Dated)));
        var side = Make.Side("Left", excel, new FakeDialog(@"C:\data\Book.xlsx"));
        await side.ChooseExcelDataCommand.ExecuteAsync(null);
        Assert.Equal("11/5/2026", side.PreviewTable!.Rows[0][1]);
        var changed = new List<string?>();
        side.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        side.Culture = CultureChoice.Of(CultureInfo.GetCultureInfo("de-DE"));

        Assert.Equal("05.11.2026", side.PreviewTable!.Rows[0][1]);
        Assert.Contains(nameof(SideViewModel.PreviewTable), changed, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Csv_text_is_shown_as_the_file_holds_it_whatever_the_culture()
    {
        var csv = new FakeCsv(_ => Task.FromResult<CsvPeek>(new CsvPeek.Read(new SourcePreview(["Date"], [["2026-11-05"]], 1))));
        var side = Make.Side("Right", FakeExcel.ReadingTable(), new FakeDialog(@"C:\data\trades.csv"), csv);
        await side.ChooseCsvFileCommand.ExecuteAsync(null);

        side.Culture = CultureChoice.Of(CultureInfo.GetCultureInfo("de-DE"));

        Assert.Equal("2026-11-05", side.PreviewTable!.Rows[0][0]);
    }

    [Fact]
    public void An_unknown_culture_name_renders_as_invariant() =>
        Assert.Equal(CultureInfo.InvariantCulture, new CultureChoice("not a culture!", "Unknown").ToCulture());

    [Fact]
    public void A_side_with_no_data_has_no_preview_table() =>
        Assert.Null(Make.Side("Left", FakeExcel.ReadingTable(), new FakeDialog(null)).PreviewTable);
}
