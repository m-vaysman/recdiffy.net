using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using ExcelDna.Integration;
using RecDiffy.ExcelAddIn.ViewModels;
using RecDiffy.ExcelAddIn.Views;

namespace RecDiffy.ExcelAddIn;

internal static class AddInLogs
{
    public static TimeProvider Clock { get; } = TimeProvider.System;

    public static LogBook Book { get; } = new(LogViewModel.MaxEntries);

    public static LogFileListener File { get; } = new(LogFileListener.ForWindowsUser(), Environment.ProcessId, Clock, FileFailed);

    private static readonly TraceSource Source = TraceAddInLog.CreateSource([new LogBookListener(Book, Clock), File]);

    public static IAddInLog Log { get; } = new TraceAddInLog(Source, Clock);

    public static WindowHost Window { get; } = new(LoggedWindows.Wrap(CreateWindow, Log, LogAreas.LogWindow, "The log window"));

    public static void Start()
    {
        Book.Added += ShowOnError;
        var deleted = File.DeleteOlderThan(LogFileListener.KeptFor);
        Log.Write(
            LogSeverity.Information,
            LogAreas.AddIn,
            $"RecDiffy add-in {Version()} loaded in Excel {ExcelVersion()}, {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}, on {RuntimeInformation.FrameworkDescription}.");
        if (deleted > 0)
            Log.Write(LogSeverity.Information, LogAreas.AddIn, $"Deleted {LogText.Count(deleted, "log file", "log files")} older than 14 days.");
    }

    public static void Stop()
    {
        Book.Added -= ShowOnError;
        Log.Write(LogSeverity.Information, LogAreas.AddIn, "RecDiffy add-in closed.");
        Window.Close();
        Source.Flush();
    }

    public static void ShowWindow() =>
        Window.ShowOrActivate(
            XlCall.TryExcel(XlCall.xlGetHwnd, out var handle) switch
            {
                XlCall.XlReturn.XlReturnSuccess => ExcelWindows.Resolve(handle),
                _ => 0,
            });

    private static LogWindow CreateWindow()
    {
        var model = new LogViewModel(Book, new LogShell(File, Log, Clock), SynchronizationContext.Current);
        var window = new LogWindow { DataContext = model };
        window.Closed += (_, _) => model.Dispose();
        return window;
    }

    private static void ShowOnError(object? sender, LogEntryEventArgs e)
    {
        if (e.Entry is { Severity: LogSeverity.Error, Area: not LogAreas.LogWindow })
            ExcelAsyncUtil.QueueAsMacro(ShowWindow);
    }

    private static void FileFailed(string message) =>
        Book.Append(new LogEntry(Clock.GetLocalNow(), LogSeverity.Warning, LogAreas.AddIn, message));

    private static string Version() =>
        typeof(AddInLogs).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "(unknown version)";

    private static string ExcelVersion() =>
        XlCall.TryExcel(XlCall.xlfGetWorkspace, out var version, 2) switch
        {
            XlCall.XlReturn.XlReturnSuccess when version is string text => text,
            _ => "(unknown version)",
        };
}
