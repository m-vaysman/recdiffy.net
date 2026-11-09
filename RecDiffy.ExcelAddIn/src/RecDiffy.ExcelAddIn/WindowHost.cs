using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ExcelDna.Logging;

namespace RecDiffy.ExcelAddIn;

internal sealed class WindowHost(Func<Window> createWindow)
{
    private readonly Lock _gate = new();
    private Thread? _thread;
    private Dispatcher? _dispatcher;
    private Window? _window;

    public void ShowOrActivate(nint owner)
    {
        lock (_gate)
        {
            if (_thread is { IsAlive: true })
            {
                _ = _dispatcher?.BeginInvoke(ActivateWindow);
                return;
            }

            _thread = new Thread(() => Run(owner)) { IsBackground = true, Name = "RecDiffy window" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            _ = _dispatcher?.BeginInvoke(() => _window?.Close());
        }
    }

    private void Run(nint owner)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.UnhandledException += OnUnhandledException;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        try
        {
            var window = createWindow();
            new WindowInteropHelper(window).Owner = owner;
            window.Closed += (_, _) => dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            lock (_gate)
            {
                _dispatcher = dispatcher;
                _window = window;
            }

            window.Show();
            Dispatcher.Run();
        }
        catch (Exception exception)
        {
            LogDisplay.WriteLine("RecDiffy: the reconciliation window stopped: {0}", exception);
        }
        finally
        {
            lock (_gate)
            {
                _dispatcher = null;
                _window = null;
            }
        }
    }

    private void ActivateWindow()
    {
        if (_window is null)
            return;

        if (_window.WindowState is WindowState.Minimized)
            _window.WindowState = WindowState.Normal;

        _window.Activate();
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogDisplay.WriteLine("RecDiffy: an error in the reconciliation window: {0}", e.Exception);
        e.Handled = true;
    }
}
