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
    private const string RecoveredFlag = "--recovered";
    // A recovered instance that bursts again this soon ends instead of restarting in a loop.
    private const long RecoveryCalmMs = 10 * 60_000;
    private static readonly long StartedAt = Environment.TickCount64;

    private string[] _args = Array.Empty<string>();
    private bool _recovering;
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _wait;
    private AppController? _controller;

    private static readonly string[] Commands = { "--quick-add", "--quick-import", "--quick-clean", "--quick-scan", "--quick", "--path-bar", "--open", "--panel", "--exit" };

    public static bool Exiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // The elevated traffic helper has no window and ends with its work. It leaves through Environment.Exit, not
        // a WPF shutdown: after an update has replaced the executable that shutdown fails to load an assembly, and
        // a process left alive by it makes Task Scheduler ignore every later start of the helper.
        if (Array.IndexOf(e.Args, "--traffic-helper") >= 0)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, a) => { if (a.ExceptionObject is Exception ex) Log.Crash("helper", ex); };
            Task.Run(() =>
            {
                int code = 1;
                try { code = TrafficHelper.Run(); }
                catch (Exception ex) { Log.Error("traffic-helper", ex); }
                Environment.Exit(code);
            });
            return;
        }

        _args = e.Args;
        WatchUnhandled();
        try { DataPaths.Init(e.Args); }
        catch (ArgumentException ex)
        {
            Log.Write($"start refused: {ex.Message}");
            Quit(2);
            return;
        }

        bool autostart = Array.IndexOf(e.Args, "--autostart") >= 0;
        bool command = Array.Exists(e.Args, a => Array.IndexOf(Commands, a.ToLowerInvariant()) >= 0);
        bool restarted = Array.IndexOf(e.Args, "--restart") >= 0;
        bool skipInstall = autostart || command || restarted || DataPaths.Sandboxed || Array.IndexOf(e.Args, "--updated") >= 0
                           || Array.IndexOf(e.Args, "--installed") >= 0 || Array.IndexOf(e.Args, "--portable") >= 0;
        if (!skipInstall && !InInstallFolder())
        {
            RunInstaller();
            return;
        }

        bool updated = Array.IndexOf(e.Args, "--updated") >= 0;
        _mutex = new Mutex(true, MutexName + DataPaths.InstanceSuffix, out bool first);
        if (!first && (updated || restarted)) first = TakeOverFromPrevious(_mutex, PreviousPid(e.Args));
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName + DataPaths.InstanceSuffix);
        if (!first)
        {
            // --exit never falls back to showing the panel, even when the pipe could not be reached.
            if ((command && CommandPipe.Send(Absolutize(e.Args))) || Array.IndexOf(e.Args, "--exit") >= 0)
            {
                Quit();
                return;
            }
            _showEvent.Set();
            Quit();
            return;
        }
        if (Array.IndexOf(e.Args, "--exit") >= 0)
        {
            Quit(); // nothing running to stop
            return;
        }

        // Normal priority and no EcoQoS: a below-normal background process is starved exactly when the
        // machine is busy, which is when the clock and the CPU / network readout matter most.
        Native.OptOutOfPowerThrottling();
        _ = Task.Run(TaskbarLayout.Warmup);

        // Sign-out, restart or shut-down: save settings, window places and traffic totals before Windows ends the process.
        SessionEnding += (_, e) =>
        {
            Log.Write($"session ending ({e.ReasonSessionEnding}): saving");
            PrepareExit();
        };
        if (DataPaths.Sandboxed) Log.Write($"sandbox instance on {DataPaths.Root}");
        _controller = new AppController();
        // A recovery restart comes back quietly, as the user left it (tray, widget, hotkeys), without the panel.
        bool recovered = Array.IndexOf(e.Args, RecoveredFlag) >= 0;
        if (recovered) Log.Write("recovered after a burst of errors");
        _controller.Start(showPanel: !autostart && !command && !recovered, updated);
        if (command) _controller.Execute(e.Args);

        _wait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(() => _controller?.ShowPanel()), null, Timeout.Infinite, false);
    }

    private static bool WaitForPreviousInstance(Mutex mutex, int seconds)
    {
        try { return mutex.WaitOne(TimeSpan.FromSeconds(seconds)); }
        catch (AbandonedMutexException) { return true; }
    }

    // "--from <pid>": the instance that started this one (restart); 0 when absent (updates from older versions).
    private static int PreviousPid(string[] args)
    {
        int i = Array.IndexOf(args, "--from");
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int pid) ? pid : 0;
    }

    // Waits for the previous instance; if it hangs, ends only that one. Without its id (an update started by an
    // older version) every other copy is ended, except from a sandbox, which must never touch the user's instance.
    private static bool TakeOverFromPrevious(Mutex mutex, int previousPid)
    {
        if (WaitForPreviousInstance(mutex, 8)) return true;
        if (previousPid == 0 && DataPaths.Sandboxed) return WaitForPreviousInstance(mutex, 10);

        Log.Write($"previous instance did not exit, ending {(previousPid > 0 ? $"pid {previousPid}" : "other copies")}");
        try
        {
            foreach (var other in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName))
            {
                using (other)
                {
                    if (other.Id == Environment.ProcessId || (previousPid > 0 && other.Id != previousPid)) continue;
                    try { other.Kill(true); }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
            }
        }
        catch (InvalidOperationException) { }
        return WaitForPreviousInstance(mutex, 10);
    }

    // Anything that escapes an event handler is logged in full; the app keeps running unless failures repeat
    // in a burst. Then it saves and starts again once (the user sees it come back, not vanish); a recovered
    // instance that bursts again within ten minutes ends, so a fault on start-up cannot loop.
    private void WatchUnhandled()
    {
        var recent = new System.Collections.Generic.Queue<long>();
        DispatcherUnhandledException += (_, e) =>
        {
            Log.Crash("ui", e.Exception);
            if (Exiting)
            {
                // Everything was saved before closing began: carrying on would only leave a process without windows.
                e.Handled = true;
                Environment.Exit(0);
            }
            long now = Environment.TickCount64;
            recent.Enqueue(now);
            while (recent.Count > 0 && now - recent.Peek() > 60_000) recent.Dequeue();
            e.Handled = recent.Count <= 10;
            if (e.Handled) return;
            if (CanRecover(now))
            {
                _recovering = true;
                e.Handled = true;
                Log.Write($"crash burst ({recent.Count} errors in a minute): saving and restarting");
                Restart(flag: RecoveredFlag);
                return;
            }
            Log.Write("crash burst again soon after a recovery: exiting");
            try { _controller?.Dispose(); }
            catch (Exception ex) { Log.Error("crash-save", ex); }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Log.Crash("domain", ex); };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Crash("task", e.Exception);
            e.SetObserved();
        };
    }

    private bool CanRecover(long now) =>
        !_recovering && Environment.ProcessPath is not null
        && !(Array.IndexOf(_args, RecoveredFlag) >= 0 && now - StartedAt < RecoveryCalmMs);

    private void Quit(int code = 0)
    {
        Exiting = true;
        Shutdown(code);
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

    // Saves and stops everything, runs apply (e.g. writing a restored version) while no part of the app can
    // write the files any more, then starts a fresh instance that takes over once this one has exited.
    // flag goes to the new instance (--recovered after a burst of errors).
    public void Restart(Action? apply = null, string? flag = null)
    {
        string? exe = Environment.ProcessPath;
        if (exe is null) return;
        PrepareExit();
        DataPaths.WritesFrozen = true;
        try { apply?.Invoke(); }
        catch (Exception ex) { Log.Error("restart-apply", ex); }
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        psi.ArgumentList.Add("--restart");
        psi.ArgumentList.Add("--from");
        psi.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (flag is not null) psi.ArgumentList.Add(flag);
        if (DataPaths.Sandboxed)
        {
            psi.ArgumentList.Add("--data-dir");
            psi.ArgumentList.Add(DataPaths.Root);
        }
        try { Process.Start(psi); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Log.Error("restart", ex); }
        _ = Task.Run(async () =>
        {
            await Task.Delay(2500);
            Environment.Exit(0);
        });
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

    private static string[] Absolutize(string[] args)
    {
        var result = (string[])args.Clone();
        for (int i = 0; i < result.Length; i++)
        {
            string a = result[i].Trim('"');
            if (a.StartsWith("--", StringComparison.Ordinal) || a.Length == 0 || Path.IsPathFullyQualified(a)
                || Core.Quick.PathEngine.IsShell(a) || Core.Quick.PathEngine.IsUrl(a)) continue;
            string full = Path.GetFullPath(a);
            if (File.Exists(full) || Directory.Exists(full)) result[i] = full;
        }
        return result;
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
        Quit();
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
