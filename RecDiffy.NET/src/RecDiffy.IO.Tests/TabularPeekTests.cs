using System.Globalization;

namespace RecDiffy.IO.Tests;

public class TabularPeekTests
{
    private static readonly CsvOptions Options = new() { HasHeader = true, Delimiter = CsvDelimiter.Comma };

    private static string File1000() =>
        TestFiles.Csv(TestFiles.Folder(), "rows.csv", [["Id", "Value"], .. Enumerable.Range(0, 1000).Select(index => new[] { index.ToString(CultureInfo.InvariantCulture), $"v{index}" })], ',');

    [Fact]
    public void The_same_seed_gives_the_same_sample()
    {
        var path = File1000();

        var first = FileSources.PeekCsv(path, Options, 25, seed: 7);
        var again = FileSources.PeekCsv(path, Options, 25, seed: 7);

        Assert.Equal(first.Rows.Select(row => row.Index), again.Rows.Select(row => row.Index));
        Assert.NotEqual(first.Rows.Select(row => row.Index), FileSources.PeekCsv(path, Options, 25, seed: 8).Rows.Select(row => row.Index));
    }

    [Fact]
    public void The_sample_comes_from_across_the_whole_file_in_file_order()
    {
        var peek = FileSources.PeekCsv(File1000(), Options, 25, seed: 1);

        Assert.Equal(25, peek.Rows.Count);
        Assert.Equal(1000, peek.RowCount);
        Assert.Equal(peek.Rows.Select(row => row.Index).Order(), peek.Rows.Select(row => row.Index));
        Assert.Contains(peek.Rows, row => row.Index >= 500);
        Assert.Contains(peek.Rows, row => row.Index < 500);
        Assert.All(peek.Rows, row => Assert.Equal($"v{row.Index}", row.Values[1]));
    }

    [Fact]
    public void A_sample_larger_than_the_file_returns_every_row()
    {
        var peek = FileSources.PeekCsv(File1000(), Options, 5000, seed: 1);

        Assert.Equal(Enumerable.Range(0, 1000).Select(index => (long)index), peek.Rows.Select(row => row.Index));
        Assert.Equal(["Id", "Value"], peek.Headers, StringComparer.Ordinal);
    }

    [Fact]
    public void A_sample_of_zero_still_reads_the_headers_and_counts_the_rows()
    {
        var peek = FileSources.PeekCsv(File1000(), Options, 0, seed: 1);

        Assert.Empty(peek.Rows);
        Assert.Equal(1000, peek.RowCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => FileSources.PeekCsv(File1000(), Options, -1, seed: 1));
    }

    // A peek shows cells as the reader returns them: an empty CSV cell is empty text, an empty Excel cell is null.
    // The engine treats both as missing.
    [Fact]
    public void Empty_cells_are_shown_as_the_reader_returns_them()
    {
        var csv = TestFiles.Csv(TestFiles.Folder(), "gaps.csv", [["Id", "Value"], ["1", null]], ',');
        var excel = TestFiles.Xlsx(TestFiles.Folder(), "gaps.xlsx", new Sheet("Gaps", [["Id", "Value"], ["1", null]]));

        Assert.Equal("", Assert.Single(FileSources.PeekCsv(csv, Options, 10, 0).Rows).Values[1]);
        Assert.Null(Assert.Single(FileSources.PeekExcel(excel, new ExcelOptions { HasHeader = true, Sheet = ExcelSheet.Numbered(1) }, 10, 0).Rows).Values[1]);
    }
}
