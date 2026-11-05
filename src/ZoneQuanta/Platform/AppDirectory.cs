using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ZoneQuanta.Core;

namespace ZoneQuanta.Platform;

// Process name -> executable path and description, so traffic rows can show icon and full name.
// Looked up while the app runs and remembered in apps-meta.json for when it has exited.
internal static class AppDirectory
{
    public sealed record Entry(string Path, string Description);

    private static readonly object Gate = new();
    private static readonly object SaveGate = new();
    private static readonly string FilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZoneQuanta", "apps-meta.json");
    private static readonly Dictionary<string, DateTime> Tried = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, Entry>? _cache;

    public static Entry? Get(string name)
    {
        lock (Gate) return Cache().TryGetValue(name, out var e) ? e : null;
    }

    // Background thread. Returns true when at least one new name was resolved.
    public static bool Resolve(IEnumerable<string> names)
    {
        var todo = new List<string>();
        lock (Gate)
        {
            var cache = Cache();
            var now = DateTime.UtcNow;
            foreach (string n in names)
            {
                if (cache.ContainsKey(n) || n is "System" or "已退出的进程") continue;
                if (Tried.TryGetValue(n, out var at) && (now - at).TotalSeconds < 60) continue;
                Tried[n] = now;
                todo.Add(n);
            }
        }
        bool found = false;
        foreach (string n in todo)
        {
            if (PathOf(n) is not { } path) continue;
            string desc = "";
            try { desc = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim() ?? ""; }
            catch (FileNotFoundException) { }
            lock (Gate) Cache()[n] = new Entry(path, desc);
            found = true;
        }
        if (found) Save();
        return found;
    }

    private static Dictionary<string, Entry> Cache()
    {
        if (_cache is not null) return _cache;
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(FilePath)) is { } d)
                return _cache = new Dictionary<string, Entry>(d, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException) { Log.Error("apps-meta", ex); }
        return _cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    }

    private static void Save()
    {
        try
        {
            lock (SaveGate)
            {
                string json;
                lock (Gate) json = JsonSerializer.Serialize(_cache);
                File.WriteAllText(FilePath + ".tmp", json);
                File.Move(FilePath + ".tmp", FilePath, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error("apps-meta-save", ex); }
    }

    // PROCESS_QUERY_LIMITED_INFORMATION also works for elevated processes, unlike Process.MainModule.
    private static string? PathOf(string processName)
    {
        Process[] procs;
        try { procs = Process.GetProcessesByName(processName); }
        catch (InvalidOperationException) { return null; }
        try
        {
            foreach (var p in procs)
            {
                IntPtr h = OpenProcess(0x1000, false, p.Id);
                if (h == IntPtr.Zero) continue;
                try
                {
                    var sb = new StringBuilder(1024);
                    int len = sb.Capacity;
                    if (QueryFullProcessImageName(h, 0, sb, ref len)) return sb.ToString();
                }
                finally { CloseHandle(h); }
            }
        }
        finally
        {
            foreach (var p in procs) p.Dispose();
        }
        return null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
