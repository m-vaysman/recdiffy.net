using System.Globalization;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed class LoggedResultOutput : IResultOutput
{
    private readonly IResultOutput _inner;
    private readonly LogTiming _timing;

    public LoggedResultOutput(IResultOutput inner, IAddInLog log, TimeProvider clock)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _timing = new LogTiming(log, clock, LogAreas.Worksheet);
    }

    public Task<WriteOutcome> WriteAsync(ResultSheet sheet, IProgress<WriteProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        _timing.Write(LogSeverity.Information, $"Writing {LogText.Count(sheet.Results, "result", "results")} to a new workbook.", []);
        return _timing.RunAsync("Writing the results", () => _inner.WriteAsync(sheet, progress, cancellationToken), Written, []);
    }

    private static LogLine Written(WriteOutcome outcome, TimeSpan elapsed) => outcome switch
    {
        WriteOutcome.Completed completed => new(
            LogSeverity.Information,
            $"Wrote {LogText.Count(completed.Results, "result", "results")} to {Where(completed.Workbook, completed.Sheets)} in {LogText.Seconds(elapsed)}."),
        WriteOutcome.Canceled canceled => new(
            LogSeverity.Information,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Writing was canceled after {canceled.Written:N0} of {LogText.Count(canceled.Total, "result", "results")}, in {LogText.Seconds(elapsed)}. {Where(canceled.Workbook, canceled.Sheets)} keeps what was written.")),
        WriteOutcome.Failed failed => new(
            LogSeverity.Warning,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Writing failed after {failed.Written:N0} of {LogText.Count(failed.Total, "result", "results")}, in {LogText.Seconds(elapsed)}: {failed.Reason}")),
        _ => throw new InvalidOperationException("A write is written, canceled or failed."),
    };

    private static string Where(string workbook, IReadOnlyList<string> sheets) =>
        sheets is [] ? $"[{workbook}]" : $"[{workbook}]{string.Join(", ", sheets)}";
}
