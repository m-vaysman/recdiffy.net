using System.Data;
using System.Globalization;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

internal sealed class FakeOutput(Func<ResultSheet, IProgress<WriteProgress>, CancellationToken, Task<WriteOutcome>> write) : IResultOutput
{
    public List<ResultSheet> Sheets { get; } = [];

    public static FakeOutput Writing() =>
        new((sheet, _, _) => Task.FromResult<WriteOutcome>(new WriteOutcome.Completed("Book2", [sheet.Name], sheet.Results)));

    public static FakeOutput Returning(WriteOutcome outcome) => new((_, _, _) => Task.FromResult(outcome));

    public Task<WriteOutcome> WriteAsync(ResultSheet sheet, IProgress<WriteProgress> progress, CancellationToken cancellationToken)
    {
        Sheets.Add(sheet);
        return write(sheet, progress, cancellationToken);
    }
}

internal sealed class FakeSettingsStore(AddInSettings loaded, string? saveProblem = null) : ISettingsStore
{
    public List<AddInSettings> Saved { get; } = [];

    public AddInSettings Load() => loaded;

    public string? Save(AddInSettings settings)
    {
        Saved.Add(settings);
        return saveProblem;
    }
}

internal static class ResultSheetFixtures
{
    public static readonly DateTimeOffset ReadAt = new(2026, 11, 9, 17, 30, 0, TimeSpan.FromHours(1));

    public static SourceTable Left { get; } = new(
        ["Ref", "Quantity", "Price"],
        [
            ["T-1", 10m, 1.50m],
            ["T-2", 10m, 1.50m],
            ["T-3", 10m, 1.50m],
            ["T-4", 10m, 1.50m],
            ["T-6", "ten", 1.50m],
            ["T-7", 10m, 1.50m],
            ["=1+1", 10m, 1.50m],
        ])
    { SheetRows = [2, 3, 4, 5, 7, 8, 9] };

    public static SourceTable Right { get; } = new(
        ["Ref", "Quantity", "Price"],
        [
            ["T-1", 10m, 1.50m],
            ["T-2", 10m, 1.51m],
            ["T-3", 11m, 1.50m],
            ["T-5", 10m, 1.50m],
            ["T-7", 10m, 1.50m],
            ["T-7", 10m, 1.50m],
            ["T-7", 10m, 1.50m],
            ["T-7", 10m, 1.50m],
            ["T-7", 10m, 1.50m],
            ["=1+1", 10m, 1.50m],
        ]);

    public static T InCulture<T>(string name, Func<T> act)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            return act();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    public static FakeRuns Engine(SourceTable left, SourceTable right, List<RunOutcome.Completed>? completed = null) => new((request, _, cancellationToken) =>
    {
        using var leftData = DataOf(left);
        using var rightData = DataOf(right);
        var result = Reconciler.Run(
            Reconciler.Compile(request.Profile, RuleRegistry.CreateCore()),
            RecordSources.FromDataReader("left", leftData.CreateDataReader, new TabularOptions()),
            RecordSources.FromDataReader("right", rightData.CreateDataReader, new TabularOptions()),
            ReferenceDataSet.Empty,
            cancellationToken: cancellationToken);
        var outcome = new RunOutcome.Completed(result, new SideRows("Book.xlsx · Trades", left.SheetRows), SideRows.Records("broker.csv"), ReadAt);
        completed?.Add(outcome);
        return Task.FromResult<RunOutcome>(outcome);
    });

    public static Task<ShellViewModel> ValidShell(IResultOutput output, SettingsViewModel? settings = null, List<RunOutcome.Completed>? completed = null) =>
        ValidShell(Engine(Left, Right, completed), output, settings ?? new SettingsViewModel(), Left, Right);

    public static async Task<ShellViewModel> ValidShell(
        IReconciliationRuns runs,
        IResultOutput output,
        SettingsViewModel settings,
        SourceTable left,
        SourceTable right,
        Action<RulesViewModel>? configure = null)
    {
        var reads = 0;
        SourceRead ReadNext()
        {
            var table = reads++ % 2 == 0 ? left : right;
            return new SourceRead.Read(FakeExcel.Range, table, SourcePreview.Of(table));
        }

        var excel = new FakeExcel(new RangeChoice.Picked(FakeExcel.Range), _ => Task.FromResult(ReadNext()));
        var shell = new ShellViewModel(Make.Sources(excel, new FakeDialog(@"C:\data\Book.xlsx")), runs, output, settings);
        await shell.Sources.Left.ChooseExcelDataCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.Sources.Right.ChooseExcelDataCommand.ExecuteAsync(null).ConfigureAwait(true);
        shell.Rules.PairSameNamedColumnsCommand.Execute(null);
        (configure ?? Trades)(shell.Rules);
        shell.Rules.AddPassCommand.Execute(null);
        shell.Rules.Passes[0].Keys[0].IsSelected = true;
        Assert.True(shell.CanRun, string.Join(" ", shell.Rules.Problems));
        return shell;
    }

    private static void Trades(RulesViewModel rules)
    {
        rules.Fields[0].Type = FieldType.Identifier;
        rules.Fields[0].Role = FieldRole.Key;
        rules.Fields[1].Type = FieldType.Decimal;
        rules.Fields[1].Role = FieldRole.Economic;
        rules.Fields[1].ScaleText = "0";
        rules.Fields[2].Type = FieldType.Decimal;
        rules.Fields[2].Role = FieldRole.Economic;
        rules.Comparisons[1].Kind = ComparisonKind.Absolute;
        rules.Comparisons[1].AmountText = "0.01";
    }

    public static IReadOnlyList<SheetRow> AllRows(ResultSheet sheet) => [.. sheet.Pages.SelectMany(page => page.Rows)];

    public static IReadOnlyList<SheetRow> ResultRows(ResultSheet sheet) => [.. AllRows(sheet).Where(row => row.Kind is SheetRowKind.Result)];

    public static SheetValue Cell(SheetRow row, int column) => column < row.Cells.Count ? row.Cells[column] : SheetValue.Empty;

    public static string? TextOf(SheetValue value) => value is SheetValue.Text text ? text.Value : null;

    public static IEnumerable<string> Texts(SheetRow row) => row.Cells.Select(cell => TextOf(cell) ?? string.Empty);

    public static SheetRow RowWithOutcome(ResultSheet sheet, string outcome, string? leftKey = null) =>
        ResultRows(sheet).First(row =>
            TextOf(row.Cells[0]) == outcome
            && (leftKey is null || TextOf(row.Cells[1]) == leftKey || TextOf(row.Cells[3]) == leftKey));

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
