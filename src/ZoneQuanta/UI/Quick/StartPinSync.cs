using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using ZoneQuanta.Core;
using ZoneQuanta.Platform;

namespace ZoneQuanta.UI.Quick;

// Mirrors Start-menu pins into the quick panel: everything once, then each newly pinned app.
// A pin is imported only the first time it is seen, so items removed from the panel stay removed.
public sealed class StartPinSync : IDisposable
{
    private readonly QuickActions _actions;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromSeconds(4) };
    private FileSystemWatcher? _watcher;
    private bool _running, _again, _againAll;
    // Reading the layout makes Start rewrite start2.bin right afterwards: new bytes (the file is
    // encrypted) but the same final length. Reacting to that echo re-read the layout every eight
    // seconds, forever. Writes during a read or shortly after it are echo unless, once the writes
    // have settled, the length differs from what it was when the read started (a pin added or removed).
    private DateTime _quietUntil;
    private long _quietLength = -1;
    private bool _real;

    public event Action<IReadOnlyList<string>, bool>? Imported;
    // The explicit import finished: items added, or -1 when the Start layout could not be read.
    public event Action<int>? ImportDone;

    public StartPinSync(QuickActions actions, Dispatcher ui)
    {
        _actions = actions;
        _ui = ui;
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            if (!_enabled || (!_real && StartFileLength() == _quietLength)) return;
            _ = RunAsync(all: false);
        };
    }

    // Switched off, nothing already queued may still import (the explicit import ignores this).
    private bool _enabled;

    public void Apply(bool on)
    {
        _enabled = on;
        if (!on)
        {
            _debounce.Stop();
            _again = false;
            _watcher?.Dispose();
            _watcher = null;
            return;
        }
        if (_watcher is null && Directory.Exists(StartLayout.StateDir))
        {
            try
            {
                _watcher = new FileSystemWatcher(StartLayout.StateDir, "start2.bin")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                FileSystemEventHandler changed = (_, _) => _ui.BeginInvoke(new Action(OnStartChanged));
                _watcher.Changed += changed;
                _watcher.Created += changed;
                _watcher.Renamed += (_, _) => _ui.BeginInvoke(new Action(OnStartChanged));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                Log.Error("start-watch", ex);
            }
        }
        _real = true;
        Poke();
    }

    private void OnStartChanged()
    {
        if (!_enabled) return;
        if (_running)
        {
            _again = true;
            return;
        }
        if (DateTime.UtcNow >= _quietUntil) _real = true;
        Poke();
    }

    private static long StartFileLength()
    {
        try { return new FileInfo(Path.Combine(StartLayout.StateDir, "start2.bin")).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return -1; }
    }

    private void Poke()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    // all = also bring back pins the user removed earlier (the explicit "import" command).
    public async Task<int> RunAsync(bool all)
    {
        if (_running)
        {
            _again = true;
            _againAll |= all;
            return 0;
        }
        _running = true;
        _real = false;
        _quietLength = StartFileLength();
        int count = 0;
        bool failed = false;
        try
        {
            var pins = await Task.Run(StartLayout.ReadAsync);
            if (pins is null)
            {
                failed = true;
                return 0;
            }
            var data = _actions.Store.Data;
            bool first = data.StartSeen.Count == 0;
            var seen = new HashSet<string>(data.StartSeen, StringComparer.OrdinalIgnoreCase);
            var fresh = pins.Where(p => all || !seen.Contains(p.Target)).Select(p => (p.Target, p.Name)).ToList();
            foreach (var p in pins) if (seen.Add(p.Target)) data.StartSeen.Add(p.Target);
            var added = _actions.Store.AddRange(fresh);
            if (added.Count == 0) _actions.Store.Save(null, notify: false);
            count = added.Count;
            Log.Write($"start-sync pins={pins.Count} added={count}");
            if (count > 0) Imported?.Invoke(added.Select(i => i.DisplayName).ToList(), first || all);
        }
        finally
        {
            _running = false;
            _quietUntil = DateTime.UtcNow.AddSeconds(10);
            if (all) ImportDone?.Invoke(failed ? -1 : count);
            if (_again)
            {
                bool rerunAll = _againAll;
                _again = _againAll = false;
                if (rerunAll) _ = RunAsync(all: true);
                else Poke();
            }
        }
        return count;
    }

    public void Dispose()
    {
        _debounce.Stop();
        _watcher?.Dispose();
    }
}
