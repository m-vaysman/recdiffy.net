using System.Globalization;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class PreviewAndCultureTests
{
    [Fact]
    public void A_small_table_is_previewed_whole()
    {
        var preview = SourcePreview.Of(FakeExcel.Table);

        Assert.Equal(FakeExcel.Table.Headers, preview.Headers);
        Assert.Equal([["T-1", "10", "1.5"], ["T-2", "20", "2.5"]], preview.Render(CultureInfo.InvariantCulture).Rows);
        Assert.Equal(2, preview.RowCount);
    }

    [Fact]
    public void Values_other_than_dates_are_previewed_as_invariant_text()
    {
        object?[] values = ["T-1", 1500.6m, null, true, 0.032600000000000004];

        var texts = values.Select(value => SourcePreview.Text(value, CultureInfo.GetCultureInfo("de-DE")));

        Assert.Equal(["T-1", "1500.6", string.Empty, "True", "0.032600000000000004"], texts, StringComparer.Ordinal);
    }

    [Fact]
    public void A_sample_is_spread_across_the_rows_sorted_and_the_same_every_time()
    {
        var sample = SourcePreview.SampleIndexes(1_000_000, 5, SourcePreview.Seed);

        Assert.Equal(5, sample.Distinct().Count());
        Assert.Equal(sample.Order(), sample);
        Assert.All(sample, index => Assert.InRange(index, 0, 999_999));
        Assert.Equal(sample, SourcePreview.SampleIndexes(1_000_000, 5, SourcePreview.Seed));
        Assert.True(sample[^1] - sample[0] > 100_000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void Up_to_the_sample_size_every_row_is_shown(int count) =>
        Assert.Equal(Enumerable.Range(0, count), SourcePreview.SampleIndexes(count, 5, SourcePreview.Seed));

    [Fact]
    public async Task The_preview_caption_counts_the_sample_and_every_row()
    {
        var side = Make.Side("Left", FakeExcel.ReadingTable(), new FakeDialog(@"C:\data\Book.xlsx"));
        Assert.Equal("No data chosen yet.", side.PreviewCaption);

        await side.ChooseExcelDataCommand.ExecuteAsync(null);

        Assert.Equal("2 sample rows of 2 rows", side.PreviewCaption);
    }

    [Fact]
    public void The_culture_list_starts_with_invariant_and_holds_excels()
    {
        var excel = CultureChoice.Of(CultureInfo.GetCultureInfo("de-CH"));

        var choices = CultureChoice.ListWith(excel);

        Assert.Equal(CultureChoice.Invariant, choices[0]);
        Assert.Contains(excel, choices);
        Assert.Equal("de-CH", CultureChoice.Find(choices, "de-CH").Name);
    }

    [Fact]
    public void A_culture_missing_from_the_list_is_added_so_it_can_be_shown()
    {
        var unusual = new CultureChoice("x-test", "Test culture");

        Assert.Equal(unusual, CultureChoice.ListWith(unusual)[0]);
    }
}
