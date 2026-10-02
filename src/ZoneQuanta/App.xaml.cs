using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace ZoneQuanta;

public partial class App : Application
{
    private const string MutexName = "ZoneQuanta.SingleInstance.7F3A";
    private const string ShowEventName = "ZoneQuanta.ShowPanel.7F3A";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _wait;
    private AppController? _controller;

    public static bool Exiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out bool first);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!first)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }

        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        bool autostart = Array.IndexOf(e.Args, "--autostart") >= 0;

        _controller = new AppController();
        _controller.Start(showPanel: !autostart);

        _wait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(() => _controller?.ShowPanel()), null, Timeout.Infinite, false);
    }

    public void ExitApp()
    {
        Exiting = true;
        _wait?.Unregister(null);
        _controller?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showEvent?.Dispose();
        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
