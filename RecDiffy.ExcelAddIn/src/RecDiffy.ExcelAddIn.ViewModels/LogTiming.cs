namespace RecDiffy.ExcelAddIn.ViewModels;

public readonly record struct LogLine(LogSeverity Severity, string Message);

internal sealed class LogTiming
{
    private readonly IAddInLog _log;
    private readonly TimeProvider _clock;
    private readonly string _area;

    public LogTiming(IAddInLog log, TimeProvider clock, string area)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _area = area;
    }

    public void Write(LogSeverity severity, string message, IEnumerable<string?> paths) =>
        _log.Write(severity, _area, LogText.Sanitize(message, paths));

    public async Task<T> RunAsync<T>(string doing, Func<Task<T>> call, Func<T, TimeSpan, LogLine> describe, IReadOnlyList<string?> paths)
    {
        var started = _clock.GetTimestamp();
        try
        {
            var result = await call().ConfigureAwait(false);
            Write(describe(result, _clock.GetElapsedTime(started)), paths);
            return result;
        }
        catch (OperationCanceledException)
        {
            Write(LogSeverity.Information, $"{doing} was canceled after {LogText.Seconds(_clock.GetElapsedTime(started))}.", paths);
            throw;
        }
        catch (Exception exception)
        {
            Write(LogSeverity.Error, $"{doing} failed after {LogText.Seconds(_clock.GetElapsedTime(started))}: {LogText.Failure(exception, paths)}", paths);
            throw;
        }
    }

    public T Run<T>(string doing, Func<T> call, Func<T, LogLine> describe)
    {
        try
        {
            var result = call();
            Write(describe(result), []);
            return result;
        }
        catch (Exception exception)
        {
            Write(LogSeverity.Error, $"{doing} failed: {LogText.Failure(exception)}", []);
            throw;
        }
    }

    private void Write(LogLine line, IEnumerable<string?> paths) => Write(line.Severity, line.Message, paths);
}
