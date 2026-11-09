using System.IO;
using System.Text;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.Tests;

public sealed class EngineRunsTests : IDisposable
{
    private static readonly ExcelRangePick Range = new("Blotter.xlsx", "Trades", "$A$1:$D$6", 0, 0, 6, 4);

    private static readonly RulesState Rules = new(
        [
            new FieldRule("Ref", "TradeRef", "TradeRef", FieldType.Identifier, FieldRole.Key, 0, []),
            new FieldRule("TradeDate", "TradeDate", "TradeDate", FieldType.Date, FieldRole.Economic, 0, ["yyyy-MM-dd"]),
            new FieldRule("Quantity", "Qty", "Quantity", FieldType.Decimal, FieldRole.Economic, 0, []),
            new FieldRule("Price", "Price", "Price", FieldType.Decimal, FieldRole.Economic, 2, []),
        ],
        [new PassRule(["Ref"])],
        [
            new ComparisonRule("TradeDate", ComparisonKind.Exact, null, null, null),
            new ComparisonRule("Quantity", ComparisonKind.Exact, null, null, null),
            new ComparisonRule("Price", ComparisonKind.Absolute, 0.01m, null, null),
        ]);

    private static readonly ProfileDocument Profile =
        RulesProfiles.Document(Rules, SetupProfiles.WithCultures(SetupProfiles.Draft(), "en-US", "en-US"));

