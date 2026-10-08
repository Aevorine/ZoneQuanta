using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
        return await RunHiddenAsync("schtasks.exe", $"/Run /TN {TaskName}") ? HelperState.Started : HelperState.Failed;
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
