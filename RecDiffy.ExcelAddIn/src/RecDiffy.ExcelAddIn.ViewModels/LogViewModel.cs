using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RecDiffy.ExcelAddIn.ViewModels;

public sealed partial class LogViewModel : ObservableObject, IDisposable
{
    public const int MaxEntries = 2_000;

    private readonly ILogFeed _feed;
    private readonly ILogShell _shell;
    private readonly SynchronizationContext? _ui;
    private readonly List<LogEntry> _all;
    private readonly HashSet<LogEntry> _alreadyShown;
    private bool _disposed;

    [ObservableProperty]
    private bool problemsOnly;

    public LogViewModel(ILogFeed feed, ILogShell shell, SynchronizationContext? ui)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _ui = ui;
        _feed.Added += OnAdded;
        _all = [.. feed.Snapshot().TakeLast(MaxEntries)];
        _alreadyShown = new HashSet<LogEntry>(_all, ReferenceEqualityComparer.Instance);
        Entries = [.. _all.Where(Shows)];
    }

    public ObservableCollection<LogEntry> Entries { get; }

    public string Summary
    {
        get
        {
            var warnings = _all.Count(entry => entry.Severity is LogSeverity.Warning);
            var errors = _all.Count(entry => entry.Severity is LogSeverity.Error);
            return string.Create(
                CultureInfo.CurrentCulture,
                $"{Plural(_all.Count, "entry", "entries")} · {Plural(warnings, "warning", "warnings")} · {Plural(errors, "error", "errors")}");
        }
    }

    public string FileStatus => _shell.FileStatus;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _feed.Added -= OnAdded;
    }

    public string CopyText() => string.Join(Environment.NewLine, Entries.Select(LogText.Format));

    [RelayCommand]
    private void Copy() => _shell.Copy(CopyText());

    [RelayCommand]
    private void OpenFolder() => _shell.OpenFolder();

    partial void OnProblemsOnlyChanged(bool value)
    {
        Entries.Clear();
        foreach (var entry in _all.Where(Shows))
            Entries.Add(entry);
    }

    private bool Shows(LogEntry entry) => !ProblemsOnly || entry.Severity is not LogSeverity.Information;

    private void OnAdded(object? sender, LogEntryEventArgs e)
    {
        if (_ui is null)
            Add(e.Entry);
        else
            _ui.Post(_ => Add(e.Entry), null);
    }

    private void Add(LogEntry entry)
    {
        if (_disposed || _alreadyShown.Remove(entry))
            return;

        _all.Add(entry);
        if (_all.Count > MaxEntries)
        {
            var oldest = _all[0];
            _all.RemoveAt(0);
            _ = Entries.Remove(oldest);
        }

        if (Shows(entry))
            Entries.Add(entry);

        OnPropertyChanged(nameof(Summary));
    }

    private static string Plural(int count, string one, string many) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? one : many)}");
}
