using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

internal sealed record Logged(LogSeverity Severity, string Area, string Message);

internal sealed class RecordingLog : IAddInLog
{
    public List<Logged> Entries { get; } = [];

    public void Write(LogSeverity severity, string area, string message) => Entries.Add(new Logged(severity, area, message));
}

internal sealed class StepClock(TimeSpan step) : TimeProvider
{
    public static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 30, 0, TimeSpan.Zero);

    private long _timestamp;

    public static StepClock QuarterSecond() => new(TimeSpan.FromMilliseconds(250));

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override long GetTimestamp()
    {
        var now = _timestamp;
        _timestamp += step.Ticks;
        return now;
    }

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeLogShell : ILogShell
{
    public List<string> Copied { get; } = [];

    public int FolderOpened { get; private set; }

    public string FileStatus => "Also saved to RecDiffy-test.log.";

    public void Copy(string text) => Copied.Add(text);

    public void OpenFolder() => FolderOpened++;
}

internal sealed class FakeFeed(params LogEntry[] snapshot) : ILogFeed
{
    public event EventHandler<LogEntryEventArgs>? Added;

    public bool HasListeners => Added is not null;

    public IReadOnlyList<LogEntry> Snapshot() => snapshot;

    public void Raise(LogEntry entry) => Added?.Invoke(this, new LogEntryEventArgs(entry));
}

internal static class LogEntries
{
    public static LogEntry At(int minute, LogSeverity severity, string message) =>
        new(StepClock.Now.AddMinutes(minute), severity, LogAreas.Run, message);
}
