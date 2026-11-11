using System.Diagnostics;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn;

internal sealed class LogBookListener(LogBook book, TimeProvider clock) : TraceListener
{
    public override bool IsThreadSafe => true;

    public override void TraceData(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, object? data)
    {
        if (data is LogEntry entry)
            book.Append(entry);
        else
            WriteLine(data?.ToString());
    }

    public override void Write(string? message) => WriteLine(message);

    public override void WriteLine(string? message)
    {
        if (message is { Length: > 0 })
            book.Append(new LogEntry(clock.GetLocalNow(), LogSeverity.Information, LogAreas.AddIn, message));
    }
}
