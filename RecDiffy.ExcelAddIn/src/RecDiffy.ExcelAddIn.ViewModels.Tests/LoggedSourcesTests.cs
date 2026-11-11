using System.IO;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public sealed class LoggedSourcesTests
{
    private static readonly ExcelRangePick WithEmptyRows = FakeExcel.Range with { Rows = 5 };

    [Fact]
    public async Task A_pick_from_an_Excel_file_logs_the_range_and_its_size_without_the_path()
    {
        var log = new RecordingLog();
        var excel = new LoggedExcelSources(FakeExcel.ReadingTable(), log, StepClock.QuarterSecond());

        var choice = await excel.PickRangeAsync(@"C:\data\Book.xlsx", "Select the left data.", CancellationToken.None);

        Assert.IsType<RangeChoice.Picked>(choice);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(
            new Logged(LogSeverity.Information, LogAreas.Excel, "Excel file Book.xlsx: picked $A$1:$C$3 on [Book.xlsx]Trades, 3 rows by 3 columns, after 0.25 s."),
            entry);
    }

    [Fact]
    public async Task A_canceled_pick_in_an_open_workbook_is_logged_as_information()
    {
        var log = new RecordingLog();
        var excel = new LoggedExcelSources(new FakeExcel(new RangeChoice.Canceled(), _ => throw new InvalidOperationException()), log, StepClock.QuarterSecond());

        _ = await excel.PickOpenRangeAsync("Select the left data.", CancellationToken.None);

        Assert.Equal(new Logged(LogSeverity.Information, LogAreas.Excel, "Open workbook: the pick was canceled after 0.25 s."), Assert.Single(log.Entries));
    }

    [Fact]
    public async Task A_rejected_pick_is_a_warning_with_its_reason()
    {
        var log = new RecordingLog();
        var excel = new LoggedExcelSources(
            new FakeExcel(new RangeChoice.Rejected("Pick one rectangular range, not several."), _ => throw new InvalidOperationException()),
            log,
            StepClock.QuarterSecond());

        _ = await excel.PickOpenRangeAsync("Select the left data.", CancellationToken.None);

        Assert.Equal(
            new Logged(LogSeverity.Warning, LogAreas.Excel, "Open workbook: the pick was rejected after 0.25 s: Pick one rectangular range, not several."),
            Assert.Single(log.Entries));
    }

    [Fact]
    public async Task A_read_logs_the_rows_kept_and_the_empty_rows_left_out()
    {
        var log = new RecordingLog();
        var excel = new LoggedExcelSources(FakeExcel.ReadingTable(), log, StepClock.QuarterSecond());

        _ = await excel.ReadAsync(WithEmptyRows, new Progress<double>(), CancellationToken.None);

        Assert.Equal(
            new Logged(LogSeverity.Information, LogAreas.Excel, "Read 2 rows from $A$1:$C$3 on [Book.xlsx]Trades in 0.25 s; 2 empty rows were left out."),
            Assert.Single(log.Entries));
    }

    [Fact]
    public async Task A_read_with_no_empty_rows_says_nothing_was_left_out()
    {
        var log = new RecordingLog();
        var excel = new LoggedExcelSources(FakeExcel.ReadingTable(), log, StepClock.QuarterSecond());

        _ = await excel.ReadAsync(FakeExcel.Range, new Progress<double>(), CancellationToken.None);

        Assert.Equal("Read 2 rows from $A$1:$C$3 on [Book.xlsx]Trades in 0.25 s.", Assert.Single(log.Entries).Message);
    }

    [Fact]
    public async Task A_rejected_read_is_a_warning()
    {
        var log = new RecordingLog();
        var excel = new LoggedExcelSources(FakeExcel.Reading(new SourceRead.Rejected("The range picked on [Book.xlsx]Trades is empty.")), log, StepClock.QuarterSecond());

        _ = await excel.ReadAsync(FakeExcel.Range, new Progress<double>(), CancellationToken.None);

        Assert.Equal(
            new Logged(LogSeverity.Warning, LogAreas.Excel, "Reading $A$1:$C$3 on [Book.xlsx]Trades was rejected after 0.25 s: The range picked on [Book.xlsx]Trades is empty."),
            Assert.Single(log.Entries));
    }

    [Fact]
    public async Task A_read_that_throws_is_logged_as_an_error_without_the_path_and_still_throws()
    {
        var log = new RecordingLog();
        var excel = new LoggedExcelSources(
            new FakeExcel(new RangeChoice.Canceled(), _ => throw new IOException(@"The process cannot access the file 'C:\data\Book.xlsx' because it is being used by another process.")),
            log,
            StepClock.QuarterSecond());

        _ = await Assert.ThrowsAsync<IOException>(() => excel.ReadAsync(FakeExcel.Range, new Progress<double>(), CancellationToken.None));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogSeverity.Error, entry.Severity);
        Assert.Equal(
            "Reading $A$1:$C$3 on [Book.xlsx]Trades failed after 0.25 s: IOException: The process cannot access the file 'Book.xlsx' because it is being used by another process.",
            entry.Message);
        Assert.DoesNotContain(@"C:\data", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_read_that_is_canceled_is_logged_as_information_and_still_throws()
    {
        var log = new RecordingLog();
        var excel = new LoggedExcelSources(new FakeExcel(new RangeChoice.Canceled(), _ => throw new OperationCanceledException()), log, StepClock.QuarterSecond());

        _ = await Assert.ThrowsAsync<OperationCanceledException>(() => excel.ReadAsync(FakeExcel.Range, new Progress<double>(), CancellationToken.None));

        Assert.Equal(
            new Logged(LogSeverity.Information, LogAreas.Excel, "Reading $A$1:$C$3 on [Book.xlsx]Trades was canceled after 0.25 s."),
            Assert.Single(log.Entries));
    }

    [Fact]
    public async Task A_CSV_file_that_is_read_logs_its_size_and_settings()
    {
        var log = new RecordingLog();
        var csv = new LoggedCsvSources(FakeCsv.Reading(), log, StepClock.QuarterSecond());

        _ = await csv.PeekAsync(CsvSource.At(@"C:\data\broker.csv"), CancellationToken.None);

        Assert.Equal(
            new Logged(LogSeverity.Information, LogAreas.Csv, "Read broker.csv: 2 rows, 2 columns, utf-8, comma-delimited, with a header row, in 0.25 s."),
            Assert.Single(log.Entries));
    }

    [Fact]
    public async Task A_rejected_CSV_file_is_a_warning_that_says_why()
    {
        var log = new RecordingLog();
        var csv = new LoggedCsvSources(
            new FakeCsv(_ => Task.FromResult<CsvPeek>(new CsvPeek.Rejected("broker.csv has bytes that aren't valid UTF-8. Choose the file's encoding."))),
            log,
            StepClock.QuarterSecond());

        _ = await csv.PeekAsync(CsvSource.At(@"C:\data\broker.csv") with { Delimiter = null }, CancellationToken.None);

        Assert.Equal(
            new Logged(
                LogSeverity.Warning,
                LogAreas.Csv,
                "Rejected broker.csv (utf-8, delimiter detected, with a header row) after 0.25 s: broker.csv has bytes that aren't valid UTF-8. Choose the file's encoding."),
            Assert.Single(log.Entries));
    }
}
