using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZoneQuanta.Core.Monitor;

namespace ZoneQuanta.Core.Traffic;

public enum HelperState { Started, Missing, Failed }

public static class HelperLauncher
{
    private const string TaskName = "ZoneQuantaTraffic";

    private static string StopFile => Path.Combine(TrafficFile.DefaultDir, TrafficHelper.StopFileName);

    public static async Task<bool> EnableAsync()
    {
        Log.Write("enable tracking");
        string exe = Environment.ProcessPath ?? string.Empty;
        if (exe.Length == 0) return false;
        ClearStop();

        string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
            "<Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>" +
            "<Principals><Principal id=\"A\"><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
            "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
            "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><AllowStartOnDemand>true</AllowStartOnDemand></Settings>" +
            $"<Actions Context=\"A\"><Exec><Command>{System.Security.SecurityElement.Escape(exe)}</Command><Arguments>--traffic-helper</Arguments></Exec></Actions></Task>";
        Directory.CreateDirectory(TrafficFile.DefaultDir);
        string xmlPath = Path.Combine(TrafficFile.DefaultDir, "traffic-task.xml");
        File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode);

        bool ok = await RunElevatedAsync("schtasks.exe", $"/Create /TN {TaskName} /XML \"{xmlPath}\" /F");
        File.Delete(xmlPath);
        if (!ok) return false;
        return await RunHiddenAsync("schtasks.exe", $"/Run /TN {TaskName}");
    }

    public static async Task DisableAsync()
    {
        Directory.CreateDirectory(TrafficFile.DefaultDir);
        File.WriteAllText(StopFile, DateTime.Now.ToString("O"));
        await RunElevatedAsync("schtasks.exe", $"/Delete /TN {TaskName} /F");
    }

    public static async Task<HelperState> StartIfInstalledAsync()
    {
        await Task.Delay(4000);
        ClearStop();
        // /Query fails only when the task does not exist, which tells "removed" apart from "could not start".
        if (!await RunHiddenAsync("schtasks.exe", $"/Query /TN {TaskName}")) return HelperState.Missing;
        // Task Scheduler ignores a start while a previous instance is still exiting (for instance while
        // Windows records its crash), so confirm the helper came up and ask again a few times.
        for (int attempt = 1; ; attempt++)
        {
            if (!await RunHiddenAsync("schtasks.exe", $"/Run /TN {TaskName}")) return HelperState.Failed;
            for (int i = 0; i < 12 && !IsRunning(); i++) await Task.Delay(250);
            bool up = IsRunning();
            if (up || attempt == 6)
            {
                if (attempt > 1) Log.Write($"helper start attempt {attempt} -> {(up ? "running" : "not running")}");
                return HelperState.Started;
            }
            await Task.Delay(2000);
        }
    }

    // After an update the helper still runs from the previous image. Removing that image kills it
    // mid-write and Task Scheduler ignores a new start while it lives, so stop it cleanly first.
    public static async Task StopRunningAsync()
    {
        if (!IsRunning()) return;
        RequestStop();
        for (int i = 0; i < 40 && IsRunning(); i++) await Task.Delay(250);
        Log.Write($"helper stop for update -> {(IsRunning() ? "still running" : "stopped")}");
    }

    private static bool IsRunning()
    {
        try
        {
            using var m = Mutex.OpenExisting(TrafficHelper.MutexName);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return true; } // owned by the elevated helper
    }

    public static void RequestStop()
    {
        try
        {
            Directory.CreateDirectory(TrafficFile.DefaultDir);
            File.WriteAllText(StopFile, DateTime.Now.ToString("O"));
        }
        catch (IOException) { }
    }

    private static void ClearStop()
    {
        try { if (File.Exists(StopFile)) File.Delete(StopFile); }
        catch (IOException) { }
    }

    private static async Task<bool> RunElevatedAsync(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args) { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            if (p is null) return false;
            await p.WaitForExitAsync();
            Log.Write($"elevated {file} {args} -> {p.ExitCode}");
            return p.ExitCode == 0;
        }
        catch (Win32Exception ex)
        {
            Log.Error("elevate", ex);
            return false;
        }
    }

    private static async Task<bool> RunHiddenAsync(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args) { CreateNoWindow = true, UseShellExecute = false });
            if (p is null) return false;
            await p.WaitForExitAsync();
            Log.Write($"run {file} {args} -> {p.ExitCode}");
            return p.ExitCode == 0;
        }
        catch (Win32Exception ex)
        {
            Log.Error("schtasks", ex);
            return false;
        }
    }
}
