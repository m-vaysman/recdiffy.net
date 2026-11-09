using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class CsvSourceTests
{
    private const string File = @"C:\data\trades.csv";

    [Fact]
    public async Task Choosing_a_csv_file_previews_it_with_the_default_options()
    {
        var csv = FakeCsv.Reading();
        var side = Make.Side("Right", FakeExcel.ReadingTable(), new FakeDialog(File), csv);

        await side.ChooseCsvFileCommand.ExecuteAsync(null);

        Assert.Equal(CsvSource.At(File), side.Source);
        Assert.Equal([CsvSource.At(File)], csv.Peeked);
        Assert.Same(FakeCsv.Preview, side.Preview);
        Assert.True(side.HasData);
        Assert.True(side.IsCsv);
        Assert.Equal("trades.csv · 2 rows · 2 columns", side.Summary);
    }

    [Fact]
    public async Task The_defaults_are_a_header_row_commas_double_quotes_and_utf8()
    {
        var side = Make.Side("Right", FakeExcel.ReadingTable(), new FakeDialog(File));

        await side.ChooseCsvFileCommand.ExecuteAsync(null);

        Assert.True(side.CsvHasHeader);
        Assert.Equal(DelimiterChoice.All[0], side.CsvDelimiter);
        Assert.Equal(QuoteChoice.All[0], side.CsvQuote);
        Assert.Equal(EscapeChoice.All[0], side.CsvEscape);
        Assert.Equal(EncodingChoice.All[0], side.CsvEncoding);
    }

    [Fact]
    public async Task Changing_an_option_reads_the_file_again_with_it()
    {
        var csv = FakeCsv.Reading();
        var side = Make.Side("Right", FakeExcel.ReadingTable(), new FakeDialog(File), csv);
        await side.ChooseCsvFileCommand.ExecuteAsync(null);

        side.CsvDelimiter = DelimiterChoice.For(';');
        side.CsvEncoding = EncodingChoice.For("windows-1252");
        side.CsvHasHeader = false;

        Assert.Equal(
            CsvSource.At(File) with { Delimiter = ';', Encoding = "windows-1252", HasHeader = false },
            csv.Peeked[^1]);
        Assert.Equal(4, csv.Peeked.Count);
    }

    [Fact]
    public async Task Choosing_an_option_that_is_already_set_reads_nothing()
    {
        var csv = FakeCsv.Reading();
        var side = Make.Side("Right", FakeExcel.ReadingTable(), new FakeDialog(File), csv);
        await side.ChooseCsvFileCommand.ExecuteAsync(null);

        side.CsvDelimiter = DelimiterChoice.For(',');

        Assert.Single(csv.Peeked);
    }

    [Theory]
    [InlineData('"', '\'', '\'')]
    [InlineData('\\', '\'', '\\')]
    public async Task A_new_quote_keeps_a_doubled_escape_doubled_and_a_backslash_a_backslash(char escape, char quote, char expectedEscape)
    {
        var side = Make.Side("Right", FakeExcel.ReadingTable(), new FakeDialog(File));
        await side.ChooseCsvFileCommand.ExecuteAsync(null);
        side.CsvEscape = EscapeChoice.For(escape, '"');

        side.CsvQuote = QuoteChoice.For(quote);

        var source = Assert.IsType<CsvSource>(side.Source);
        Assert.Equal((quote, expectedEscape), (source.Quote, source.Escape));
    }

    [Fact]
    public async Task A_new_file_keeps_the_options_chosen_for_the_last_one()
    {
        var paths = new Queue<string>([File, @"C:\data\later.csv"]);
        var side = Make.Side("Right", FakeExcel.ReadingTable(), new QueueDialog(paths));
        await side.ChooseCsvFileCommand.ExecuteAsync(null);
        side.CsvDelimiter = DelimiterChoice.For('|');

        await side.ChooseCsvFileCommand.ExecuteAsync(null);

        Assert.Equal(CsvSource.At(@"C:\data\later.csv") with { Delimiter = '|' }, side.Source);
    }

    [Fact]
    public async Task A_rejected_file_shows_the_reason_and_keeps_its_options_to_fix()
    {
        var csv = new FakeCsv(_ => Task.FromResult<CsvPeek>(new CsvPeek.Rejected("trades.csv has bytes that aren't valid UTF-8. Choose the file's encoding.")));
        var side = Make.Side("Right", FakeExcel.ReadingTable(), new FakeDialog(File), csv);

        await side.ChooseCsvFileCommand.ExecuteAsync(null);

        Assert.False(side.HasData);
        Assert.Equal("trades.csv has bytes that aren't valid UTF-8. Choose the file's encoding.", side.Problem);
        Assert.Equal(CsvSource.At(File), side.Source);
    }

    [Fact]
    public async Task A_read_that_finishes_after_a_newer_change_is_dropped()
    {
        var pending = new Queue<TaskCompletionSource<CsvPeek>>();
        var csv = new FakeCsv(_ =>
        {
            var next = new TaskCompletionSource<CsvPeek>();
            pending.Enqueue(next);
            return next.Task;
        });
        var side = Make.Side("Right", FakeExcel.ReadingTable(), new FakeDialog(File), csv);
        var choosing = side.ChooseCsvFileCommand.ExecuteAsync(null);
        side.CsvDelimiter = DelimiterChoice.For(';');
        var newer = new SourcePreview(["Ref"], [["T-9"]], 1);

        pending.Dequeue().SetResult(new CsvPeek.Read(FakeCsv.Preview));
        await choosing;
        Assert.Null(side.Preview);

        pending.Dequeue().SetResult(new CsvPeek.Read(newer));
        await (side.ReadCsvCommand.ExecutionTask ?? Task.CompletedTask);
        Assert.Same(newer, side.Preview);
    }

    [Fact]
    public async Task Csv_options_do_nothing_for_a_workbook_source()
    {
        var csv = FakeCsv.Reading();
        var side = Make.Side("Left", FakeExcel.ReadingTable(), new FakeDialog(@"C:\data\Book.xlsx"), csv);
        await side.ChooseExcelDataCommand.ExecuteAsync(null);

        side.CsvDelimiter = DelimiterChoice.For(';');

        Assert.IsType<WorkbookSource>(side.Source);
        Assert.False(side.IsCsv);
        Assert.Empty(csv.Peeked);
    }

    private sealed class QueueDialog(Queue<string> paths) : IFileDialogs
    {
        public string? ChooseWorkbook(string title) => paths.Dequeue();

        public string? ChooseCsvFile(string title) => paths.Dequeue();
    }
}
