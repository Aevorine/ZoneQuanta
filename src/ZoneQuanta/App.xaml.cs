using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ZoneQuanta.Core;
using ZoneQuanta.Core.Traffic;
using ZoneQuanta.Platform;
using ZoneQuanta.UI.Setup;
using ZoneQuanta.UI.Theme;

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

        if (Array.IndexOf(e.Args, "--traffic-helper") >= 0)
        {
            Task.Run(() =>
            {
                try { TrafficHelper.Run(); }
                catch (Exception ex) { Log.Error("traffic-helper", ex); }
                Dispatcher.BeginInvoke(new Action(Shutdown));
            });
            return;
        }

        bool autostart = Array.IndexOf(e.Args, "--autostart") >= 0;
        bool skipInstall = autostart || Array.IndexOf(e.Args, "--updated") >= 0
                           || Array.IndexOf(e.Args, "--installed") >= 0 || Array.IndexOf(e.Args, "--portable") >= 0;
        if (!skipInstall && !InInstallFolder())
        {
            RunInstaller();
            return;
        }

        bool updated = Array.IndexOf(e.Args, "--updated") >= 0;
        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first && updated) first = TakeOverFromPrevious(_mutex);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!first)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }

        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;

        _controller = new AppController();
        _controller.Start(showPanel: !autostart);

        _wait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(() => _controller?.ShowPanel()), null, Timeout.Infinite, false);
    }

    private static bool WaitForPreviousInstance(Mutex mutex, int seconds)
    {
        try { return mutex.WaitOne(TimeSpan.FromSeconds(seconds)); }
        catch (AbandonedMutexException) { return true; }
    }

    private static bool TakeOverFromPrevious(Mutex mutex)
    {
        if (WaitForPreviousInstance(mutex, 8)) return true;

        Log.Write("previous instance did not exit, ending it");
        try
        {
            foreach (var other in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName))
            {
                using (other)
                {
                    if (other.Id == Environment.ProcessId) continue;
                    try { other.Kill(true); }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
            }
        }
        catch (InvalidOperationException) { }
        return WaitForPreviousInstance(mutex, 10);
    }

    public void PrepareExit()
    {
        Exiting = true;
        try { _wait?.Unregister(null); }
        catch (Exception ex) { Log.Error("exit-wait", ex); }
        try { _controller?.Dispose(); }
        catch (Exception ex) { Log.Error("exit-dispose", ex); }
        _controller = null;
    }

    public void ExitApp()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(2500);
            Environment.Exit(0);
        });
        PrepareExit();
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

    private static bool InInstallFolder()
    {
        string? dir = Path.GetDirectoryName(Environment.ProcessPath);
        return string.Equals(Path.GetFileName(dir), InstallWindow.FolderName, StringComparison.OrdinalIgnoreCase);
    }

    private void RunInstaller()
    {
        ThemeManager.Apply("Graphite");
        var window = new InstallWindow();
        if (window.ShowDialog() == true && window.InstalledExe is { } exe)
        {
            if (window.EnableAutostart) AutoStart.Set(true, exe);
            if (window.CreateDesktopShortcut) CreateShortcut(exe);
            Process.Start(new ProcessStartInfo(exe, "--installed") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
        }
        Shutdown();
    }

    private static void CreateShortcut(string exe)
    {
        try
        {
            Type? shell = Type.GetTypeFromProgID("WScript.Shell");
            if (shell is null) return;
            dynamic sh = Activator.CreateInstance(shell)!;
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            dynamic link = sh.CreateShortcut(Path.Combine(desktop, "ZoneQuanta.lnk"));
            link.TargetPath = exe;
            link.WorkingDirectory = Path.GetDirectoryName(exe);
            link.Save();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
        {
            Log.Error("shortcut", ex);
        }
    }
}
