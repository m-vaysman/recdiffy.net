using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn.ViewModels.Tests;

public sealed class LogViewModelTests
{
    private static readonly LogEntry Loaded = LogEntries.At(0, LogSeverity.Information, "RecDiffy add-in loaded.");
    private static readonly LogEntry Rejected = LogEntries.At(1, LogSeverity.Warning, "Rejected broker.csv.");
    private static readonly LogEntry Crashed = LogEntries.At(2, LogSeverity.Error, "The reconciliation window: an unexpected error.");

    [Fact]
    public void The_window_starts_with_what_is_already_logged()
    {
        using var model = new LogViewModel(new FakeFeed(Loaded, Rejected), new FakeLogShell(), ui: null);

        Assert.Equal([Loaded, Rejected], model.Entries);
        Assert.Equal("2 entries · 1 warning · 0 errors", ResultSheetFixtures.InCulture("en-US", () => model.Summary));
    }

    [Fact]
    public void New_entries_appear_as_they_are_logged()
    {
        var feed = new FakeFeed(Loaded);
        using var model = new LogViewModel(feed, new FakeLogShell(), ui: null);

        feed.Raise(Crashed);

        Assert.Equal([Loaded, Crashed], model.Entries);
        Assert.Equal("2 entries · 0 warnings · 1 error", ResultSheetFixtures.InCulture("en-US", () => model.Summary));
    }

    [Fact]
    public void An_entry_already_in_the_snapshot_is_not_shown_twice()
    {
        var feed = new FakeFeed(Loaded, Rejected);
        using var model = new LogViewModel(feed, new FakeLogShell(), ui: null);

        feed.Raise(Rejected);

        Assert.Equal([Loaded, Rejected], model.Entries);
    }

    [Fact]
    public void Warnings_and_errors_only_hides_information()
    {
        var feed = new FakeFeed(Loaded, Rejected);
        using var model = new LogViewModel(feed, new FakeLogShell(), ui: null);

        model.ProblemsOnly = true;
        feed.Raise(LogEntries.At(3, LogSeverity.Information, "Run started."));
        feed.Raise(Crashed);

        Assert.Equal([Rejected, Crashed], model.Entries);

        model.ProblemsOnly = false;

        Assert.Equal(4, model.Entries.Count);
    }

    [Fact]
    public void Copy_copies_the_entries_shown_as_log_lines()
    {
        var shell = new FakeLogShell();
        using var model = new LogViewModel(new FakeFeed(Loaded, Rejected), shell, ui: null);
        model.ProblemsOnly = true;

        model.CopyCommand.Execute(null);

        Assert.Equal(LogText.Format(Rejected), Assert.Single(shell.Copied));
    }

    [Fact]
    public void Open_log_folder_asks_the_shell_and_the_file_status_comes_from_it()
    {
        var shell = new FakeLogShell();
        using var model = new LogViewModel(new FakeFeed(), shell, ui: null);

        model.OpenFolderCommand.Execute(null);

        Assert.Equal(1, shell.FolderOpened);
        Assert.Equal(shell.FileStatus, model.FileStatus);
    }

    [Fact]
    public void A_closed_window_stops_listening()
    {
        var feed = new FakeFeed();
        using (new LogViewModel(feed, new FakeLogShell(), ui: null))
        {
            Assert.True(feed.HasListeners);
        }

        Assert.False(feed.HasListeners);
    }

    [Fact]
    public void Entries_logged_on_another_thread_are_added_through_the_window_context()
    {
        var feed = new FakeFeed();
        var context = new QueueContext();
        using var model = new LogViewModel(feed, new FakeLogShell(), context);

        feed.Raise(Rejected);

        Assert.Empty(model.Entries);
        context.RunAll();
        Assert.Equal([Rejected], model.Entries);
    }

    [Fact]
    public void The_window_keeps_at_most_its_limit_of_entries()
    {
        var feed = new FakeFeed();
        using var model = new LogViewModel(feed, new FakeLogShell(), ui: null);

        foreach (var minute in Enumerable.Range(0, LogViewModel.MaxEntries + 5))
            feed.Raise(LogEntries.At(minute, LogSeverity.Information, "tick"));

        Assert.Equal(LogViewModel.MaxEntries, model.Entries.Count);
        Assert.Equal(StepClock.Now.AddMinutes(5), model.Entries[0].At);
    }

    private sealed class QueueContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _posted = new();

        public override void Post(SendOrPostCallback d, object? state) => _posted.Enqueue((d, state));

        public void RunAll()
        {
            while (_posted.TryDequeue(out var posted))
                posted.Callback(posted.State);
        }
    }
}
