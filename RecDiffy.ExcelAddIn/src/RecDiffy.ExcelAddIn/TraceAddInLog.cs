using System.Diagnostics;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn;

internal sealed class TraceAddInLog(TraceSource source, TimeProvider clock) : IAddInLog
{
    public const string SourceName = "RecDiffy.ExcelAddIn";

    public static TraceSource CreateSource(IReadOnlyList<TraceListener> listeners)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        var source = new TraceSource(SourceName, SourceLevels.Information);
        source.Listeners.Clear();
        foreach (var listener in listeners)
            _ = source.Listeners.Add(listener);

        return source;
    }

    public static TraceEventType EventType(LogSeverity severity) => severity switch
    {
        LogSeverity.Information => TraceEventType.Information,
        LogSeverity.Warning => TraceEventType.Warning,
        LogSeverity.Error => TraceEventType.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "A log entry is information, a warning or an error."),
    };

    public void Write(LogSeverity severity, string area, string message) =>
        source.TraceData(EventType(severity), (int)severity, new LogEntry(clock.GetLocalNow(), severity, area, message));
}
