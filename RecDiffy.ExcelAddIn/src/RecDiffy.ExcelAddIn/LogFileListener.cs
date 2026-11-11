using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn;

internal sealed class LogFileListener : TraceListener
{
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(14);

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;
    private readonly Action<string> _failed;
    private string? _failure;

    public LogFileListener(string folder, int processId, TimeProvider clock, Action<string> failed)
    {
        Folder = folder ?? throw new ArgumentNullException(nameof(folder));
        ProcessId = processId;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _failed = failed ?? throw new ArgumentNullException(nameof(failed));
    }

    public string Folder { get; }

    public int ProcessId { get; }

    public string? Failure
    {
        get
        {
            lock (_gate)
            {
                return _failure;
            }
        }
    }

    public override bool IsThreadSafe => true;

    public static string ForWindowsUser() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecDiffy", "Logs");

    public string PathFor(DateTimeOffset at) =>
        Path.Combine(Folder, string.Create(CultureInfo.InvariantCulture, $"RecDiffy-{at:yyyy-MM-dd}-{ProcessId}.log"));

    public override void TraceData(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, object? data)
    {
        if (data is LogEntry entry)
            Append(entry);
        else
            WriteLine(data?.ToString());
    }

    public override void Write(string? message) => WriteLine(message);

    public override void WriteLine(string? message)
    {
        if (message is { Length: > 0 })
            Append(new LogEntry(_clock.GetLocalNow(), LogSeverity.Information, LogAreas.AddIn, message));
    }

    public void Append(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string? failure;
        lock (_gate)
        {
            if (_failure is not null)
                return;

            failure = TryAppend(entry);
            _failure = failure;
        }

        if (failure is not null)
            _failed(failure);
    }

    public int DeleteOlderThan(TimeSpan age)
    {
        var cutoff = (_clock.GetUtcNow() - age).UtcDateTime;
        try
        {
            if (!Directory.Exists(Folder))
                return 0;

            var old = new DirectoryInfo(Folder).EnumerateFiles("RecDiffy-*.log").Where(file => file.LastWriteTimeUtc < cutoff).ToList();
            var deleted = 0;
            foreach (var file in old)
            {
                if (TryDelete(file))
                    deleted++;
            }

            return deleted;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private string? TryAppend(LogEntry entry)
    {
        try
        {
            _ = Directory.CreateDirectory(Folder);
            File.AppendAllText(PathFor(entry.At), LogText.Format(entry) + Environment.NewLine, Utf8);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return $"The log file can't be written, so this session is logged only in the log window: {LogText.Failure(exception, Folder)}";
        }
    }

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
