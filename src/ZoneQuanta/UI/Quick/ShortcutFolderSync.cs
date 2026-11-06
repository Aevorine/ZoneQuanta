using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using ZoneQuanta.Core;
using ZoneQuanta.Core.Quick;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.Platform;

namespace ZoneQuanta.UI.Quick;

// New apps in the Start menu folders (the user's and everyone's "Programs") and in extra folders join the quick
// panel by themselves, grouped. A folder's existing shortcuts are its baseline; every shortcut is looked at once,
// so one removed from the panel never comes back. Two ways to notice: file system events (seconds, debounced
// while an installer writes) and a rescan every ten minutes (events can be lost; folders can appear later).
public sealed class ShortcutFolderSync : IDisposable
{
    private readonly QuickActions _actions;
    private readonly AppSettings _settings;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _rescan = new() { Interval = TimeSpan.FromMinutes(10) };
    private readonly List<FileSystemWatcher> _watchers = new();
    private bool _enabled, _running, _again;

    // Names of the apps added, after they were grouped.
    public event Action<IReadOnlyList<string>>? Imported;

    public ShortcutFolderSync(QuickActions actions, AppSettings settings, Dispatcher ui)
    {
        _actions = actions;
        _settings = settings;
        _ui = ui;
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = RunAsync(importExisting: false);
        };
        _rescan.Tick += (_, _) => _ = RunAsync(importExisting: false);
    }

    public static IReadOnlyList<string> DefaultFolders { get; } = new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
    }.Where(d => d.Length > 0).ToArray();

    public IReadOnlyList<string> Folders() =>
        DefaultFolders.Concat(_settings.QuickWatchFolders).Where(d => d.Length > 0)
            .Select(d => d.TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public void Apply(bool on)
    {
        _enabled = on;
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
        _debounce.Stop();
        _rescan.Stop();
        if (!on) return;
        foreach (var dir in Folders())
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                var w = new FileSystemWatcher(dir)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    InternalBufferSize = 32 * 1024,
                };
                FileSystemEventHandler poke = (_, e) => { if (ShortcutFilter.IsShortcut(e.FullPath) || Directory.Exists(e.FullPath)) _ui.BeginInvoke(new Action(Poke)); };
                w.Created += poke;
                w.Renamed += (_, e) => { if (ShortcutFilter.IsShortcut(e.FullPath)) _ui.BeginInvoke(new Action(Poke)); };
                // A lost event buffer: a full rescan finds whatever was missed.
                w.Error += (_, ex) =>
                {
                    Log.Write($"folder-watch {dir}: {ex.GetException().Message}");
                    _ui.BeginInvoke(new Action(Poke));
                };
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                Log.Error("folder-watch", ex);
            }
        }
        _rescan.Start();
        Log.Write($"folder-watch on: {string.Join(" | ", Folders())}");
        Poke();
    }

    private void Poke()
    {
        if (!_enabled) return;
        _debounce.Stop();
        _debounce.Start();
    }

    // importExisting: also the shortcuts that were there before (the explicit command); returns items added.
    public async Task<int> RunAsync(bool importExisting)
    {
        if (!_enabled && !importExisting) return 0;
        if (_running && importExisting)
            for (int i = 0; i < 400 && _running; i++) await Task.Delay(50);
        if (_running)
        {
            _again = true;
            return 0;
        }
        _running = true;
        try
        {
            var data = _actions.Store.Data;
            var folders = Folders();
            var seen = new HashSet<string>(data.FolderSeen, StringComparer.OrdinalIgnoreCase);
            var roots = new HashSet<string>(data.FolderRoots, StringComparer.OrdinalIgnoreCase);
            var have = _actions.Store.Items.Select(i => i.Path).ToList();
            var sw = Stopwatch.StartNew();
            var scan = await Task.Run(() => Scan(folders, seen, roots, have, importExisting));

            // Everything looked at is remembered; entries whose file is gone are dropped, so a reinstalled app returns.
            data.FolderSeen = scan.Seen.ToList();
            data.FolderRoots = scan.Roots.ToList();
            var added = _actions.Store.AddRange(scan.Fresh.Select(f => (f.Path, (string?)f.Name)));
            if (added.Count == 0) _actions.Store.Save(null, notify: false);
            Log.Write($"folder-watch scan {scan.Files} files, new {scan.New}, added {added.Count}, skipped {string.Join(" ", scan.Skipped.Take(12))} {sw.ElapsedMilliseconds}ms");
            if (added.Count == 0) return 0;
            _actions.Imported(added);
            await _actions.AutoGroupAsync(added);
            Imported?.Invoke(added.Select(i => i.DisplayName).ToList());
            return added.Count;
        }
        catch (Exception ex)
        {
            Log.Error("folder-watch scan", ex);
            return 0;
        }
        finally
        {
            _running = false;
            if (_again)
            {
                _again = false;
                Poke();
            }
        }
    }

    private sealed record ScanResult(HashSet<string> Seen, HashSet<string> Roots, List<(string Path, string Name)> Fresh, int Files, int New, List<string> Skipped);

    // Off the UI thread: list every shortcut, take a new folder's contents as its baseline, and keep the new
    // shortcuts that are apps not already in the panel (same shortcut, or the same program by another path).
    private static ScanResult Scan(IReadOnlyList<string> folders, HashSet<string> seen, HashSet<string> roots, List<string> have, bool importExisting)
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6, AttributesToSkip = FileAttributes.System };
        var nowSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nowRoots = new HashSet<string>(roots.Where(r => folders.Contains(r, StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
        var candidates = new List<string>();
        int files = 0;
        foreach (var dir in folders)
        {
            if (!Directory.Exists(dir))
            {
                // Not there now (a removed drive): keep what was seen under it.
                foreach (var s in seen) if (s.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase)) nowSeen.Add(s);
                continue;
            }
            bool baseline = !roots.Contains(dir) && !importExisting;
            foreach (var f in Directory.EnumerateFiles(dir, "*", opts))
            {
                if (!ShortcutFilter.IsShortcut(f)) continue;
                files++;
                if (!seen.Contains(f) && !baseline) candidates.Add(f);
                else if (importExisting && seen.Contains(f)) candidates.Add(f);
                nowSeen.Add(f);
            }
            nowRoots.Add(dir);
        }

        var fresh = new List<(string, string)>();
        var skipped = new List<string>();
        if (candidates.Count == 0) return new ScanResult(nowSeen, nowRoots, fresh, files, 0, skipped);
        // Programs already in the panel, by path and by shortcut target (read only when something is new).
        var known = new HashSet<string>(have.Select(QuickStore.Normalize), StringComparer.OrdinalIgnoreCase);
        foreach (var p in have)
            if (p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && ShellLinks.Target(p) is { Length: > 0 } t) known.Add(QuickStore.Normalize(t));

        foreach (var f in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string target = f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShellLinks.Target(f) : "";
            string name = Path.GetFileNameWithoutExtension(f);
            if (ShortcutFilter.Skip(f, target) is { } why)
            {
                skipped.Add($"{name}({why})");
                continue;
            }
            if (known.Contains(QuickStore.Normalize(f)) || (target.Length > 0 && known.Contains(QuickStore.Normalize(target))))
            {
                if (importExisting) continue;
                skipped.Add($"{name}(have)");
                continue;
            }
            known.Add(QuickStore.Normalize(f));
            if (target.Length > 0) known.Add(QuickStore.Normalize(target));
            fresh.Add((f, name));
        }
        return new ScanResult(nowSeen, nowRoots, fresh, files, candidates.Count, skipped);
    }

    public void Dispose()
    {
        _debounce.Stop();
        _rescan.Stop();
        foreach (var w in _watchers) w.Dispose();
        _watchers.Clear();
    }
}
