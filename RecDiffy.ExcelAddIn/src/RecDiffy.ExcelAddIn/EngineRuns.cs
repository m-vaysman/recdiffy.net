using System.Data;
using System.IO;
using System.Text;
using RecDiffy.ExcelAddIn.ViewModels;
using RecDiffy.IO;
using RecDiffy.Results;

namespace RecDiffy.ExcelAddIn;

public sealed class EngineRuns(IExcelSources excel, TimeProvider clock) : IReconciliationRuns
{
    private readonly IExcelSources _excel = excel ?? throw new ArgumentNullException(nameof(excel));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public async Task<RunOutcome> RunAsync(RunRequest request, IProgress<RunProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        try
        {
            var compiled = Reconciler.Compile(request.Profile, RuleRegistry.CreateCore());
            var readAt = _clock.GetLocalNow();
            var left = await ReadAsync(request, Side.Left, progress, cancellationToken).ConfigureAwait(false);
            if (left is SideData.Rejected leftProblem)
                return new RunOutcome.Failed(leftProblem.Reason);

            var right = await ReadAsync(request, Side.Right, progress, cancellationToken).ConfigureAwait(false);
            if (right is SideData.Rejected rightProblem)
                return new RunOutcome.Failed(rightProblem.Reason);

            progress.Report(new RunProgress("Reconciling", null));
            var result = await Task.Run(() => Reconcile(compiled, left, right, cancellationToken), cancellationToken).ConfigureAwait(false);
            return new RunOutcome.Completed(result, RowsOf(left), RowsOf(right), readAt);
        }
        catch (OperationCanceledException)
        {
            return new RunOutcome.Canceled();
        }
        catch (ProfileCompileException exception)
        {
            return new RunOutcome.Failed($"The rules can't be compiled: {exception.Message}");
        }
        catch (Exception exception) when (exception is FormatException or InvalidDataException or DecoderFallbackException)
        {
            return new RunOutcome.Failed($"A CSV file can't be read with its settings: {exception.Message}");
        }
        catch (Exception exception)
        {
            return new RunOutcome.Failed($"The reconciliation stopped with an error: {exception.Message}");
        }
    }

    private Task<SideData> ReadAsync(RunRequest request, Side side, IProgress<RunProgress> progress, CancellationToken cancellationToken)
    {
        var stage = $"Reading the {RunRequest.NameOf(side)} data";
        progress.Report(new RunProgress(stage, null));
        var source = side is Side.Left ? request.Left : request.Right;
        return source switch
        {
            RunSide.Workbook workbook => ReadWorkbookAsync(workbook.Range, request.DateColumns(side), new StageProgress(stage, progress), cancellationToken),
            RunSide.Csv csv => ReadCsvAsync(csv.Source, cancellationToken),
            _ => throw new InvalidOperationException("A side is read from a workbook or a CSV file."),
        };
    }

    private async Task<SideData> ReadWorkbookAsync(ExcelRangePick range, IReadOnlySet<string> dateColumns, IProgress<double> progress, CancellationToken cancellationToken) =>
        await _excel.ReadAsync(range, progress, cancellationToken).ConfigureAwait(false) switch
        {
            SourceRead.Read read => new SideData.Workbook(read.Table, dateColumns, new SideRows($"{range.Workbook} · {range.Sheet}", read.Table.SheetRows)),
            SourceRead.Rejected rejected => new SideData.Rejected(rejected.Reason),
            _ => throw new InvalidOperationException("A source read is either read or rejected."),
        };

    private static async Task<SideData> ReadCsvAsync(CsvSource csv, CancellationToken cancellationToken)
    {
        try
        {
            var file = new FileStream(csv.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81_920, useAsync: true);
            await using (file.ConfigureAwait(false))
            {
                using var copy = new MemoryStream();
                await file.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
                return new SideData.Csv(csv, copy.ToArray(), SideRows.Records(Path.GetFileName(csv.Path)));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SideData.Rejected(CsvFileSources.Reason(csv, exception));
        }
    }

    private static ReconciliationResult Reconcile(CompiledProfile compiled, SideData left, SideData right, CancellationToken cancellationToken)
    {
        using var leftTable = TableOf(left);
        using var rightTable = TableOf(right);
        using var leftBytes = StreamOf(left);
        using var rightBytes = StreamOf(right);
        return Reconciler.Run(
            compiled,
            SourceOf(Side.Left, left, leftTable, leftBytes),
            SourceOf(Side.Right, right, rightTable, rightBytes),
            ReferenceDataSet.Empty,
            cancellationToken: cancellationToken);
    }

    private static DataTable? TableOf(SideData side) =>
        side is SideData.Workbook workbook ? RunTables.ToDataTable(workbook.Table, workbook.DateColumns) : null;

    private static MemoryStream? StreamOf(SideData side) =>
        side is SideData.Csv csv ? new MemoryStream(csv.Bytes, writable: false) : null;

    private static RecordSource SourceOf(Side side, SideData data, DataTable? table, MemoryStream? bytes) => (data, table, bytes) switch
    {
        (SideData.Workbook, { } rows, _) => RecordSources.FromDataReader(RunRequest.NameOf(side), rows.CreateDataReader, new TabularOptions()),
        (SideData.Csv csv, _, { } stream) => FileSources.FromCsv(RunRequest.NameOf(side), stream, CsvFileSources.Options(csv.Source)),
        _ => throw new InvalidOperationException("A side is read from a workbook or a CSV file."),
    };

    private static SideRows RowsOf(SideData side) => side switch
    {
        SideData.Workbook workbook => workbook.Rows,
        SideData.Csv csv => csv.Rows,
        _ => throw new InvalidOperationException("Only a side that was read has rows."),
    };

    private abstract record SideData
    {
        public sealed record Workbook(SourceTable Table, IReadOnlySet<string> DateColumns, SideRows Rows) : SideData;

        public sealed record Csv(CsvSource Source, byte[] Bytes, SideRows Rows) : SideData;

        public sealed record Rejected(string Reason) : SideData;
    }

    private sealed class StageProgress(string stage, IProgress<RunProgress> progress) : IProgress<double>
    {
        public void Report(double value) => progress.Report(new RunProgress(stage, value));
    }
}
