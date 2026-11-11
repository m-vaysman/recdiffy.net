namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed class LogBook : ILogFeed
{
    private readonly Lock _gate = new();
    private readonly Queue<LogEntry> _entries = new();

    public LogBook(int capacity) =>
        Capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "A log book holds at least one entry.");

    public event EventHandler<LogEntryEventArgs>? Added;

    public int Capacity { get; }

    public void Append(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            _entries.Enqueue(entry);
            if (_entries.Count > Capacity)
                _ = _entries.Dequeue();
        }

        Added?.Invoke(this, new LogEntryEventArgs(entry));
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            return [.. _entries];
        }
    }
}
