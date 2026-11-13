using System.Data;
using System.Globalization;
using RecDiffy.ExcelAddIn.ViewModels;
using RecDiffy.Results;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public class AmbiguousResultTextTests
{

    private static readonly DateTimeOffset ReadAt = new(2026, 11, 10, 17, 30, 0, TimeSpan.Zero);

    private static readonly SourceTable Left = new(
        ["Ref", "Day", "Amount"],
        [["T-1", "2026-11-05", "100.00"], ["T-2", "2026-11-05", "250.00"]]);

    private static readonly SourceTable Right = new(
        ["Ref", "Day", "Amount"],
        [["T-1", "2026-11-05", "100.00"], ["T-2", "2026-11-05", "250.00"], ["T-2", "2026-11-06", "250.00"]]);

    [Fact]
    public async Task The_results_tab_names_the_key_an_ambiguous_results_records_share()
    {
        var row = await AmbiguousRow();

        Assert.Equal("3 records share key T-2, so none was picked.", row.Details);
    }

    [Fact]
    public async Task The_explanation_names_the_key_an_ambiguous_results_records_share()
    {
        var row = await AmbiguousRow();

        var matching = Assert.Single(row.Explain(), section => section.Title == "Matching").Lines;
        Assert.Contains("Pass 1: 3 records share key T-2, so none was picked.", matching, StringComparer.Ordinal);
    }

    [Fact]
    public async Task The_explanation_of_an_ambiguous_result_does_not_say_a_pass_matched_it()
    {
        var row = await AmbiguousRow();

        var matching = Assert.Single(row.Explain(), section => section.Title == "Matching").Lines;
        Assert.DoesNotContain(matching, line => line.StartsWith("Matched by", StringComparison.Ordinal));
    }

    private static async Task<ResultRow> AmbiguousRow()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var shell = await ValidShell(Engine(Left, Right), Left, Right, RefDayAmount).ConfigureAwait(true);
            await shell.RunCommand.ExecuteAsync(null).ConfigureAwait(true);
            var rows = Assert.IsType<RunView>(shell.Results.Run).Rows;
            Assert.Single(rows, row => row.Entry.Alignment is Alignment.Matched);
            return Assert.Single(rows, row => row.Entry.Alignment is Alignment.Ambiguous);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static void RefDayAmount(RulesViewModel rules)
    {
        rules.Fields[0].Type = FieldType.Identifier;
        rules.Fields[0].Role = FieldRole.Key;
        rules.Fields[1].Type = FieldType.Date;
        rules.Fields[1].Role = FieldRole.Economic;
        rules.Fields[1].DateFormatsText = "yyyy-MM-dd";
        rules.Fields[2].Type = FieldType.Decimal;
        rules.Fields[2].Role = FieldRole.Economic;
        rules.Fields[2].ScaleText = "2";
    }

    private static FakeRuns Engine(SourceTable left, SourceTable right) => new((request, _, cancellationToken) =>
    {
        using var leftData = DataOf(left);
        using var rightData = DataOf(right);
        var result = Reconciler.Run(
            Reconciler.Compile(request.Profile, RuleRegistry.CreateCore()),
            RecordSources.FromDataReader("left", leftData.CreateDataReader, new TabularOptions()),
            RecordSources.FromDataReader("right", rightData.CreateDataReader, new TabularOptions()),
            ReferenceDataSet.Empty,
            cancellationToken: cancellationToken);
        return Task.FromResult<RunOutcome>(new RunOutcome.Completed(result, SideRows.Records("left.csv"), SideRows.Records("right.csv"), ReadAt));
    });

    private static async Task<ShellViewModel> ValidShell(IReconciliationRuns runs, SourceTable left, SourceTable right, Action<RulesViewModel> configure)
    {
        var reads = 0;
        SourceRead ReadNext()
        {
            var table = reads++ % 2 == 0 ? left : right;
            return new SourceRead.Read(FakeExcel.Range, table, SourcePreview.Of(table));
        }

        var excel = new FakeExcel(new RangeChoice.Picked(FakeExcel.Range), _ => Task.FromResult(ReadNext()));
        var shell = new ShellViewModel(Make.Sources(excel, new FakeDialog(@"C:\data\Book.xlsx")), runs);
        await shell.Sources.Left.ChooseExcelDataCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.Sources.Right.ChooseExcelDataCommand.ExecuteAsync(null).ConfigureAwait(true);
        shell.Rules.PairSameNamedColumnsCommand.Execute(null);
        configure(shell.Rules);
        shell.Rules.AddPassCommand.Execute(null);
        shell.Rules.Passes[0].Keys[0].IsSelected = true;
        Assert.True(shell.CanRun, string.Join(" ", shell.Rules.Problems));
        return shell;
    }

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
}
