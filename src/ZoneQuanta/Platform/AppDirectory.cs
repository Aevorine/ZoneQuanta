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
    private const int Revision = 4;

    public sealed record Entry
    {
        public string Path { get; init; } = "";
        public string Description { get; init; } = "";
        public string Aumid { get; init; } = "";
        public string Icon { get; init; } = "";      // shell parsing name, a glyph: token, or "" for a monogram
        public string Friendly { get; init; } = "";  // the name people know the program by
        public string Owner { get; init; } = "";     // application a helper belongs to
        public string OwnerKey { get; init; } = "";  // the same for every process of one application
        public string Web { get; init; } = "";       // brand icon still to fetch (AppBrands.Web.Spec)
        public int Rev { get; init; }
    }

    private static readonly object Gate = new();
    private static readonly object SaveGate = new();
    private static string FilePath => DataPaths.File("apps-meta.json");
    // Names asked for and when; a name that stays unknown waits twice as long each time (1 min .. 30 min).
    private static readonly Dictionary<string, (long At, int Misses)> Tried = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, Entry>? _cache;
    private static Dictionary<string, (string Path, string Aumid)> _helper = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _helperStamp;
    private static Dictionary<string, string>? _services;

    public static Entry? Get(string name)
    {
        lock (Gate) return Cache().TryGetValue(name, out var e) && e.Rev == Revision ? e : null;
    }

    // Background thread. True when something new was worked out.
    // Two passes: everything the quick sources know is published at once (progress), then one search of the
    // install folders for what is left. True when the second pass found something too.
    public static bool Resolve(IEnumerable<string> names, Action? progress = null)
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
                var t = Tried.GetValueOrDefault(n);
                if (!helperChanged && t.At != 0 && now - t.At < Math.Min(60_000L << Math.Min(t.Misses, 5), 1_800_000)) continue;
                Tried[n] = (now, t.Misses + (t.At != 0 ? 1 : 0));
                todo.Add(n);
            }
        }
        var sw = Stopwatch.StartNew();
        bool found = false;
        var via = new Dictionary<string, int>();
        var missing = new List<string>();
        foreach (string n in todo)
        {
            var e = Work(n, out string source);
            if (e is null)
            {
                missing.Add(n);
                continue;
            }
            via[source] = via.GetValueOrDefault(source) + 1;
            lock (Gate) Cache()[n] = e;
            found = true;
        }
        if (found)
        {
            Save();
            progress?.Invoke();
        }
        long quick = sw.ElapsedMilliseconds;
        bool late = false;
        if (missing.Count > 0)
        {
            foreach (var (n, path) in ExecutableIndex.Search(missing, StartMenuIndex.Current.AppFolders))
            {
                if (Work(n, out _, path) is not { } e) continue;
                via["scan"] = via.GetValueOrDefault("scan") + 1;
                lock (Gate) Cache()[n] = e;
                missing.Remove(n);
                late = true;
            }
            // Whose file is gone: an older copy's name, an app or product of the same name, or the brand.
            foreach (string n in missing.ToList())
            {
                if (Fallback(n, out string how) is not { } e) continue;
                via[how] = via.GetValueOrDefault(how) + 1;
                lock (Gate) Cache()[n] = e;
                missing.Remove(n);
                late = true;
            }
            if (late) Save();
        }
        long searched = sw.ElapsedMilliseconds;
        int web = FetchBrandIcons(names);
        if (web > 0) late = true;
        // Which source answered how often, and what is still unknown: enough to improve resolution from the log alone.
        if (todo.Count > 0 || web > 0)
            Log.Write($"app-identity {todo.Count} name(s) {quick}ms (+search {searched - quick}ms, +web {web} {sw.ElapsedMilliseconds - searched}ms) via {string.Join(", ", via.Select(kv => $"{kv.Key} {kv.Value}"))}"
                      + (missing.Count > 0 ? $"; unknown: {string.Join(", ", missing.Take(20))}" : ""));
        return late || (found && progress is null);
    }

    // Brand icons for the programs still waiting for one: those fetched meanwhile are used at once, the rest
    // fetched (when allowed). Number of programs that got one.
    private static int FetchBrandIcons(IEnumerable<string> names)
    {
        var pending = new List<(string Name, AppBrands.Web Web)>();
        lock (Gate)
            foreach (string n in names.Distinct(StringComparer.OrdinalIgnoreCase))
                if (Cache().TryGetValue(n, out var e) && e.Rev == Revision && e.Web.Length > 0 && AppBrands.Web.Parse(e.Web) is { } w)
                    pending.Add((n, w));
        if (pending.Count == 0) return 0;
        if (WebIcons.Enabled) WebIcons.Fetch(pending.Select(p => p.Web));
        int got = 0;
        foreach (var (n, w) in pending)
        {
            if (WebIcons.Cached(w) is not { } file) continue;
            lock (Gate)
                if (Cache().TryGetValue(n, out var e) && e.Web.Length > 0)
                {
                    Cache()[n] = e with { Icon = IconImages.Prefix + file, Web = "" };
                    got++;
                }
        }
        if (got > 0) Save();
        return got;
    }

    private static Entry? Fallback(string name, out string how)
    {
        how = "alias";
        foreach (string b in AppBrands.BaseNames(name))
            lock (Gate)
                if (Cache().TryGetValue(b, out var e) && e.Rev == Revision && e.Icon.Length > 0)
                    return e with
                    {
                        Path = "", Aumid = "", Description = "", Friendly = name,
                        Owner = e.Owner.Length > 0 ? e.Owner : e.Friendly.Length > 0 ? e.Friendly : b,
                        OwnerKey = e.OwnerKey.Length > 0 ? e.OwnerKey : b,
                    };
        how = "brand";
        var (icon, web) = AppIdentity.Borrowed("", name, "");
        if (icon is null && web is null) return null;
        return WithBrandIcon(new Entry { Icon = icon ?? "", Friendly = name, OwnerKey = name, Web = icon is null ? web!.Spec : "", Rev = Revision });
    }

    private static Entry WithBrandIcon(Entry e) =>
        e.Web.Length > 0 && AppBrands.Web.Parse(e.Web) is { } w && WebIcons.Cached(w) is { } file ? e with { Icon = IconImages.Prefix + file, Web = "" } : e;

    private static Entry? Work(string name, out string source, string? searched = null)
    {
        source = "fixed";
        if (name == "System") return new Entry { Icon = AppBrands.WindowsArt(name) ?? AppIdentity.WindowsGlyph, Friendly = "System", OwnerKey = "System", Rev = Revision };
        if (name == TrafficHelper.ExitedName) return new Entry { Icon = AppIdentity.ExitedGlyph, Friendly = name, OwnerKey = name, Rev = Revision };

        // Most trusted first: what the helper saw the process run from, the running process itself, then
        // registrations and Windows' own records, then a scan of the install folders. A source naming another
        // program file (an App Paths alias, a stale cache) only counts if <name>.exe sits next to it.
        string path = searched ?? "", aumid = "";
        source = searched is null ? "" : "scan";
        if (path.Length == 0)
        {
            lock (Gate)
            {
                if (_helper.TryGetValue(name, out var h)) (path, aumid, source) = (h.Path, h.Aumid, "helper");
                else if (Cache().TryGetValue(name, out var old) && Matching(old.Path, name) is { } kept) (path, aumid, source) = (kept, kept == old.Path ? old.Aumid : "", "cache");
            }
        }
        if (path.Length == 0 && Running(name) is { } r) (path, aumid, source) = (r.Path, r.Aumid, "running");
        if (path.Length == 0 && (Matching(Registered(name), name) ?? Matching(StartMenuIndex.Current.PathOf(name), name)) is { } reg) (path, source) = (reg, "registry");
        if (path.Length == 0 && ExecutableIndex.Known(name) is { } known) (path, aumid, source) = (known.Path, known.Aumid, known.Source);
        if (path.Length == 0 && OnPath(name) is { } onPath) (path, source) = (onPath, "path");
        if (path.Length == 0 && AppBrands.ShimTarget(name) is { } shim) (path, source) = (shim, "shim");
        if (path.Length == 0)
        {
            source = "";
            return null; // tried again once the helper or a running process knows it
        }

        string desc = "";
        try { if (File.Exists(path)) desc = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim() ?? ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (AppBrands.RuntimeBuilt(path, name)) desc = ""; // the runtime's name, not the program's
        var id = AppIdentity.Describe(path, aumid, name, desc);
        return WithBrandIcon(new Entry
        {
            Path = path, Description = desc, Aumid = aumid, Icon = id.Icon, Friendly = id.Friendly,
            Owner = id.Owner, OwnerKey = id.OwnerKey, Web = id.Web, Rev = Revision,
        });
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

    // path itself when it is <name>.exe, else <name>.exe in the same folder (wpp -> office6\wpp.exe, not wps.exe).
    private static string? Matching(string? path, string name)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        if (string.Equals(ProcessImages.NameOf(path), name, StringComparison.OrdinalIgnoreCase)) return path;
        string sibling = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? "", name + ".exe");
        return File.Exists(sibling) ? sibling : null;
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
