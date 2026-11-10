using RecDiffy.ExcelAddIn.ViewModels;
using static RecDiffy.ExcelAddIn.ViewModels.Tests.ResultSheetFixtures;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class WriteResultsTests
{
    [Fact]
    public async Task Writing_is_off_until_a_run_completes()
    {
        var shell = await ValidShell(FakeOutput.Writing());
        Assert.False(shell.CanWrite);
        Assert.False(shell.WriteResultsCommand.CanExecute(null));

        await shell.RunCommand.ExecuteAsync(null);

        Assert.True(shell.CanWrite);
        Assert.True(shell.WriteResultsCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_failed_first_run_leaves_writing_off()
    {
        var shell = await ValidShell(FakeRuns.Returning(new RunOutcome.Failed("Excel couldn't read it.")), FakeOutput.Writing(), new SettingsViewModel(), Left, Right);

        await shell.RunCommand.ExecuteAsync(null);

        Assert.False(shell.CanWrite);
    }

    [Fact]
    public async Task The_window_says_which_workbook_and_sheet_it_wrote_and_how_many_results()
    {
        var shell = await ValidShell(FakeOutput.Writing());
        await shell.RunCommand.ExecuteAsync(null);

        await shell.WriteResultsCommand.ExecuteAsync(null);

        Assert.False(shell.Results.IsWriting);
        Assert.Equal("Wrote 7 results to [Book2]RecDiffy 2026-11-09 17.30.", shell.Results.WriteMessage);
        Assert.True(shell.CanWrite);
    }

    [Fact]
    public async Task The_sheet_follows_the_include_exact_matches_setting()
    {
        var output = FakeOutput.Writing();
        var shell = await ValidShell(output);
        await shell.RunCommand.ExecuteAsync(null);
        shell.Settings.IncludeExactMatches = false;

        await shell.WriteResultsCommand.ExecuteAsync(null);

        var sheet = Assert.Single(output.Sheets);
        Assert.Equal(5, sheet.Results);
        Assert.DoesNotContain(ResultRows(sheet), row => TextOf(row.Cells[0]) == "Matched · exact");
    }

    [Fact]
    public async Task Run_and_write_are_off_while_writing_and_cancel_reaches_the_writer()
    {
        var started = new TaskCompletionSource();
        var output = new FakeOutput(async (sheet, _, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => { }, TaskScheduler.Default).ConfigureAwait(false);
            return new WriteOutcome.Canceled("Book2", [sheet.Name], 0, sheet.Results);
        });
        var shell = await ValidShell(output);
        await shell.RunCommand.ExecuteAsync(null);

        var writing = shell.WriteResultsCommand.ExecuteAsync(null);
        await started.Task;

        Assert.True(shell.Results.IsWriting);
        Assert.False(shell.CanRun);
        Assert.False(shell.CanWrite);
        Assert.False(shell.RunCommand.CanExecute(null));
        Assert.Equal("Writing results to Excel…", shell.Status);
        shell.WriteResultsCancelCommand.Execute(null);
        await writing;
        Assert.False(shell.Results.IsWriting);
        Assert.True(shell.CanRun);
        Assert.Equal(
            "The write was canceled after 0 of 7 results. [Book2]RecDiffy 2026-11-09 17.30 keeps what was written and says it's incomplete.",
            shell.Results.WriteMessage);
    }

    [Fact]
    public async Task A_failed_write_says_why()
    {
        var shell = await ValidShell(FakeOutput.Returning(new WriteOutcome.Failed("the workbook Book2 was closed before the write finished.", 5_000, 7)));
        await shell.RunCommand.ExecuteAsync(null);

        await shell.WriteResultsCommand.ExecuteAsync(null);

        Assert.Equal(
            "The write stopped after 5,000 of 7 results: the workbook Book2 was closed before the write finished.",
            InCulture("en-US", () => ResultOutputs.Describe(new WriteOutcome.Failed("the workbook Book2 was closed before the write finished.", 5_000, 7))));
        Assert.StartsWith("The write stopped after ", shell.Results.WriteMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_shell_without_an_output_says_writing_is_not_available()
    {
        var shell = new ShellViewModel(Make.Sources(FakeExcel.ReadingTable(), new FakeDialog(@"C:\data\Book.xlsx")), FakeRuns.Unused());
        var results = new ResultsViewModel();
        var outcome = await ResultOutputs.Unavailable.WriteAsync(
            new ResultSheet("RecDiffy", [], 3, new SheetCell(7, 1), ResultSheets.CompleteStatus, IsStale: false),
            new Progress<WriteProgress>(),
            CancellationToken.None);

        results.FinishWrite(outcome);

        Assert.False(shell.CanWrite);
        Assert.Equal("The results weren't written: writing to a worksheet isn't available here.", results.WriteMessage);
    }

    [Fact]
    public void Progress_names_how_many_results_are_written()
    {
        var results = new ResultsViewModel();
        results.StartWrite();
        Assert.Equal("Writing results to Excel", results.WriteStage);

        results.ReportWrite(new WriteProgress(40_000, 120_000));

        Assert.Equal("Writing results to Excel: 40,000 of 120,000", InCulture("en-US", () => results.WriteStage));
        Assert.Equal(1d / 3, results.WriteFraction, 6);
    }

    [Fact]
    public void Progress_after_the_write_finished_is_ignored()
    {
        var results = new ResultsViewModel();
        results.StartWrite();
        results.FinishWrite(new WriteOutcome.Completed("Book2", ["RecDiffy"], 1));

        results.ReportWrite(new WriteProgress(1, 1));

        Assert.Null(results.WriteProgress);
    }

    [Fact]
    public void A_write_over_several_sheets_names_each_one() =>
        Assert.Equal(
            "Wrote 1,200,000 results to Book2, on sheets RecDiffy, RecDiffy (2) and RecDiffy (3).",
            InCulture("en-US", () => ResultOutputs.Describe(new WriteOutcome.Completed("Book2", ["RecDiffy", "RecDiffy (2)", "RecDiffy (3)"], 1_200_000))));

    [Fact]
    public async Task A_new_run_clears_the_last_write_message()
    {
        var shell = await ValidShell(FakeOutput.Writing());
        await shell.RunCommand.ExecuteAsync(null);
        await shell.WriteResultsCommand.ExecuteAsync(null);
        Assert.NotNull(shell.Results.WriteMessage);

        await shell.RunCommand.ExecuteAsync(null);

        Assert.Null(shell.Results.WriteMessage);
    }
}