    private readonly string _folder = Directory.CreateTempSubdirectory("recdiffy-runs-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(false, 46331)]
    [InlineData(true, 44869)]
    public async Task A_workbook_against_a_csv_file_reconciles_with_excel_dates_read_in_the_workbooks_date_system(bool uses1904, int tradeDay)
    {
        var csv = WriteCsv(
            "TradeRef,TradeDate,Quantity,Price",
            "T-1,2026-11-05,10,1.50",
            "T-2,2026-11-05,10,1.51",
            "T-3,2026-11-05,11,1.50",
            "T-5,2026-11-05,10,1.50");
        var table = new SourceTable(
            ["TradeRef", "TradeDate", "Qty", "Price"],
            [
                ["T-1", (decimal)tradeDay, 10m, 1.50m],
                ["T-2", (decimal)tradeDay, 10m, 1.50m],
                ["T-3", (decimal)tradeDay, 10m, 1.50m],
                ["T-4", (decimal)tradeDay, 10m, 1.50m],
            ])
        { SheetRows = [2, 3, 5, 6], Uses1904 = uses1904 };
        var runs = new EngineRuns(new ReadingExcel(new SourceRead.Read(Range, table, SourcePreview.Of(table))), new FixedClock());

        var outcome = await runs.RunAsync(Request(new RunSide.Workbook(Range), new RunSide.Csv(csv)), new Reports(), CancellationToken.None);

        var completed = Assert.IsType<RunOutcome.Completed>(outcome);
        var summary = completed.Result.Summary;
        Assert.Equal((1, 1, 1, 1, 1), (summary.Exact, summary.WithinTolerance, summary.Break, summary.LeftOnly, summary.RightOnly));
        Assert.Equal("Blotter.xlsx · Trades", completed.Left.Label);
        Assert.Equal([2, 3, 5, 6], completed.Left.SheetRows);
        Assert.Equal("broker.csv", completed.Right.Label);
        Assert.Equal(FixedClock.Now, completed.ReadAt.ToUniversalTime());
    }

    [Fact]
    public async Task The_run_reports_reading_each_side_and_then_reconciling()
    {
        var csv = WriteCsv("TradeRef,TradeDate,Quantity,Price", "T-1,2026-11-05,10,1.50");
        var reports = new Reports();
        var runs = new EngineRuns(new ReadingExcel(new SourceRead.Rejected("unused")), new FixedClock());

        await runs.RunAsync(Request(new RunSide.Csv(csv), new RunSide.Csv(csv)), reports, CancellationToken.None);

        Assert.Equal(["Reading the left data", "Reading the right data", "Reconciling"], reports.Stages, StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_workbook_that_cant_be_read_fails_the_run_with_excels_reason()
    {
        var runs = new EngineRuns(new ReadingExcel(new SourceRead.Rejected("Excel couldn't read $A$1:$D$6 on [Blotter.xlsx]Trades.")), new FixedClock());

        var outcome = await runs.RunAsync(Request(new RunSide.Workbook(Range), new RunSide.Workbook(Range)), new Reports(), CancellationToken.None);

        Assert.Equal("Excel couldn't read $A$1:$D$6 on [Blotter.xlsx]Trades.", Assert.IsType<RunOutcome.Failed>(outcome).Reason);
    }

    [Fact]
    public async Task A_missing_csv_file_fails_the_run_and_names_the_file()
    {
        var missing = CsvSource.At(Path.Combine(_folder, "gone.csv"));
        var runs = new EngineRuns(new ReadingExcel(new SourceRead.Rejected("unused")), new FixedClock());

        var outcome = await runs.RunAsync(Request(new RunSide.Csv(missing), new RunSide.Csv(missing)), new Reports(), CancellationToken.None);

        Assert.Equal("gone.csv wasn't found. Choose the file again.", Assert.IsType<RunOutcome.Failed>(outcome).Reason);
    }

    [Fact]
    public async Task Rules_the_engine_cant_compile_fail_the_run_with_its_errors()
    {
        var csv = WriteCsv("TradeRef,TradeDate,Quantity,Price", "T-1,2026-11-05,10,1.50");
        var broken = Profile with { Fields = [.. Profile.Fields, Profile.Fields[0]] };
        var runs = new EngineRuns(new ReadingExcel(new SourceRead.Rejected("unused")), new FixedClock());

        var outcome = await runs.RunAsync(new RunRequest(broken, new RunSide.Csv(csv), new RunSide.Csv(csv)), new Reports(), CancellationToken.None);

        Assert.StartsWith("The rules can't be compiled:", Assert.IsType<RunOutcome.Failed>(outcome).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_canceled_run_reports_cancellation_not_a_result()
    {
        var csv = WriteCsv("TradeRef,TradeDate,Quantity,Price", "T-1,2026-11-05,10,1.50");
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        var runs = new EngineRuns(new ReadingExcel(new SourceRead.Rejected("unused")), new FixedClock());

        var outcome = await runs.RunAsync(Request(new RunSide.Csv(csv), new RunSide.Csv(csv)), new Reports(), canceled.Token);

        Assert.IsType<RunOutcome.Canceled>(outcome);
    }

    private static RunRequest Request(RunSide left, RunSide right) => new(Profile, left, right);

    private CsvSource WriteCsv(params string[] lines)
    {
        var path = Path.Combine(_folder, "broker.csv");
        File.WriteAllText(path, string.Join("\n", lines) + "\n", Encoding.UTF8);
        return CsvSource.At(path) with { Delimiter = ',' };
    }

    private sealed class ReadingExcel(SourceRead read) : IExcelSources
    {
        public Task<RangeChoice> PickRangeAsync(string workbookPath, string prompt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A run doesn't pick ranges.");

        public Task<RangeChoice> PickOpenRangeAsync(string prompt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A run doesn't pick ranges.");

        public Task<SourceRead> ReadAsync(ExcelRangePick range, IProgress<double> progress, CancellationToken cancellationToken)
        {
            progress.Report(1);
            return Task.FromResult(read);
        }
    }

    private sealed class Reports : IProgress<RunProgress>
    {
        public List<string> Stages { get; } = [];

        public void Report(RunProgress value)
        {
            if (Stages.LastOrDefault() != value.Stage)
                Stages.Add(value.Stage);
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public static DateTimeOffset Now { get; } = new(2026, 11, 9, 17, 30, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
