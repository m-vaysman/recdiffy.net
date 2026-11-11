using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public sealed class LoggedRunsTests
{
    [Fact]
    public async Task A_completed_run_logs_its_sides_and_the_counts()
    {
        var log = new RecordingLog();
        var completed = new List<RunOutcome.Completed>();
        var runs = new LoggedRuns(ResultSheetFixtures.Engine(ResultSheetFixtures.Left, ResultSheetFixtures.Right, completed), log, StepClock.QuarterSecond());
        var shell = await ResultSheetFixtures.ValidShell(runs, FakeOutput.Writing(), new SettingsViewModel(), ResultSheetFixtures.Left, ResultSheetFixtures.Right);

        await shell.RunCommand.ExecuteAsync(null);

        var outcome = Assert.Single(completed);
        Assert.Equal(
            [
                new Logged(LogSeverity.Information, LogAreas.Run, "Run started: left $A$1:$C$3 on [Book.xlsx]Trades, right $A$1:$C$3 on [Book.xlsx]Trades."),
                new Logged(LogSeverity.Information, LogAreas.Run, $"Run finished in 0.25 s: {LoggedRuns.Counts(outcome.Result.Summary)}."),
            ],
            log.Entries);
        Assert.StartsWith("Matched ", LoggedRuns.Counts(outcome.Result.Summary), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_run_is_a_warning_and_a_CSV_side_is_named_by_its_file_with_its_settings()
    {
        var log = new RecordingLog();
        var runs = new LoggedRuns(FakeRuns.Returning(new RunOutcome.Failed("The rules can't be compiled: no key.")), log, StepClock.QuarterSecond());
        var request = new RunRequest(SetupProfiles.Draft(), new RunSide.Workbook(FakeExcel.Range), new RunSide.Csv(CsvSource.At(@"C:\data\broker.csv")));

        _ = await runs.RunAsync(request, new Progress<RunProgress>(), CancellationToken.None);

        Assert.Equal(
            [
                new Logged(LogSeverity.Information, LogAreas.Run, "Run started: left $A$1:$C$3 on [Book.xlsx]Trades, right broker.csv (utf-8, comma-delimited, with a header row)."),
                new Logged(LogSeverity.Warning, LogAreas.Run, "Run failed after 0.25 s: The rules can't be compiled: no key."),
            ],
            log.Entries);
    }

    [Fact]
    public async Task A_canceled_run_is_information()
    {
        var log = new RecordingLog();
        var runs = new LoggedRuns(FakeRuns.Returning(new RunOutcome.Canceled()), log, StepClock.QuarterSecond());
        var request = new RunRequest(SetupProfiles.Draft(), new RunSide.Workbook(FakeExcel.Range), new RunSide.Workbook(FakeExcel.Range));

        _ = await runs.RunAsync(request, new Progress<RunProgress>(), CancellationToken.None);

        Assert.Equal(new Logged(LogSeverity.Information, LogAreas.Run, "Run canceled after 0.25 s."), log.Entries[^1]);
    }

    [Fact]
    public async Task A_run_that_throws_is_an_error_and_the_exception_reaches_the_caller()
    {
        var log = new RecordingLog();
        var runs = new LoggedRuns(new FakeRuns((_, _, _) => throw new InvalidOperationException(@"Couldn't load C:\data\broker.csv.")), log, StepClock.QuarterSecond());
        var request = new RunRequest(SetupProfiles.Draft(), new RunSide.Workbook(FakeExcel.Range), new RunSide.Csv(CsvSource.At(@"C:\data\broker.csv")));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => runs.RunAsync(request, new Progress<RunProgress>(), CancellationToken.None));

        Assert.Equal(
            new Logged(LogSeverity.Error, LogAreas.Run, "The run failed after 0.25 s: InvalidOperationException: Couldn't load broker.csv."),
            log.Entries[^1]);
    }

    [Fact]
    public async Task A_completed_write_logs_where_the_results_went()
    {
        var log = new RecordingLog();
        var output = new LoggedResultOutput(FakeOutput.Returning(new WriteOutcome.Completed("Book2", ["RecDiffy 2026-11-11 09.30"], 7)), log, StepClock.QuarterSecond());

        _ = await output.WriteAsync(Sheet(7), new Progress<WriteProgress>(), CancellationToken.None);

        Assert.Equal(
            [
                new Logged(LogSeverity.Information, LogAreas.Worksheet, "Writing 7 results to a new workbook."),
                new Logged(LogSeverity.Information, LogAreas.Worksheet, "Wrote 7 results to [Book2]RecDiffy 2026-11-11 09.30 in 0.25 s."),
            ],
            log.Entries);
    }

    [Fact]
    public async Task A_canceled_write_says_how_far_it_got()
    {
        var log = new RecordingLog();
        var output = new LoggedResultOutput(FakeOutput.Returning(new WriteOutcome.Canceled("Book2", ["RecDiffy"], 40_000, 120_000)), log, StepClock.QuarterSecond());

        _ = await output.WriteAsync(Sheet(120_000), new Progress<WriteProgress>(), CancellationToken.None);

        Assert.Equal(
            new Logged(LogSeverity.Information, LogAreas.Worksheet, "Writing was canceled after 40,000 of 120,000 results, in 0.25 s. [Book2]RecDiffy keeps what was written."),
            log.Entries[^1]);
    }

    [Fact]
    public async Task A_failed_write_is_a_warning()
    {
        var log = new RecordingLog();
        var output = new LoggedResultOutput(FakeOutput.Returning(new WriteOutcome.Failed("Excel is busy.", 0, 1)), log, StepClock.QuarterSecond());

        _ = await output.WriteAsync(Sheet(1), new Progress<WriteProgress>(), CancellationToken.None);

        Assert.Equal(new Logged(LogSeverity.Warning, LogAreas.Worksheet, "Writing failed after 0 of 1 result, in 0.25 s: Excel is busy."), log.Entries[^1]);
    }

    [Fact]
    public void Settings_that_load_and_save_are_logged()
    {
        var log = new RecordingLog();
        var store = new LoggedSettingsStore(new FakeSettingsStore(AddInSettings.Default), log, StepClock.QuarterSecond());

        _ = store.Load();
        _ = store.Save(new AddInSettings(IncludeExactMatches: false, OffersExplanation: true));

        Assert.Equal(
            [
                new Logged(LogSeverity.Information, LogAreas.Settings, "Settings loaded: exact matches listed, explanation offered."),
                new Logged(LogSeverity.Information, LogAreas.Settings, "Settings saved: exact matches left out, explanation offered."),
            ],
            log.Entries);
    }

    [Fact]
    public void Settings_that_cannot_be_saved_are_a_warning_without_the_user_profile_path()
    {
        var log = new RecordingLog();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var store = new LoggedSettingsStore(
            new FakeSettingsStore(AddInSettings.Default, $"access to {profile}\\AppData\\Roaming\\RecDiffy\\settings.json is denied."),
            log,
            StepClock.QuarterSecond());

        _ = store.Save(AddInSettings.Default);

        Assert.Equal(
            new Logged(LogSeverity.Warning, LogAreas.Settings, @"Settings weren't saved: access to %USERPROFILE%\AppData\Roaming\RecDiffy\settings.json is denied."),
            Assert.Single(log.Entries));
    }

    private static ResultSheet Sheet(int results) =>
        new("RecDiffy", [], results, new SheetCell(0, 0), "Complete", IsStale: false);
}
