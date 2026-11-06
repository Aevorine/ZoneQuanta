using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;
using ZoneQuanta.Core;
using ZoneQuanta.Core.Traffic;

namespace ZoneQuanta.Platform;

// Process name -> program file, display name, owning application and icon, for the traffic list.
// The path comes from, in order: what the traffic helper saw (apps-paths.tsv), a running process,
// the App Paths and service registrations, the Start menu, and PATH. Remembered in apps-meta.json.
internal static class AppDirectory
{
    // Bumped when the way an entry is worked out changes: older entries are worked out again.
    private const int Revision = 2;

    public sealed record Entry
    {
        public string Path { get; init; } = "";
        public string Description { get; init; } = "";
        public string Aumid { get; init; } = "";
        public string Icon { get; init; } = "";      // shell parsing name, a glyph: token, or "" for a monogram
        public string Friendly { get; init; } = "";  // the name people know the program by
        public string Owner { get; init; } = "";     // application a helper belongs to
        public string OwnerKey { get; init; } = "";  // the same for every process of one application
        public int Rev { get; init; }
    }

    private static readonly object Gate = new();
    private static readonly object SaveGate = new();
    private static string FilePath => DataPaths.File("apps-meta.json");
    private static readonly Dictionary<string, long> Tried = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, Entry>? _cache;
    private static Dictionary<string, (string Path, string Aumid)> _helper = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _helperStamp;
    private static Dictionary<string, string>? _services;

    public static Entry? Get(string name)
    {
        lock (Gate) return Cache().TryGetValue(name, out var e) && e.Rev == Revision ? e : null;
    }

    // Background thread. True when something new was worked out.
    public static bool Resolve(IEnumerable<string> names)
    {
        var todo = new List<string>();
        bool helperChanged = RefreshHelperPaths();
        lock (Gate)
        {
            var cache = Cache();
            long now = Environment.TickCount64;
            foreach (string n in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                bool done = cache.TryGetValue(n, out var e) && e.Rev == Revision && (e.Path.Length > 0 || !_helper.ContainsKey(n));
                if (done && !(helperChanged && _helper.TryGetValue(n, out var h) && h.Path != e!.Path)) continue;
                if (!helperChanged && Tried.TryGetValue(n, out long at) && now - at < 60_000) continue;
                Tried[n] = now;
                todo.Add(n);
            }
        }
        if (todo.Count == 0) return false;
        var sw = Stopwatch.StartNew();
        bool found = false;
        foreach (string n in todo)
        {
            var e = Work(n);
            if (e is null) continue;
            lock (Gate) Cache()[n] = e;
            found = true;
        }
        if (found) Save();
        Log.Write($"app-identity {todo.Count} name(s) {sw.ElapsedMilliseconds}ms");
        return found;
    }

    private static Entry? Work(string name)
    {
        if (name == "System") return new Entry { Icon = AppIdentity.WindowsGlyph, Friendly = "System", OwnerKey = "System", Rev = Revision };
        if (name == TrafficHelper.ExitedName) return new Entry { Icon = AppIdentity.ExitedGlyph, Friendly = name, OwnerKey = name, Rev = Revision };

        string path = "", aumid = "";
        lock (Gate)
        {
            if (_helper.TryGetValue(name, out var h)) (path, aumid) = h;
            else if (Cache().TryGetValue(name, out var old) && File.Exists(old.Path)) (path, aumid) = (old.Path, old.Aumid);
        }
        if (path.Length == 0 && Running(name) is { } r) (path, aumid) = r;
        if (path.Length == 0) path = Registered(name) ?? StartMenuIndex.Current.PathOf(name) ?? OnPath(name) ?? "";
        if (path.Length == 0) return null; // tried again once the helper or a running process knows it

        string desc = "";
        try { if (File.Exists(path)) desc = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim() ?? ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var id = AppIdentity.Describe(path, aumid, name, desc);
        return new Entry
        {
            Path = path, Description = desc, Aumid = aumid, Icon = id.Icon, Friendly = id.Friendly,
            Owner = id.Owner, OwnerKey = id.OwnerKey, Rev = Revision,
        };
    }

    private static bool RefreshHelperPaths()
    {
        string file = System.IO.Path.Combine(DataPaths.HelperDir, TrafficHelper.PathsFileName);
        DateTime stamp;
        try { stamp = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        lock (Gate) if (stamp == _helperStamp) return false;
        var map = TrafficHelper.LoadPaths(file);
        lock (Gate)
        {
            _helper = map;
            _helperStamp = stamp;
        }
        return true;
    }

    private static (string Path, string Aumid)? Running(string name)
    {
        Process[] procs;
        try { procs = Process.GetProcessesByName(name); }
        catch (InvalidOperationException) { return null; }
        try
        {
            foreach (var p in procs)
                if (ProcessImages.Query(p.Id) is { } q) return q;
        }
        finally
        {
            foreach (var p in procs) p.Dispose();
        }
        return null;
    }

    // App Paths (programs that register how to be started) and Windows services.
    private static string? Registered(string name)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var k = hive.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{name}.exe");
                if (k?.GetValue(null) is string v && Clean(v) is { } p && File.Exists(p)) return p;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return Services().GetValueOrDefault(name);
    }

    private static Dictionary<string, string> Services()
    {
        lock (Gate) if (_services is not null) return _services;
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            foreach (string svc in root?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                try
                {
                    using var k = root!.OpenSubKey(svc);
                    if (k?.GetValue("ImagePath") is string v && Clean(v) is { } p && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                        d.TryAdd(ProcessImages.NameOf(p), p);
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        lock (Gate) return _services = d;
    }

    // "\"C:\x\y.exe\" -k" / "%SystemRoot%\y.exe /a" / "\??\C:\y.exe" -> the program file.
    private static string? Clean(string raw)
    {
        string v = Environment.ExpandEnvironmentVariables(raw.Trim());
        if (v.StartsWith(@"\??\", StringComparison.Ordinal)) v = v[4..];
        if (v.StartsWith('"'))
        {
            int end = v.IndexOf('"', 1);
            return end > 1 ? v[1..end] : null;
        }
        int exe = v.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? v[..(exe + 4)] : null;
    }

    private static string? OnPath(string name)
    {
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string p = System.IO.Path.Combine(dir.Trim(), name + ".exe");
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException) { }
        }
        return null;
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
}
