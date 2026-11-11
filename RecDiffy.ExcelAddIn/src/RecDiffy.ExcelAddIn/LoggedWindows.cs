using System.Windows;
using System.Windows.Threading;
using RecDiffy.ExcelAddIn.ViewModels;

namespace RecDiffy.ExcelAddIn;

internal static class LoggedWindows
{
    public static Func<Window> Wrap(Func<Window> create, IAddInLog log, string area, string name)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(log);
        return () =>
        {
            Dispatcher.CurrentDispatcher.UnhandledException += (_, e) =>
                log.Write(LogSeverity.Error, area, $"{name}: an unexpected error. {LogText.Sanitize(e.Exception.ToString())}");
            try
            {
                var window = create();
                window.Closed += (_, _) => log.Write(LogSeverity.Information, area, $"{name} closed.");
                log.Write(LogSeverity.Information, area, $"{name} opened.");
                return window;
            }
            catch (Exception exception)
            {
                log.Write(LogSeverity.Error, area, $"{name} couldn't open. {LogText.Sanitize(exception.ToString())}");
                throw;
            }
        };
    }
}
