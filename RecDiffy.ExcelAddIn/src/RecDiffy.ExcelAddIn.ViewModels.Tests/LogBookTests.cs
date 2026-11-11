using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public sealed class LogBookTests
{
    [Fact]
    public void A_full_book_drops_its_oldest_entry()
    {
        var book = new LogBook(2);
        var first = LogEntries.At(0, LogSeverity.Information, "first");
        var second = LogEntries.At(1, LogSeverity.Information, "second");
        var third = LogEntries.At(2, LogSeverity.Information, "third");

        book.Append(first);
        book.Append(second);
        book.Append(third);

        Assert.Equal([second, third], book.Snapshot());
    }

    [Fact]
    public void Each_entry_is_announced_once()
    {
        var book = new LogBook(10);
        var announced = new List<LogEntry>();
        book.Added += (_, e) => announced.Add(e.Entry);
        var entry = LogEntries.At(0, LogSeverity.Warning, "careful");

        book.Append(entry);

        Assert.Same(entry, Assert.Single(announced));
    }

    [Fact]
    public void A_snapshot_does_not_change_when_the_book_does()
    {
        var book = new LogBook(10);
        book.Append(LogEntries.At(0, LogSeverity.Information, "before"));
        var snapshot = book.Snapshot();

        book.Append(LogEntries.At(1, LogSeverity.Information, "after"));

        _ = Assert.Single(snapshot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_book_holds_at_least_one_entry(int capacity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogBook(capacity));
}
