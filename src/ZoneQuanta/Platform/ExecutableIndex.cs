using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;
using ZoneQuanta.Core;

namespace ZoneQuanta.Platform;

// Where the program file of a process name lives, for traffic from programs that are no longer running.
// Evidence, most trusted first: Store packages (path + AppUserModelID), what Windows recorded about programs
// that were started (Program Compatibility Assistant, MuiCache), the system folders, and last a pruned scan of
// the install folders (exe-index.tsv, rebuilt at most daily). Background threads only.
internal static class ExecutableIndex
{
    public sealed record Hit(string Path, string Aumid, string Source);

    private const long EvidenceTtlMs = 600_000;
    private static readonly TimeSpan ScanTtl = TimeSpan.FromDays(1);
    // Folders that never hold the programs that use the network, but can hold huge trees.
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "Cache", "Caches", "cache", "Code Cache", "GPUCache", "CachedData", "Temp", "tmp", "CrashDumps", "logs",
        ".gradle", ".m2", ".nuget", ".npm", ".cache", ".pnpm-store", ".yarn", ".android", "lib", "share", "doc", "docs", "man", "icons",
        "Packages", "WindowsApps", "site-packages", "__pycache__", "include", "locales", "$Recycle.Bin",
        "User Data", "IndexedDB", "Service Worker", "registry", "go-build",
    };
    private static readonly object Gate = new(), ScanGate = new();
    private static Dictionary<string, List<Hit>>? _evidence;
    private static long _evidenceAt;

    private static string ScanFile => DataPaths.File("exe-index.tsv");

    // Cheap sources only (registry, package manifests, system folders).
    public static Hit? Known(string name)
    {
        var all = Evidence();
        if (all.TryGetValue(name, out var hits))
        {
            var live = hits.Where(h => File.Exists(h.Path)).ToList();
            // A Store package, else the file this user last ran under that name, else the newest recorded copy.
            var pick = live.FirstOrDefault(h => h.Aumid.Length > 0) ?? live.FirstOrDefault(h => h.Source == "recent");
            if (pick is not null) return pick;
            if (Newest(live.Select(h => h.Path)) is { } p) return live.First(h => h.Path == p);
        }
        foreach (string dir in SystemDirs())
        {
            string p = Path.Combine(dir, name + ".exe");
            if (File.Exists(p)) return new Hit(p, "", "system");
        }
        return null;
    }

    // Program files of names no cheap source knows: one breadth-first search of the install folders for all of
    // them (shallow installs first), ending when every name is found or after MaxFolders folders / MaxSearchMs.
    // Answers, found or not, are kept in exe-index.tsv for a day so the search does not repeat.
    public static Dictionary<string, string> Search(IReadOnlyCollection<string> names, IEnumerable<string> extraRoots)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        lock (ScanGate)
        {
            var memo = Memo();
            var want = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in names)
            {
                if (memo.TryGetValue(n, out var m) && DateTime.UtcNow - m.At < ScanTtl)
                {
                    if (m.Path.Length > 0 && File.Exists(m.Path)) found[n] = m.Path;
                    continue;
                }
                want.Add(n);
            }
            if (want.Count == 0) return found;
            var sw = Stopwatch.StartNew();
            var (folders, complete) = Breadth(want, Roots(extraRoots), found);
            var now = DateTime.UtcNow;
            // A search cut short by its budget proves nothing about the rest: those misses are asked again in an hour.
            var missAt = complete ? now : now - ScanTtl + TimeSpan.FromHours(1);
            foreach (string n in names)
                if (!memo.TryGetValue(n, out var m) || now - m.At >= ScanTtl) memo[n] = found.TryGetValue(n, out var f) ? (f, now) : ("", missAt);
            SaveMemo(memo);
            Log.Write($"exe-search {found.Count}/{names.Count} found, {folders} folders {sw.ElapsedMilliseconds}ms{(complete ? "" : " (budget reached)")}"
                      + (want.Count > 0 ? $"; {(complete ? "not found" : "not reached")}: {string.Join(", ", want.Take(15))}" : ""));
        }
        return found;
    }

    // The latest install wins when several copies exist (an old version folder, a portable copy).
    private static string? Newest(IEnumerable<string> paths)
    {
        string? best = null;
        DateTime at = DateTime.MinValue;
        foreach (string p in paths)
        {
            DateTime t;
            try { t = File.GetLastWriteTimeUtc(p); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            if (best is null || t > at) (best, at) = (p, t);
        }
        return best;
    }

    private static Dictionary<string, List<Hit>> Evidence()
    {
        lock (Gate)
        {
            if (_evidence is not null && Environment.TickCount64 - _evidenceAt < EvidenceTtlMs) return _evidence;
            var sw = Stopwatch.StartNew();
            var d = new Dictionary<string, List<Hit>>(StringComparer.OrdinalIgnoreCase);
            void Add(string path, string aumid, string source)
            {
                if (path.Length < 4 || path[1] != ':' || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
                string n = ProcessImages.NameOf(path);
                if (!d.TryGetValue(n, out var list)) d[n] = list = new List<Hit>();
                if (!list.Exists(h => string.Equals(h.Path, path, StringComparison.OrdinalIgnoreCase))) list.Add(new Hit(path, aumid, source));
            }
            int pkg = Packages(Add);
            int pca = ValueNames(Registry.CurrentUser, @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store", v => Add(v, "", "history"));
            int mui = ValueNames(Registry.CurrentUser, @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache", v =>
            {
                int exe = v.IndexOf(".exe.", StringComparison.OrdinalIgnoreCase);
                if (exe > 0) Add(v[..(exe + 4)], "", "history");
            });
            int seen = 0;
            try
            {
                string file = Path.Combine(DataPaths.HelperDir, Core.Traffic.TrafficHelper.SeenFileName);
                if (File.Exists(file))
                    foreach (string line in File.ReadLines(file, Encoding.UTF8))
                        if (line.IndexOf('\t') is int tab and > 0) { Add(line[(tab + 1)..], "", "recent"); seen++; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error("exe-seen", ex); }
            Log.Write($"exe-evidence packages {pkg}, compat {pca}, muicache {mui}, recent runs {seen}, {d.Count} names {sw.ElapsedMilliseconds}ms");
            _evidenceAt = Environment.TickCount64;
            return _evidence = d;
        }
    }

    private static int ValueNames(RegistryKey hive, string key, Action<string> each)
    {
        int n = 0;
        try
        {
            using var k = hive.OpenSubKey(key);
            foreach (string v in k?.GetValueNames() ?? Array.Empty<string>()) { each(v); n++; }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { Log.Error("exe-evidence", ex); }
        return n;
    }

    // Installed Store packages of this user: each application's program file and AppUserModelID.
    private static int Packages(Action<string, string, string> add)
    {
        int n = 0;
        try
        {
            using var repo = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            foreach (string full in repo?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                try
                {
                    using var k = repo!.OpenSubKey(full);
                    if (k?.GetValue("PackageRootFolder") is not string root) continue;
                    string manifest = Path.Combine(root, "AppxManifest.xml");
                    if (!File.Exists(manifest)) continue;
                    foreach (var app in XDocument.Load(manifest).Descendants().Where(e => e.Name.LocalName == "Application"))
                    {
                        if ((string?)app.Attribute("Executable") is not { Length: > 0 } exe || (string?)app.Attribute("Id") is not { Length: > 0 } id) continue;
                        add(Path.Combine(root, exe), ProcessImages.AumidOf(full, id), "package");
                        n++;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or System.Security.SecurityException) { }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { Log.Error("exe-packages", ex); }
        return n;
    }

    private static IEnumerable<string> SystemDirs()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        yield return Environment.SystemDirectory;
        yield return Path.Combine(win, "SysWOW64");
        yield return win;
        // Windows Update's own programs (MoUsoCoreWorker, WaaSMedicAgent) live here on current builds.
        yield return Path.Combine(win, "UUS", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "amd64");
    }

    // Programs this user ran lately, by name, the most recent run's file when one name was run from several places
    // (Background Activity Moderator: each value is a program path, its data starts with the last-run FILETIME).
    // Readable only elevated, so the traffic helper reads it and leaves the answer in apps-seen.tsv.
    public static Dictionary<string, string> RecentlyRun()
    {
        var latest = new Dictionary<string, (string Path, long At)>(StringComparer.OrdinalIgnoreCase);
        string? sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
        if (sid is null) return new Dictionary<string, string>();
        foreach (string key in new[] { @"SYSTEM\CurrentControlSet\Services\bam\State\UserSettings", @"SYSTEM\CurrentControlSet\Services\bam\UserSettings" })
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(key + "\\" + sid);
                foreach (string v in k?.GetValueNames() ?? Array.Empty<string>())
                {
                    if (!v.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase) || !v.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    long at = k!.GetValue(v) is byte[] { Length: >= 8 } data ? BitConverter.ToInt64(data, 0) : 0;
                    string path = ProcessImages.ToDosPath(v);
                    string name = ProcessImages.NameOf(path);
                    if (!latest.TryGetValue(name, out var cur) || at > cur.At) latest[name] = (path, at);
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { Log.Error("bam", ex); }
        }
        return latest.ToDictionary(kv => kv.Key, kv => kv.Value.Path, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, (string Path, DateTime At)> Memo()
    {
        var d = new Dictionary<string, (string, DateTime)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(ScanFile)) return d;
            foreach (string line in File.ReadLines(ScanFile, Encoding.UTF8))
            {
                var p = line.Split('\t');
                if (p.Length == 3 && p[0].Length > 0 && long.TryParse(p[2], out long t)) d[p[0]] = (p[1], DateTime.FromBinary(t));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Log.Error("exe-index-read", ex); }
        return d;
    }

    private static void SaveMemo(Dictionary<string, (string Path, DateTime At)> memo)
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var (n, (path, at)) in memo)
                if (DateTime.UtcNow - at < ScanTtl) sb.Append(n).Append('\t').Append(path).Append('\t').Append(at.ToBinary()).Append('\n');
            File.WriteAllText(ScanFile + ".tmp", sb.ToString(), Encoding.UTF8);
            File.Move(ScanFile + ".tmp", ScanFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error("exe-index", ex); }
    }

    // Where applications get installed, most likely first; depth limits keep caches and data folders cheap.
    private static List<(string Dir, int Depth)> Roots(IEnumerable<string> extraRoots)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new List<(string Dir, int Depth)>
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), 3),
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), 3),
            (Path.Combine(local, "Programs"), 3),
        };
        roots.AddRange(extraRoots.Select(r => (r.TrimEnd('\\'), 3)));
        roots.Add((Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 3));
        roots.Add((local, 2));
        try { roots.AddRange(Directory.EnumerateDirectories(profile, ".*").Where(d => !Skip.Contains(Path.GetFileName(d))).Select(d => (d, 3))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        // A root inside another one is covered by it.
        var keep = new List<(string Dir, int Depth)>();
        foreach (var r in roots)
            if (r.Dir.Length > 3 && Directory.Exists(r.Dir) && !keep.Exists(k => r.Dir.StartsWith(k.Dir + "\\", StringComparison.OrdinalIgnoreCase) || string.Equals(r.Dir, k.Dir, StringComparison.OrdinalIgnoreCase)))
                keep.Add(r);
        return keep;
    }

    private const int MaxFolders = 40_000, MaxSearchMs = 8_000;

    // Level by level over all roots at once, below normal priority so the desktop stays smooth.
    // Returns the folders visited and whether the search finished (everything found or nothing left to visit).
    private static (int Folders, bool Complete) Breadth(HashSet<string> want, List<(string Dir, int Depth)> roots, Dictionary<string, string> found)
    {
        var prio = System.Threading.Thread.CurrentThread.Priority;
        System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.BelowNormal;
        var queue = new Queue<(string Dir, int Left)>(roots.Select(r => (r.Dir, r.Depth)));
        var sw = Stopwatch.StartNew();
        int folders = 0;
        try
        {
            while (queue.Count > 0 && want.Count > 0 && folders < MaxFolders && sw.ElapsedMilliseconds < MaxSearchMs)
            {
                var (dir, left) = queue.Dequeue();
                folders++;
                try
                {
                    foreach (string f in Directory.EnumerateFiles(dir, "*.exe"))
                    {
                        string n = ProcessImages.NameOf(f);
                        if (want.Remove(n)) found[n] = f;
                    }
                    if (left <= 0) continue;
                    foreach (string sub in Directory.EnumerateDirectories(dir))
                    {
                        if (Skip.Contains(Path.GetFileName(sub))) continue;
                        try { if (new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue; }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                        queue.Enqueue((sub, left - 1));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
        }
        finally { System.Threading.Thread.CurrentThread.Priority = prio; }
        return (folders, want.Count == 0 || queue.Count == 0);
    }
}
