using System.Diagnostics;
using System.IO;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.Tests;

public sealed class LogFileListenerTests : IDisposable
{
    private static readonly LogEntry Started = new(FixedClock.Now, LogSeverity.Information, LogAreas.Run, "Run started.");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"recdiffy-log-tests-{Guid.NewGuid():N}");
    private readonly List<string> _failures = [];

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Entries_go_to_one_file_per_day_and_Excel_process()
    {
        using var listener = Listener(_folder);
        var finished = Started with { Message = "Run finished." };
        var nextDay = Started with { At = Started.At.AddDays(1), Message = "Next day." };

        listener.Append(Started);
        listener.Append(finished);
        listener.Append(nextDay);

        Assert.Equal([LogText.Format(Started), LogText.Format(finished)], File.ReadAllLines(Path.Combine(_folder, "RecDiffy-2026-11-11-4242.log")));
        Assert.Equal([LogText.Format(nextDay)], File.ReadAllLines(Path.Combine(_folder, "RecDiffy-2026-11-12-4242.log")));
        Assert.Null(listener.Failure);
        Assert.Empty(_failures);
    }

    [Fact]
    public void A_log_file_has_no_byte_order_mark()
    {
        using var listener = Listener(_folder);

        listener.Append(Started);

        Assert.Equal((byte)'2', File.ReadAllBytes(listener.PathFor(Started.At))[0]);
    }

    [Fact]
    public void Entries_traced_through_a_source_and_plain_text_are_both_written()
    {
        using var listener = Listener(_folder);

        listener.TraceData(null, TraceAddInLog.SourceName, TraceEventType.Warning, 1, Started);
        listener.WriteLine("Plain text.");

        Assert.Equal(
            [LogText.Format(Started), LogText.Format(new LogEntry(FixedClock.Now, LogSeverity.Information, LogAreas.AddIn, "Plain text."))],
            File.ReadAllLines(listener.PathFor(Started.At)));
    }

    [Fact]
    public void A_folder_that_cannot_be_written_is_reported_once_and_the_file_is_then_left_alone()
    {
        _ = Directory.CreateDirectory(_folder);
        var blocked = Path.Combine(_folder, "blocked");
        File.WriteAllText(blocked, "A file where the log folder should be.");
        using var listener = Listener(blocked);

        listener.Append(Started);
        listener.Append(Started);

        var failure = Assert.Single(_failures);
        Assert.StartsWith("The log file can't be written, so this session is logged only in the log window: IOException", failure, StringComparison.Ordinal);
        Assert.Equal(failure, listener.Failure);
    }

    [Fact]
    public void Log_files_older_than_the_limit_are_deleted_and_other_files_are_kept()
    {
        _ = Directory.CreateDirectory(_folder);
        var old = Touch("RecDiffy-2026-10-01-1.log", FixedClock.Now.AddDays(-41));
        var recent = Touch("RecDiffy-2026-11-10-1.log", FixedClock.Now.AddDays(-1));
        var other = Touch("notes.txt", FixedClock.Now.AddDays(-41));
        using var listener = Listener(_folder);

        var deleted = listener.DeleteOlderThan(LogFileListener.KeptFor);

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void Deleting_old_files_from_a_folder_that_does_not_exist_deletes_nothing()
    {
        using var listener = Listener(_folder);

        Assert.Equal(0, listener.DeleteOlderThan(LogFileListener.KeptFor));
    }

    [Fact]
    public void The_folder_is_under_local_app_data()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecDiffy", "Logs"),
            LogFileListener.ForWindowsUser());
    }

    private LogFileListener Listener(string folder) => new(folder, 4242, FixedClock.Instance, _failures.Add);

    private string Touch(string name, DateTimeOffset written)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);
        return path;
    }
}
