using System.Data;
using System.Globalization;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class RunTests
{
    private static readonly DateTimeOffset ReadAt = new(2026, 11, 9, 17, 30, 0, TimeSpan.Zero);

    private static readonly SourceTable Left = new(
        ["Ref", "Quantity", "Price"],
        [["T-1", 10m, 1.50m], ["T-2", 10m, 1.50m], ["T-3", 10m, 1.50m], ["T-4", 10m, 1.50m], ["T-6", "ten", 1.50m], ["T-7", 10m, 1.50m]])
    { SheetRows = [2, 3, 4, 5, 7, 8] };

    private static readonly SourceTable Right = new(
        ["Ref", "Quantity", "Price"],
        [["T-1", 10m, 1.50m], ["T-2", 10m, 1.51m], ["T-3", 11m, 1.50m], ["T-5", 10m, 1.50m], ["T-7", 10m, 1.50m], ["T-7", 10m, 1.50m]]);

    [Fact]
    public async Task A_run_shows_the_engines_result_on_the_results_tab()
    {
        var shell = await ValidShell(Engine());

        await shell.RunCommand.ExecuteAsync(null);

        var results = shell.Results;
        Assert.Equal(ShellTab.Results, shell.SelectedTab);
        Assert.Equal(RunState.Completed, results.State);
        Assert.NotNull(results.Run);
        Assert.Equal(
            [("Matched", 3), ("Exact", 1), ("Within tolerance", 1), ("Breaks", 1), ("Left only", 1), ("Right only", 1), ("Ambiguous", 1), ("Quarantined rows", 1)],
            results.Run.Counts.Select(count => (count.Label, count.Count)));
        Assert.StartsWith("Data read at ", results.Run.Label, StringComparison.Ordinal);
        Assert.Contains("Left: Book.xlsx · Trades, 6 rows · Right: broker.csv, 6 rows", results.Run.Label, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_result_row_names_its_outcome_its_rows_and_what_differed()
    {
        var shell = await ValidShell(Engine());

        await shell.RunCommand.ExecuteAsync(null);

        var rows = Rows(shell);
        Assert.Equal(("T-1 · row 2", "T-1 · record 1", string.Empty), (rows["T-1"].Left, rows["T-1"].Right, rows["T-1"].Details));
        Assert.Equal("Matched · exact", rows["T-1"].Outcome);
        Assert.Equal("Matched · within tolerance", rows["T-2"].Outcome);
        Assert.Equal("Price 1.50 ≈ 1.51 (difference 0.01)", rows["T-2"].Details);
        Assert.Equal("Matched · break", rows["T-3"].Outcome);
        Assert.True(rows["T-3"].IsBreak);
        Assert.Equal("Quantity 10 ≠ 11 (difference 1)", rows["T-3"].Details);
        Assert.Equal(("Left only", "—"), (rows["T-4"].Outcome, rows["T-4"].Right));
        Assert.Equal(("Right only", "—"), (rows["T-5"].Outcome, rows["T-5"].Left));
        Assert.Equal(("Ambiguous", "2 rows: record 5, record 6"), (rows["T-7"].Outcome, rows["T-7"].Right));
    }

    [Fact]
    public async Task A_row_the_engine_could_not_read_is_listed_with_its_sheet_row()
    {
        var shell = await ValidShell(Engine());

        await shell.RunCommand.ExecuteAsync(null);

        var problem = Assert.Single(Assert.IsType<RunView>(shell.Results.Run).RowProblems);
        Assert.StartsWith("Left row 7: ", problem, StringComparison.Ordinal);
        Assert.Contains("Quantity", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_request_carries_the_setups_profile_and_each_sides_range()
    {
        var runs = Engine();
        var shell = await ValidShell(runs);

        await shell.RunCommand.ExecuteAsync(null);

        var request = Assert.Single(runs.Requests);
        Assert.Equal(shell.ToSetup().Profile.ToJson(), request.Profile.ToJson());
        Assert.Equal(new RunSide.Workbook(FakeExcel.Range), request.Left);
        Assert.Equal(new RunSide.Workbook(FakeExcel.Range), request.Right);
    }

    [Fact]
    public async Task Run_is_off_during_a_run_and_a_canceled_run_keeps_the_previous_results()
    {
        var started = new TaskCompletionSource();
        var engine = Engine();
        var runs = new FakeRuns(async (request, progress, cancellationToken) =>
        {
            if (started.Task.IsCompleted is false && engine.Requests.Count > 0)
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => { }, TaskScheduler.Default).ConfigureAwait(false);
                return new RunOutcome.Canceled();
            }

            return await engine.RunAsync(request, progress, cancellationToken).ConfigureAwait(false);
        });
        var shell = await ValidShell(runs);
        await shell.RunCommand.ExecuteAsync(null);
        var first = shell.Results.Run;

        var running = shell.RunCommand.ExecuteAsync(null);
        await started.Task;

        Assert.True(shell.Results.IsRunning);
        Assert.False(shell.CanRun);
        Assert.False(shell.RunCommand.CanExecute(null));
        Assert.Equal("Starting the run…", shell.Status);
        shell.RunCancelCommand.Execute(null);
        await running;
        Assert.Equal(RunState.Canceled, shell.Results.State);
        Assert.Same(first, shell.Results.Run);
        Assert.Equal("The run was canceled. The results below are from the previous run.", shell.Results.Message);
        Assert.True(shell.CanRun);
    }

    [Fact]
    public async Task A_failed_run_says_why_and_shows_no_results()
    {
        var shell = await ValidShell(FakeRuns.Returning(new RunOutcome.Failed("Excel couldn't read $A$1:$C$3 on [Book.xlsx]Trades.")));

        await shell.RunCommand.ExecuteAsync(null);

        Assert.Equal(RunState.Failed, shell.Results.State);
        Assert.Equal("Excel couldn't read $A$1:$C$3 on [Book.xlsx]Trades.", shell.Results.Message);
        Assert.False(shell.Results.HasResults);
        Assert.False(shell.Results.ShowsEmptyNotice);
        Assert.True(shell.CanRun);
    }

    [Fact]
    public async Task Results_are_marked_stale_after_a_rule_changes_until_the_next_run()
    {
        var shell = await ValidShell(Engine());
        await shell.RunCommand.ExecuteAsync(null);
        Assert.False(shell.ShowsStaleNotice);

        shell.Rules.Comparisons[1].AmountText = "0.02";

        Assert.True(shell.ShowsStaleNotice);
        await shell.RunCommand.ExecuteAsync(null);
        Assert.False(shell.ShowsStaleNotice);
    }

    [Fact]
    public async Task The_explanation_comes_from_the_result_and_a_new_run_starts_it_collapsed()
    {
        var shell = await ValidShell(Engine());
        await shell.RunCommand.ExecuteAsync(null);
        var results = shell.Results;

        results.Selected = Rows(shell)["T-2"];
        results.IsExplanationOpen = true;

        var sections = results.Explanation.ToDictionary(section => section.Title, section => section.Lines, StringComparer.Ordinal);
        Assert.Equal(["Matched · within tolerance"], sections["Outcome"]);
        Assert.Equal("Matched by Pass 1.", sections["Matching"][0]);
        Assert.Equal("Pass 1: matched on key T-2.", sections["Matching"][1]);
        Assert.Contains(sections["Left T-2 · row 3"], line => line.StartsWith("identifier.map → Ref: 'T-2'", StringComparison.Ordinal));
        await shell.RunCommand.ExecuteAsync(null);
        Assert.Null(results.Selected);
        Assert.False(results.IsExplanationOpen);
        Assert.Empty(results.Explanation);
    }

    [Fact]
    public void A_new_shell_has_no_results_and_shows_the_empty_notice()
    {
        var results = new ResultsViewModel();

        Assert.Equal(RunState.NotRun, results.State);
        Assert.True(results.ShowsEmptyNotice);
        Assert.False(results.HasResults);
    }

    [Fact]
    public void Progress_after_a_run_finished_is_ignored()
    {
        var results = new ResultsViewModel();
        results.Start();
        results.Finish(new RunOutcome.Canceled(), []);

        results.Report(new RunProgress("Reconciling", null));

        Assert.Equal(string.Empty, results.Stage);
    }

    [Fact]
    public void The_date_columns_are_the_columns_each_side_reads_as_dates()
    {
        var rules = new RulesState(
            [
                new FieldRule("Ref", "Trade ref", "REF", FieldType.Identifier, FieldRole.Key, 0, []),
                new FieldRule("TradeDate", "Trade date", "Date", FieldType.Date, FieldRole.Economic, 0, []),
            ],
            [new PassRule(["Ref"])],
            [new ComparisonRule("TradeDate", ComparisonKind.Exact, null, null, null)]);
        var request = new RunRequest(RulesProfiles.Document(rules, SetupProfiles.Draft()), new RunSide.Workbook(FakeExcel.Range), new RunSide.Workbook(FakeExcel.Range));

        Assert.Equal(["Trade date"], request.DateColumns(Side.Left), StringComparer.Ordinal);
        Assert.Equal(["Date"], request.DateColumns(Side.Right), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(0, "row 2")]
    [InlineData(1, "row 5")]
    [InlineData(2, "record 3")]
    public void A_row_is_named_by_its_sheet_row_when_known_and_its_record_number_otherwise(int ordinal, string expected)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal(expected, new SideRows("Book.xlsx · Trades", [2, 5]).Describe(ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData("P1", "Pass 1")]
    [InlineData("P12", "Pass 12")]
    [InlineData("custom", "pass custom")]
    public void A_pass_is_named_by_its_position_when_the_tab_numbered_it(string id, string expected)
    {
        Assert.Equal(expected, ResultText.Pass(id));
    }

    [Theory]
    [InlineData("Ref\u001eT-1", "T-1")]
    [InlineData("Ref\u001eT-1\u001fTradeDate\u001e2026-11-05", "T-1 · 2026-11-05")]
    [InlineData("T-1", "T-1")]
    [InlineData(null, "(none)")]
    public void A_match_key_shows_its_values_without_the_engines_separators(string? key, string expected)
    {
        Assert.Equal(expected, ResultText.KeyText(key));
    }

    private static Dictionary<string, ResultRow> Rows(ShellViewModel shell) =>
        Assert.IsType<RunView>(shell.Results.Run).Rows
            .ToDictionary(row => KeyOf(row.Entry.Left.Concat(row.Entry.Right).First()), StringComparer.Ordinal);

    private static string KeyOf(SourceRef source) =>
        (string.Equals(source.Source, "left", StringComparison.Ordinal) ? Left : Right).Rows[source.Ordinal][0] as string ?? string.Empty;

    private static FakeRuns Engine() => new((request, _, cancellationToken) =>
    {
        using var left = DataOf(Left);
        using var right = DataOf(Right);
        var result = Reconciler.Run(
            Reconciler.Compile(request.Profile, RuleRegistry.CreateCore()),
            RecordSources.FromDataReader("left", left.CreateDataReader, new TabularOptions()),
            RecordSources.FromDataReader("right", right.CreateDataReader, new TabularOptions()),
            ReferenceDataSet.Empty,
            cancellationToken: cancellationToken);
        return Task.FromResult<RunOutcome>(new RunOutcome.Completed(result, new SideRows("Book.xlsx · Trades", Left.SheetRows), SideRows.Records("broker.csv"), ReadAt));
    });

    private static DataTable DataOf(SourceTable table)
    {
        var data = new DataTable { Locale = CultureInfo.InvariantCulture };
#pragma warning disable IDISP004 // The table owns the columns it adds.
        foreach (var header in table.Headers)
            data.Columns.Add(header, typeof(object));
#pragma warning restore IDISP004
        foreach (var row in table.Rows)
        {
            object[] values = [.. row.Select(value => value ?? DBNull.Value)];
            data.Rows.Add(values);
        }

        return data;
    }

    private static async Task<ShellViewModel> ValidShell(FakeRuns runs)
    {
        var reads = 0;
        SourceRead ReadNext()
        {
            var table = reads++ % 2 == 0 ? Left : Right;
            return new SourceRead.Read(FakeExcel.Range, table, SourcePreview.Of(table));
        }

        var excel = new FakeExcel(new RangeChoice.Picked(FakeExcel.Range), _ => Task.FromResult(ReadNext()));
        var shell = new ShellViewModel(Make.Sources(excel, new FakeDialog(@"C:\data\Book.xlsx")), runs);
        await shell.Sources.Left.ChooseExcelDataCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.Sources.Right.ChooseExcelDataCommand.ExecuteAsync(null).ConfigureAwait(true);
        var rules = shell.Rules;
        rules.PairSameNamedColumnsCommand.Execute(null);
        rules.Fields[0].Type = FieldType.Identifier;
        rules.Fields[0].Role = FieldRole.Key;
        rules.Fields[1].Type = FieldType.Decimal;
        rules.Fields[1].Role = FieldRole.Economic;
        rules.Fields[1].ScaleText = "0";
        rules.Fields[2].Type = FieldType.Decimal;
        rules.Fields[2].Role = FieldRole.Economic;
        rules.Comparisons[1].Kind = ComparisonKind.Absolute;
        rules.Comparisons[1].AmountText = "0.01";
        rules.AddPassCommand.Execute(null);
        rules.Passes[0].Keys[0].IsSelected = true;
        Assert.True(shell.CanRun);
        return shell;
    }
}
