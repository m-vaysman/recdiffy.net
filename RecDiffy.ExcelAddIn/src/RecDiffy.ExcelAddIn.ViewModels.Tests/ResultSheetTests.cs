using RecDiffy.ExcelAddIn.ViewModels;
using static RecDiffy.ExcelAddIn.ViewModels.Tests.ResultSheetFixtures;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class ResultSheetTests
{
    [Fact]
    public async Task The_header_block_says_when_the_data_was_read_where_it_came_from_and_which_rules_ran()
    {
        var (sheet, completed) = await WrittenSheet();

        var rows = sheet.Pages[0].Rows;
        Assert.Equal("RecDiffy results", TextOf(rows[0].Cells[0]));
        Assert.Equal(SheetRowKind.Heading, rows[0].Kind);
        Assert.Equal(("Data read at", new SheetValue.Date(ReadAt.DateTime, HasTime: true)), (TextOf(rows[1].Cells[0]), rows[1].Cells[1]));
        Assert.Equal(("Left", "Right"), (TextOf(rows[3].Cells[0]), TextOf(rows[4].Cells[0])));
        Assert.Equal(new SheetValue.Number(7), rows[3].Cells[1]);
        Assert.Equal(new SheetValue.Number(10), rows[4].Cells[1]);
        Assert.Equal(("Book.xlsx · Trades", "broker.csv"), (TextOf(rows[3].Cells[2]), TextOf(rows[4].Cells[2])));
        Assert.Equal(completed.Result.Run.ProfileHash, TextOf(rows[5].Cells[1]));
        Assert.Equal($"RecDiffy engine {completed.Result.Run.EngineVersion}", TextOf(rows[6].Cells[1]));
        Assert.Equal("RecDiffy 2026-11-09 17.30", sheet.Name);
    }

    [Fact]
    public async Task The_status_line_says_incomplete_until_the_write_finishes_then_complete()
    {
        var (sheet, _) = await WrittenSheet();

        var status = sheet.Pages[0].Rows[sheet.StatusCell.Row];
        Assert.Equal("Status", TextOf(status.Cells[0]));
        Assert.Equal(ResultSheets.WritingStatus, TextOf(status.Cells[sheet.StatusCell.Column]));
        Assert.Equal(ResultSheets.CompleteStatus, sheet.FinalStatus);
        Assert.False(sheet.IsStale);
    }

    [Fact]
    public async Task Results_written_after_the_rules_changed_carry_the_stale_status_line()
    {
        var output = FakeOutput.Writing();
        var shell = await ValidShell(output);
        await shell.RunCommand.ExecuteAsync(null);
        shell.Rules.Comparisons[1].AmountText = "0.02";

        await shell.WriteResultsCommand.ExecuteAsync(null);

        var sheet = Assert.Single(output.Sheets);
        Assert.True(sheet.IsStale);
        Assert.Equal("The rules or sources changed after this run. Run again to refresh the results.", sheet.FinalStatus);
    }

    [Fact]
    public void An_incomplete_write_says_why_and_keeps_the_stale_warning()
    {
        var canceled = InCulture("en-US", () => ResultSheets.Canceled(40_000, 120_000, isStale: false));
        var failed = ResultSheets.Failed("the workbook Book2 was closed before the write finished.", isStale: true);

        Assert.Equal("Incomplete: canceled after 40,000 of 120,000 results.", canceled);
        Assert.Equal(
            "Incomplete: the workbook Book2 was closed before the write finished. The rules or sources changed after this run. Run again to refresh the results.",
            failed);
    }

    [Fact]
    public async Task The_counts_are_the_engines_summary_as_the_results_tab_shows_it()
    {
        var (sheet, _) = await WrittenSheet();

        var rows = sheet.Pages[0].Rows;
        var labels = rows[10].Cells.Select(TextOf);
        var counts = rows[11].Cells.Select(cell => Assert.IsType<SheetValue.Number>(cell).Value);
        Assert.Equal(
            [("Matched", 4m), ("Exact", 2m), ("Within tolerance", 1m), ("Breaks", 1m), ("Left only", 1m), ("Right only", 1m), ("Ambiguous", 1m), ("Quarantined rows", 1m)],
            labels.Zip(counts));
    }

    [Fact]
    public async Task There_is_one_row_per_result_under_a_frozen_header_row()
    {
        var (sheet, completed) = await WrittenSheet();

        var page = sheet.Pages[0];
        Assert.Equal(ResultSheets.Columns, Texts(page.Rows[page.FrozenRows - 1]), StringComparer.Ordinal);
        Assert.Equal(SheetRowKind.Heading, page.Rows[page.FrozenRows - 1].Kind);
        Assert.Equal(completed.Result.Entries.Count, ResultRows(sheet).Count);
        Assert.Equal(completed.Result.Entries.Count, sheet.Results);
    }

    [Fact]
    public async Task Every_outcome_the_engine_returns_is_named_with_its_keys_rows_pass_and_details()
    {
        var (sheet, _) = await WrittenSheet();

        Assert.Equal(
            ["Matched · exact", "T-1", "row 2", "T-1", "record 1", "Pass 1", ""],
            Texts(RowWithOutcome(sheet, "Matched · exact", "T-1")),
            StringComparer.Ordinal);
        Assert.Equal("Price 1.50 ≈ 1.51 (difference 0.01)", TextOf(RowWithOutcome(sheet, "Matched · within tolerance").Cells[6]));
        Assert.Equal("Quantity 10 ≠ 11 (difference 1)", TextOf(RowWithOutcome(sheet, "Matched · break").Cells[6]));
        var leftOnly = RowWithOutcome(sheet, "Left only");
        Assert.Equal(("T-4", "row 5", SheetValue.Empty, SheetValue.Empty), (TextOf(leftOnly.Cells[1]), TextOf(leftOnly.Cells[2]), leftOnly.Cells[3], leftOnly.Cells[4]));
        var rightOnly = RowWithOutcome(sheet, "Right only");
        Assert.Equal((SheetValue.Empty, "T-5", "record 4"), (rightOnly.Cells[1], TextOf(rightOnly.Cells[3]), TextOf(rightOnly.Cells[4])));
        Assert.Equal((SheetValue.Empty, SheetValue.Empty), (leftOnly.Cells[5], rightOnly.Cells[5]));
    }

    [Fact]
    public async Task Each_result_has_the_outcome_and_details_the_results_tab_shows_in_the_same_order()
    {
        var output = FakeOutput.Writing();
        var shell = await ValidShell(output);
        await shell.RunCommand.ExecuteAsync(null);

        await shell.WriteResultsCommand.ExecuteAsync(null);

        Assert.Equal(
            shell.Results.Run!.Rows.Select(row => (row.Outcome, row.Details)),
            ResultRows(Assert.Single(output.Sheets)).Select(row => (TextOf(row.Cells[0]) ?? string.Empty, TextOf(row.Cells[6]) ?? string.Empty)));
    }

    [Fact]
    public async Task A_grouped_result_lists_every_row_it_holds()
    {
        var (sheet, _) = await WrittenSheet();

        var ambiguous = RowWithOutcome(sheet, "Ambiguous");
        Assert.Equal(("T-7", "row 8"), (TextOf(ambiguous.Cells[1]), TextOf(ambiguous.Cells[2])));
        Assert.Equal(("T-7", "record 5, record 6, record 7, record 8, record 9"), (TextOf(ambiguous.Cells[3]), TextOf(ambiguous.Cells[4])));
    }

    [Fact]
    public async Task Rows_RecDiffy_could_not_read_follow_the_results_with_their_location_and_diagnostic()
    {
        var (sheet, _) = await WrittenSheet();

        var rows = AllRows(sheet);
        var heading = rows.Select((row, index) => (row, index)).Single(row => TextOf(Cell(row.row, 0)) == "Rows RecDiffy couldn't read").index;
        Assert.Equal(["Side", "Row", "Diagnostics"], Texts(rows[heading + 1]), StringComparer.Ordinal);
        var problem = Assert.Single(rows, row => row.Kind is SheetRowKind.Problem);
        Assert.Equal(("Left", "row 7"), (TextOf(problem.Cells[0]), TextOf(problem.Cells[1])));
        Assert.Contains("Quantity", TextOf(problem.Cells[2]), StringComparison.Ordinal);
        Assert.True(heading > rows.ToList().FindLastIndex(row => row.Kind is SheetRowKind.Result));
    }

    [Fact]
    public async Task Leaving_out_exact_matches_lists_fewer_results_keeps_the_counts_and_says_so()
    {
        var completed = new List<RunOutcome.Completed>();
        var shell = await ValidShell(FakeOutput.Writing(), completed: completed);
        await shell.RunCommand.ExecuteAsync(null);
        var run = Assert.Single(completed);
        IReadOnlyList<string> keys = ["Ref"];

        var all = ResultSheets.Build(run, keys, new ResultSheetOptions(IncludeExactMatches: true, IsStale: false));
        var some = ResultSheets.Build(run, keys, new ResultSheetOptions(IncludeExactMatches: false, IsStale: false));

        Assert.Equal(all.Results - 2, some.Results);
        Assert.DoesNotContain(ResultRows(some), row => TextOf(row.Cells[0]) == "Matched · exact");
        Assert.Equal(all.Pages[0].Rows[11].Cells, some.Pages[0].Rows[11].Cells);
        Assert.Equal("Listed below.", TextOf(all.Pages[0].Rows[8].Cells[1]));
        Assert.Equal("Not listed below. The counts include all 2 of them.", TextOf(some.Pages[0].Rows[8].Cells[1]));
    }

    [Fact]
    public async Task Text_that_looks_like_a_formula_stays_text()
    {
        var (sheet, _) = await WrittenSheet();

        var formula = RowWithOutcome(sheet, "Matched · exact", "=1+1");
        Assert.Equal(new SheetValue.Text("=1+1"), formula.Cells[1]);
        Assert.Equal(new SheetValue.Text("=1+1"), formula.Cells[3]);
    }

    [Fact]
    public async Task A_decimal_key_is_a_number_and_a_date_key_is_a_date()
    {
        SourceTable left = new(["Ref", "Day"], [[10.25m, new DateTime(2026, 11, 5)]]) { SheetRows = [2] };
        SourceTable right = new(["Ref", "Day"], [[10.25m, new DateTime(2026, 11, 5)]]);
        var completed = new List<RunOutcome.Completed>();
        var shell = await ValidShell(Engine(left, right, completed), FakeOutput.Writing(), new SettingsViewModel(), left, right, rules =>
        {
            rules.Fields[0].Type = FieldType.Decimal;
            rules.Fields[0].Role = FieldRole.Key;
            rules.Fields[0].ScaleText = "2";
            rules.Fields[1].Type = FieldType.Date;
            rules.Fields[1].Role = FieldRole.Economic;
        });
        await shell.RunCommand.ExecuteAsync(null);
        var run = Assert.Single(completed);

        var byNumber = ResultSheets.Build(run, ["Ref"], new ResultSheetOptions(IncludeExactMatches: true, IsStale: false));
        var byDate = ResultSheets.Build(run, ["Day"], new ResultSheetOptions(IncludeExactMatches: true, IsStale: false));

        Assert.Equal(new SheetValue.Number(10.25m), Assert.Single(ResultRows(byNumber)).Cells[1]);
        Assert.Equal(new SheetValue.Date(new DateTime(2026, 11, 5), HasTime: false), Assert.Single(ResultRows(byDate)).Cells[1]);
    }

    [Fact]
    public async Task Past_the_row_limit_the_results_continue_on_another_page_under_the_same_header()
    {
        var completed = new List<RunOutcome.Completed>();
        var shell = await ValidShell(FakeOutput.Writing(), completed: completed);
        await shell.RunCommand.ExecuteAsync(null);
        var run = Assert.Single(completed);

        var sheet = ResultSheets.Build(run, ["Ref"], new ResultSheetOptions(IncludeExactMatches: true, IsStale: false, RowLimit: 20));

        Assert.Equal(2, sheet.Pages.Count);
        Assert.All(sheet.Pages, page => Assert.InRange(page.Rows.Count, 1, 20));
        Assert.Equal(20, sheet.Pages[0].Rows.Count);
        Assert.Equal(14, sheet.Pages[0].FrozenRows);
        Assert.Equal(ResultSheets.Columns, Texts(sheet.Pages[1].Rows[0]), StringComparer.Ordinal);
        Assert.Equal(1, sheet.Pages[1].FrozenRows);
        Assert.Equal(run.Result.Entries.Count, ResultRows(sheet).Count);
        Assert.Single(AllRows(sheet), row => row.Kind is SheetRowKind.Problem);
    }

    [Fact]
    public async Task A_row_limit_too_small_for_the_header_block_is_refused()
    {
        var completed = new List<RunOutcome.Completed>();
        var shell = await ValidShell(FakeOutput.Writing(), completed: completed);
        await shell.RunCommand.ExecuteAsync(null);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ResultSheets.Build(Assert.Single(completed), ["Ref"], new ResultSheetOptions(IncludeExactMatches: true, IsStale: false, RowLimit: 10)));
    }

    private static async Task<(ResultSheet Sheet, RunOutcome.Completed Completed)> WrittenSheet()
    {
        var output = FakeOutput.Writing();
        var completed = new List<RunOutcome.Completed>();
        var shell = await ValidShell(output, completed: completed).ConfigureAwait(true);
        await shell.RunCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.WriteResultsCommand.ExecuteAsync(null).ConfigureAwait(true);
        return (Assert.Single(output.Sheets), Assert.Single(completed));
    }
}
