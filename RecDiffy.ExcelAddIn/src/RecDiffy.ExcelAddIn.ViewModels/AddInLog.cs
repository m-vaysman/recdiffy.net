namespace RecDiffy.ExcelAddIn.ViewModels;

public enum LogSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record LogEntry(DateTimeOffset At, LogSeverity Severity, string Area, string Message);

public interface IAddInLog
{
    void Write(LogSeverity severity, string area, string message);
}

public sealed class LogEntryEventArgs(LogEntry entry) : EventArgs
{
    public LogEntry Entry { get; } = entry ?? throw new ArgumentNullException(nameof(entry));
}

public interface ILogFeed
{
    event EventHandler<LogEntryEventArgs>? Added;

    IReadOnlyList<LogEntry> Snapshot();
}

public interface ILogShell
{
    string FileStatus { get; }

    void Copy(string text);

    void OpenFolder();
}

public static class LogAreas
{
    public const string AddIn = "Add-in";

    public const string Excel = "Excel";

    public const string Csv = "CSV";

    public const string Run = "Run";

    public const string Worksheet = "Worksheet";

    public const string Settings = "Settings";

    public const string Window = "Window";

    public const string LogWindow = "Log window";
}
