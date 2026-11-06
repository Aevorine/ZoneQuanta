using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ZoneQuanta.Platform;

// Process id -> program file, for the traffic helper (elevated, so it can query nearly every process).
public static partial class ProcessImages
{
    private static Dictionary<string, string>? _devices;
    private static readonly object Gate = new();

    // Stable app name from a file name: "claude.exe.old.1790992407001" and "claude.exe" both give "claude".
    public static string NameOf(string fileOrPath)
    {
        string n = Path.GetFileName(fileOrPath.TrimEnd('\\'));
        var m = Renamed().Match(n);
        if (m.Success) n = m.Groups[1].Value;
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        return n.Length > 0 ? n : fileOrPath;
    }

    [GeneratedRegex(@"^(.+?)(?:\.exe)?\.(?:old|bak|tmp|new|delete)(?:\.\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Renamed();

    // "\Device\HarddiskVolume3\Windows\notepad.exe" -> "C:\Windows\notepad.exe".
    public static string ToDosPath(string ntPath)
    {
        if (ntPath.Length > 1 && ntPath[1] == ':') return ntPath;
        if (ntPath.StartsWith(@"\??\", StringComparison.Ordinal)) return ntPath[4..];
        for (int attempt = 0; attempt < 2; attempt++)
        {
            foreach (var (device, drive) in Devices(refresh: attempt == 1))
                if (ntPath.StartsWith(device + "\\", StringComparison.OrdinalIgnoreCase)) return drive + ntPath[device.Length..];
        }
        return ntPath;
    }

    private static Dictionary<string, string> Devices(bool refresh)
    {
        lock (Gate)
        {
            if (_devices is not null && !refresh) return _devices;
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var buf = new StringBuilder(1024);
            foreach (string root in Environment.GetLogicalDrives())
            {
                string drive = root.TrimEnd('\\');
                if (QueryDosDevice(drive, buf, buf.Capacity) > 0) d[buf.ToString()] = drive;
            }
            return _devices = d;
        }
    }

    // Full path and, for a packaged (Store) app, its AppUserModelID; null when the process is gone or protected.
    public static (string Path, string Aumid)? Query(int pid)
    {
        IntPtr h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int len = sb.Capacity;
            if (!QueryFullProcessImageName(h, 0, sb, ref len)) return null;
            string aumid = "";
            var id = new StringBuilder(256);
            int idLen = id.Capacity;
            if (GetApplicationUserModelId(h, ref idLen, id) == 0) aumid = id.ToString();
            return (sb.ToString(), aumid);
        }
        finally { CloseHandle(h); }
    }

    // "Name_1.2.3.4_x64__8wekyb3d8bbwe" + "App" -> "Name_8wekyb3d8bbwe!App".
    public static string AumidOf(string packageFullName, string relativeAppId)
    {
        if (packageFullName.Length == 0 || relativeAppId.Length == 0) return "";
        string[] p = packageFullName.Split('_');
        return p.Length >= 5 ? $"{p[0]}_{p[^1]}!{relativeAppId}" : "";
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int QueryDosDevice(string device, StringBuilder target, int max);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(IntPtr process, ref int length, StringBuilder id);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
