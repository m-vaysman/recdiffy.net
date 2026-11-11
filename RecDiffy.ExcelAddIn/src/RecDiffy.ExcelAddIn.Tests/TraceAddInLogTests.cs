using System.Diagnostics;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.Tests;

public sealed class TraceAddInLogTests
{
    [Fact]
    public void A_write_reaches_the_log_book_through_the_add_in_s_own_trace_source()
    {
        var book = new LogBook(10);
        using var listener = new LogBookListener(book, FixedClock.Instance);
        var source = TraceAddInLog.CreateSource([listener]);
        var log = new TraceAddInLog(source, FixedClock.Instance);

        log.Write(LogSeverity.Warning, LogAreas.Csv, "Rejected broker.csv.");

        Assert.Equal(new LogEntry(FixedClock.Now, LogSeverity.Warning, LogAreas.Csv, "Rejected broker.csv."), Assert.Single(book.Snapshot()));
    }

    [Fact]
    public void The_source_has_only_the_listeners_it_is_given()
    {
        var book = new LogBook(10);
        using var listener = new LogBookListener(book, FixedClock.Instance);

        var source = TraceAddInLog.CreateSource([listener]);

        Assert.Equal(TraceAddInLog.SourceName, source.Name);
        Assert.Same(listener, Assert.Single(source.Listeners.Cast<TraceListener>()));
        Assert.True(source.Switch.ShouldTrace(TraceEventType.Information));
        Assert.False(source.Switch.ShouldTrace(TraceEventType.Verbose));
    }

    [Theory]
    [InlineData(LogSeverity.Information, TraceEventType.Information)]
    [InlineData(LogSeverity.Warning, TraceEventType.Warning)]
    [InlineData(LogSeverity.Error, TraceEventType.Error)]
    public void Each_severity_is_its_trace_event_type(LogSeverity severity, TraceEventType expected) =>
        Assert.Equal(expected, TraceAddInLog.EventType(severity));

    [Fact]
    public void Plain_text_traced_to_the_log_book_becomes_an_information_entry()
    {
        var book = new LogBook(10);
        using var listener = new LogBookListener(book, FixedClock.Instance);

        listener.WriteLine("Plain text.");
        listener.WriteLine(string.Empty);

        Assert.Equal(new LogEntry(FixedClock.Now, LogSeverity.Information, LogAreas.AddIn, "Plain text."), Assert.Single(book.Snapshot()));
    }
}

internal sealed class FixedClock : TimeProvider
{
    public static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 30, 0, TimeSpan.Zero);

    public static FixedClock Instance { get; } = new();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => Now;
}
