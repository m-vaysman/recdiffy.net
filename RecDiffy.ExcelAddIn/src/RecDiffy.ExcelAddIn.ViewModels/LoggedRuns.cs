using System.Globalization;
using RecDiffy.Results;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed class LoggedRuns : IReconciliationRuns
{
    private readonly IReconciliationRuns _inner;
    private readonly LogTiming _timing;

    public LoggedRuns(IReconciliationRuns inner, IAddInLog log, TimeProvider clock)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _timing = new LogTiming(log, clock, LogAreas.Run);
    }

    public Task<RunOutcome> RunAsync(RunRequest request, IProgress<RunProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string?[] paths = [PathOf(request.Left), PathOf(request.Right)];
        _timing.Write(LogSeverity.Information, $"Run started: left {Describe(request.Left)}, right {Describe(request.Right)}.", paths);
        return _timing.RunAsync("The run", () => _inner.RunAsync(request, progress, cancellationToken), Finished, paths);
    }

    public static string Counts(RunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return string.Join(", ", ResultText.Counts(summary).Select(count => string.Create(CultureInfo.InvariantCulture, $"{count.Label} {count.Count:N0}")));
    }

    private static LogLine Finished(RunOutcome outcome, TimeSpan elapsed) => outcome switch
    {
        RunOutcome.Completed completed => new(LogSeverity.Information, $"Run finished in {LogText.Seconds(elapsed)}: {Counts(completed.Result.Summary)}."),
        RunOutcome.Canceled => new(LogSeverity.Information, $"Run canceled after {LogText.Seconds(elapsed)}."),
        RunOutcome.Failed failed => new(LogSeverity.Warning, $"Run failed after {LogText.Seconds(elapsed)}: {failed.Reason}"),
        _ => throw new InvalidOperationException("A run is completed, canceled or failed."),
    };

    private static string Describe(RunSide side) => side switch
    {
        RunSide.Workbook workbook => $"{workbook.Range.Address} on [{workbook.Range.Workbook}]{workbook.Range.Sheet}",
        RunSide.Csv csv => $"{LogText.FileName(csv.Source.Path)} ({LoggedCsvSources.Settings(csv.Source)})",
        _ => throw new InvalidOperationException("A side is read from a workbook or a CSV file."),
    };

    private static string? PathOf(RunSide side) => side switch
    {
        RunSide.Workbook workbook => workbook.Range.Path,
        RunSide.Csv csv => csv.Source.Path,
        _ => null,
    };
}
