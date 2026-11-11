using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn;

internal sealed class LogShell(LogFileListener file, IAddInLog log, TimeProvider clock) : ILogShell
{
    public string FileStatus =>
        file.Failure
        ?? $"Also saved to {LogText.FileName(file.PathFor(clock.GetLocalNow()))} in %LOCALAPPDATA%\\RecDiffy\\Logs. Log files are kept for 14 days.";

    public void Copy(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (COMException exception)
        {
            log.Write(LogSeverity.Warning, LogAreas.LogWindow, $"The log couldn't be copied: {LogText.Failure(exception)}");
        }
    }

    public void OpenFolder()
    {
        try
        {
            _ = Directory.CreateDirectory(file.Folder);
            using var explorer = Process.Start(new ProcessStartInfo { FileName = file.Folder, UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            log.Write(LogSeverity.Warning, LogAreas.LogWindow, $"The log folder couldn't be opened: {LogText.Failure(exception, file.Folder)}");
        }
    }
}
