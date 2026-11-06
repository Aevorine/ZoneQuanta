using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ZoneQuanta.Core;
using ZoneQuanta.Core.Quick;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.Platform;

namespace ZoneQuanta.UI.Quick;

// Operations on quick items shared by the launcher window, the path bar, the panel page and the tray.
public sealed class QuickActions
{
    private readonly DispatcherTimer _autoTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _autoRunning, _autoAgain;

    public QuickActions(QuickStore store, AppSettings settings)
    {
        Store = store;
        Settings = settings;
        Importer = new QuickImport(this);
        _autoTimer.Tick += (_, _) =>
        {
            _autoTimer.Stop();
            _ = AutoGroupAsync();
        };
        _goneTimer.Tick += (_, _) =>
        {
            _goneTimer.Stop();
            _ = ScanGoneAsync();
        };
        store.Changed += _ => { ScheduleAutoGroup(); ScheduleGoneScan(); };
        WarmFacts();
        ScheduleGoneScan();
        settings.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppSettings.QuickAutoGroup)) ScheduleAutoGroup(); };
    }

    public QuickStore Store { get; }
    public AppSettings Settings { get; }
    public QuickImport Importer { get; }
    public QuickItem? Highlight { get; private set; }

    // Items whose target is gone for good (see ItemHealth), rescanned after changes and when the panel opens.
    private readonly DispatcherTimer _goneTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private HashSet<QuickItem> _gone = new();
    private int _goneScan;
    public IReadOnlyCollection<QuickItem> Gone => _gone;
    public bool IsGone(QuickItem item) => _gone.Contains(item);
    public event Action? GoneChanged;
    // Image files of items whose picture was replaced (imported links' real icons).
    public event Action<IReadOnlyCollection<string>>? IconsChanged;

    public void ScheduleGoneScan()
    {
        _goneTimer.Stop();
        _goneTimer.Start();
    }

    private async Task ScanGoneAsync()
    {
        int pass = ++_goneScan;
        var items = Store.Items.Select(i => (Item: i, i.Path)).ToList();
        var sw = Stopwatch.StartNew();
        var gone = await Task.Run(() => items.Where(x => ItemHealth.IsGone(x.Path)).Select(x => x.Item).ToHashSet());
        if (pass != _goneScan) return;
        gone.IntersectWith(Store.Items);
        bool changed = !gone.SetEquals(_gone);
        _gone = gone;
        if (changed)
        {
            Log.Write($"quick gone {gone.Count}/{items.Count} {sw.ElapsedMilliseconds}ms{(gone.Count > 0 ? ": " + string.Join(", ", gone.Take(12).Select(i => i.DisplayName)) : "")}");
            GoneChanged?.Invoke();
        }
    }

    // A file added as itself, never read as bookmarks.
    public void AddFile(string path)
    {
        var (item, isNew) = Store.Add(path);
        Highlight = item;
        if (isNew) Log.Write("quick add 1");
    }

    // For the command line: scans now instead of trusting the last background pass.
    public async Task<int> CleanGoneNowAsync()
    {
        var items = Store.Items.Select(i => (Item: i, i.Path)).ToList();
        var gone = await Task.Run(() => items.Where(x => ItemHealth.IsGone(x.Path)).Select(x => x.Item).ToList());
        int n = Store.RemoveMany(gone, "target gone");
        _gone = new HashSet<QuickItem>();
        GoneChanged?.Invoke();
        if (n > 0) RefreshAutoNames();
        return n;
    }

    // One click: every item whose target is gone leaves the panel, in one undo step.
    public int CleanGone()
    {
        var gone = _gone.Where(i => Store.Items.Contains(i) && ItemHealth.IsGone(i.Path)).ToList();
        int n = Store.RemoveMany(gone, "target gone");
        _gone = new HashSet<QuickItem>();
        GoneChanged?.Invoke();
        if (n > 0) RefreshAutoNames();
        return n;
    }

    // After an import: the first new link pops, groups that received links are named for their members again.
    public void Imported(IReadOnlyList<QuickItem> added)
    {
        if (added.Count == 0) return;
        Highlight = added[0];
        Store.Save(added[0]);
        RefreshAutoNames();
    }

    public void RefreshIcons(IReadOnlyCollection<string> files)
    {
        var app = Application.Current;
        if (app is null) return;
        app.Dispatcher.BeginInvoke(new Action(() =>
        {
            foreach (var f in files) ShellIcons.Forget(IconImages.Prefix + f);
            IconsChanged?.Invoke(files);
        }));
    }

    // Reordering while another sort is shown keeps what the user sees as the custom order first.
    private void EnsureCustomOrder()
    {
        if (Settings.QuickSort == "Custom") return;
        Store.Reorder(View("").ToList());
        Log.Write($"quick sort {Settings.QuickSort} -> Custom (kept the shown order)");
        Settings.QuickSort = "Custom";
    }

    public void PlaceItem(QuickItem item, int col, int row)
    {
        Store.PlaceLoose(item, col, row);
        RefreshAutoNames();
    }

    public void PlaceInGroup(QuickItem item, string group, QuickItem? before)
    {
        EnsureCustomOrder();
        bool moved = !string.Equals(item.Group, group, StringComparison.OrdinalIgnoreCase);
        Store.PlaceInGroup(item, group, before);
        if (moved) RefreshAutoNames();
    }

    public void MoveBefore(QuickItem item, QuickItem? before)
    {
        EnsureCustomOrder();
        Store.MoveBefore(item, before);
    }

    public void UnpinItem(QuickItem item) => Store.UnpinItem(item);

    // A group just made from two items; the view starts renaming it once it is on screen.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Kind, string Sub, string Maker)> _facts = new(StringComparer.OrdinalIgnoreCase);
    public QuickGroup? PendingRename { get; private set; }
    public event Action? Opened;
    public event Action<string>? Failed;

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    public void Open(QuickItem item)
    {
        var sw = Stopwatch.StartNew();
        if (!Exists(item.Path) && !PathEngine.IsShell(item.Path))
        {
            Failed?.Invoke(item.Path);
            return;
        }
        if (!ShellLauncher.Open(item.Path))
        {
            Failed?.Invoke(item.Path);
            return;
        }
        Store.Touch(item);
        Log.Write($"quick open {sw.Elapsed.TotalMilliseconds:0.0}ms {Path.GetExtension(item.Path)}");
        Opened?.Invoke();
    }

    // Typed or pasted text: path, quoted path, %VAR%, ~, shell:, URL or a folder alias.
    public bool OpenText(string text)
    {
        var target = PathEngine.Resolve(text);
        if (target.Kind is TargetKind.None or TargetKind.Missing)
        {
            Failed?.Invoke(target.Value);
            return false;
        }
        if (!ShellLauncher.Open(target.Value))
        {
            Failed?.Invoke(target.Value);
            return false;
        }
        if (target.Kind != TargetKind.Url) Store.Remember(target.Value);
        if (Store.Find(target.Value) is { } item) Store.Touch(item);
        Log.Write($"path open {target.Kind}");
        Opened?.Invoke();
        return true;
    }

    public void RunAsAdmin(QuickItem item)
    {
        if (ShellLauncher.RunAsAdmin(item.Path))
        {
            Store.Touch(item);
            Opened?.Invoke();
        }
    }

    public void OpenWith(QuickItem item) => ShellLauncher.OpenWith(item.Path);
    public void Reveal(QuickItem item) => ShellLauncher.Reveal(item.Path);
    public void Properties(QuickItem item) => ShellLauncher.Properties(item.Path);
    public void Remove(QuickItem item)
    {
        Store.Remove(item);
        RefreshAutoNames();
    }
    public void Rename(QuickItem item, string name)
    {
        Store.Rename(item, name);
        _facts.TryRemove(item.Path, out _); // the kind is read from the name too
    }
    public void Move(QuickItem item, int index) => Store.Move(item, index);

    public void CopyPath(QuickItem item)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetDataObject(item.Path, true);
                return;
            }
            catch (COMException) { System.Threading.Thread.Sleep(30); } // clipboard held by another process
        }
    }

    // group: the group the files were dropped on; null leaves them to automatic grouping.
    public List<QuickItem> Add(IEnumerable<string> paths, int index = -1, string? group = null)
    {
        var added = new List<QuickItem>();
        var all = new List<QuickItem>();
        foreach (string raw in paths)
        {
            string p = PathEngine.Clean(raw);
            if (p.Length == 0 || !(Exists(p) || PathEngine.IsShell(p))) continue;
            // A bookmark file brings its links, not itself (it is added as a file after all when it holds none).
            if (File.Exists(p) && BookmarkReader.LooksLikeBookmarks(p))
            {
                _ = Importer.RunAsync(p, addIfNotBookmarks: true);
                continue;
            }
            if (!PathEngine.IsShell(p) && !Path.IsPathFullyQualified(p)) p = Path.GetFullPath(p);
            var (item, isNew) = Store.Add(p, index < 0 ? -1 : index + added.Count);
            if (isNew) added.Add(item);
            all.Add(item);
            Highlight = item;
        }
        if (group is not null && all.Count > 0)
        {
            Store.SetGroup(all, group);
            RefreshAutoNames();
        }
        if (added.Count > 0) Log.Write($"quick add {added.Count}");
        return added;
    }

    public void ClearHighlight() => Highlight = null;

    // Layout changes (grouping, moving, placing, sizing, renaming, removing) step back and forth, 50 deep.
    public bool Undo() => Step(Store.Undo());
    public bool Redo() => Step(Store.Redo());

    private bool Step(bool done)
    {
        if (!done) return false;
        Highlight = null;
        PendingRename = null;
        return true;
    }

    // Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z) anywhere in scope; a text field that can undo its own typing keeps the keys.
    public void AttachUndoKeys(UIElement scope)
    {
        scope.PreviewKeyDown += (_, e) =>
        {
            var m = System.Windows.Input.Keyboard.Modifiers;
            if (m != System.Windows.Input.ModifierKeys.Control && m != (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift)) return;
            bool redo = e.Key == System.Windows.Input.Key.Y || (e.Key == System.Windows.Input.Key.Z && m.HasFlag(System.Windows.Input.ModifierKeys.Shift));
            bool undo = e.Key == System.Windows.Input.Key.Z && !redo;
            if (!undo && !redo) return;
            if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox box && (undo ? box.CanUndo : box.CanRedo)) return;
            e.Handled = redo ? Redo() : Undo();
        };
    }
    public void ClearPendingRename() => PendingRename = null;

    public void MoveToGroup(QuickItem item, string group)
    {
        Store.SetGroup(new[] { item }, group);
        RefreshAutoNames();
    }

    public void Ungroup(QuickItem item)
    {
        Store.SetGroup(new[] { item }, "");
        RefreshAutoNames();
    }

    public void PlaceGroup(QuickGroup group, int col, int row) => Store.PlaceGroup(group, col, row);
    public void Unpin(QuickGroup? group) => Store.Unpin(group);

    // Right-click "按内容命名": named from the members again, and follows them from now on.
    public void NameByContent(QuickGroup group)
    {
        var members = Store.ItemsOf(group).ToList();
        if (members.Count == 0) return;
        string want = SuggestName(members, group);
        if (!GroupNamer.IsNameFor(group.Name, want)) Store.RenameGroup(group, Store.UniqueGroupName(want), auto: true);
        else Store.MarkAutoNamed(group);
    }
    public void RenameGroup(QuickGroup group, string name) => Store.RenameGroup(group, name);
    public void Dissolve(QuickGroup group) => Store.Dissolve(group);
    public void SetExpanded(QuickGroup group, bool expanded, (int Col, int Row)? at = null) => Store.SetExpanded(group, expanded, at);
    public void SetSize(QuickGroup group, int cols, int rows) => Store.SetSize(group, cols, rows);

    public void SetDefaultSize(int cols, int rows)
    {
        bool auto = cols <= 0 || rows <= 0;
        Settings.QuickCardCols = auto ? 0 : cols;
        Settings.QuickCardRows = auto ? 0 : rows;
    }

    // A hand-made group is named from what its members are for; the name opens selected for editing.
    public QuickGroup? NewGroup(IReadOnlyCollection<QuickItem> items)
    {
        var sw = Stopwatch.StartNew();
        string name = SuggestName(items);
        var g = Store.SetGroup(items, Store.UniqueGroupName(name));
        if (g is not null) Store.MarkAutoNamed(g);
        PendingRename = g;
        RefreshAutoNames(); // the group the items came from may be named after them
        Log.Write($"group-name \"{name}\" for {items.Count} items {sw.ElapsedMilliseconds}ms");
        return g;
    }

    // self: the group being named, whose own name does not count as taken.
    public string SuggestName(IReadOnlyCollection<QuickItem> items, QuickGroup? self = null)
    {
        var facts = items.Select(Facts).ToList();
        bool Taken(string name) => Store.Groups.Any(g => g != self && GroupNamer.IsNameFor(g.Name, name));
        return GroupNamer.Suggest(facts.Select(f => f.Kind).ToList(), items.Select(i => i.DisplayName).ToList(), facts.Select(f => f.Maker).ToList(),
            facts.Select(f => f.Sub).ToList(), Taken);
    }

    // What an item is for and who makes it, cached per path. Read off the UI thread: a shortcut on an
    // offline network drive gets 200 ms, then counts as unknown while the read finishes in the background.
    private (string Kind, string Sub, string Maker) Facts(QuickItem item)
    {
        if (_facts.TryGetValue(item.Path, out var f)) return f;
        string path = item.Path, name = item.DisplayName;
        var read = Task.Run(() => _facts[path] = ReadFacts(path, name));
        return read.Wait(200) ? read.Result : ("", "", "");
    }

    private static (string Kind, string Sub, string Maker) ReadFacts(string path, string name)
    {
        var hint = ShellLinks.Describe(path);
        var e = QuickCategories.Explain(path, name, hint);
        return (e.Group, e.Sub, hint?.Company ?? "");
    }

    // Programs that only start others: they say nothing about what a shortcut is for.
    private static readonly HashSet<string> Launchers = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "rundll32.exe", "wscript.exe", "cscript.exe", "javaw.exe", "java.exe",
        "python.exe", "pythonw.exe", "mshta.exe", "chrome_proxy.exe", "chrome.exe", "msedge.exe", "msedge_proxy.exe", "firefox.exe",
        "conhost.exe", "wt.exe", "mmc.exe", "control.exe", "update.exe",
    };

    // What identifies an item across shortcuts: the site it opens (its own host: mail.google.com is not
    // drive.google.com), its Store app, or its program file.
    private static string Signature(string path, TargetHint? hint)
    {
        if (hint?.Url is { } u) return "site:" + (u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host).ToLowerInvariant();
        if (path.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase))
        {
            string id = path[17..];
            int bang = id.IndexOf('!');
            return "app:" + (bang > 0 ? id[..bang] : id).ToLowerInvariant();
        }
        string exe = Path.GetFileName(hint?.Target ?? "");
        return exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !Launchers.Contains(exe) ? "exe:" + exe.ToLowerInvariant() : "";
    }

    // What the user taught by placing items by hand: the same site, Store app or program goes to the same
    // group, and a narrower kind (电脑管家, 终端) that two or more hand-placed items share goes to the group of
    // the user's own they all sit in. Only where those placements agree. Background thread.
    private sealed record Lessons(Dictionary<string, string> BySignature, Dictionary<(string Kind, string Sub), string> BySub);

    // groupKinds: each group's bound kind, read on the UI thread.
    private static Lessons Learn(IEnumerable<(string Path, string Name, string Group)> manual, IReadOnlyDictionary<string, string?> groupKinds)
    {
        var bySig = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var clash = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var subs = new Dictionary<(string, string), List<string>>();
        foreach (var (path, name, group) in manual)
        {
            var hint = ShellLinks.Describe(path);
            string sig = Signature(path, hint);
            if (sig.Length > 0 && !clash.Contains(sig))
            {
                if (bySig.TryGetValue(sig, out var had) && !string.Equals(had, group, StringComparison.OrdinalIgnoreCase))
                {
                    bySig.Remove(sig);
                    clash.Add(sig);
                }
                else bySig[sig] = group;
            }
            var e = QuickCategories.Explain(path, name, hint);
            if (e.Sub.Length == 0) continue;
            if (!subs.TryGetValue((e.Group, e.Sub), out var list)) subs[(e.Group, e.Sub)] = list = new();
            list.Add(group);
        }
        var bySub = new Dictionary<(string, string), string>();
        foreach (var (key, groups) in subs)
        {
            if (groups.Count < 2 || groups.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1) continue;
            // Taught only by a group that is not simply that kind's own group.
            if (groupKinds.TryGetValue(groups[0], out var bound) && bound != key.Item1) bySub[key] = groups[0];
        }
        return new Lessons(bySig, bySub);
    }

    private List<(string Path, string Name, string Group)> ManualPlacements() =>
        Store.Items.Where(i => i.GroupManual && i.Group.Length > 0).Select(i => (i.Path, i.DisplayName, i.Group)).ToList();

    private Dictionary<string, string?> GroupKinds() =>
        Store.Groups.GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Kind, StringComparer.OrdinalIgnoreCase);

    // Where an item goes and why: what the user taught first, then the rules.
    private static (string Kind, string? Group, string Why) Pick(string path, string name, Lessons learned)
    {
        var hint = ShellLinks.Describe(path);
        string sig = Signature(path, hint);
        var e = QuickCategories.Explain(path, name, hint);
        if (sig.Length > 0 && learned.BySignature.TryGetValue(sig, out var taught)) return (e.Group, taught, "learned " + sig);
        if (e.Sub.Length > 0 && learned.BySub.TryGetValue((e.Group, e.Sub), out var bySub)) return (e.Group, bySub, $"learned {e.Sub}, {e.Why}");
        return (e.Group, null, e.Sub.Length > 0 ? $"{e.Why}, {e.Sub}" : e.Why);
    }

    private static readonly System.Text.RegularExpressions.Regex Mail = new(@"[\w.+-]+@[\w-]+(\.[\w-]+)+");

    private static string Describe(IEnumerable<(QuickItem Item, string Kind, string? Group, string Why)> picks) =>
        string.Join(", ", picks.Take(40).Select(p => $"{Mail.Replace(p.Item.DisplayName, "…")} -> {p.Group ?? p.Kind}{(p.Why.Length > 0 ? $" ({p.Why})" : "")}"));

    public const int ClassifierVersion = 4;
    public event Action<int>? Regrouped;

    // Once per version of the rules: items this app placed in a kind's group move to the group the rules (or what
    // the user taught) now give them, in one undo step. Hand placements, groups the user built, and moves to a
    // catch-all are skipped.
    public async Task<int> RegroupAsync()
    {
        try { return await RegroupCoreAsync(); }
        catch (Exception ex)
        {
            Log.Error("auto-regroup", ex);
            return 0;
        }
    }

    private async Task<int> RegroupCoreAsync()
    {
        if (!Settings.QuickAutoGroup || Settings.QuickClassifier >= ClassifierVersion) return 0;
        var todo = Store.Items.Where(i => !i.GroupManual && Store.FindGroup(i.Group) is { Kind: not null })
            .Select(i => (Item: i, i.Path, Name: i.DisplayName, i.Group, Kind: Store.FindGroup(i.Group)!.Kind!)).ToList();
        var manual = ManualPlacements();
        var kinds = GroupKinds();
        var all = Store.Items.Select(i => (i.Path, i.DisplayName)).ToList();
        var sw = Stopwatch.StartNew();
        var moves = await Task.Run(() =>
        {
            // Every item's kind read now, so the renames that follow the moves need no further wait.
            foreach (var (p, n) in all) if (!_facts.ContainsKey(p)) _facts[p] = ReadFacts(p, n);
            var learned = Learn(manual, kinds);
            return todo.Select(t => (t.Item, t.Group, t.Kind, Pick: Pick(t.Path, t.Name, learned)))
                .Where(t => t.Pick.Group is { } g ? !string.Equals(g, t.Group, StringComparison.OrdinalIgnoreCase)
                    : t.Pick.Kind != t.Kind && !QuickCategories.IsCatchAll(t.Pick.Kind) && t.Pick.Kind.Length > 0)
                .Select(t => (Placement: new Placement(t.Item, t.Pick.Kind, t.Pick.Group), Why: $"{t.Group} -> {t.Pick.Group ?? t.Pick.Kind} ({t.Pick.Why})"))
                .ToList();
        });
        Settings.QuickClassifier = ClassifierVersion;
        if (moves.Count == 0)
        {
            Log.Write($"auto-regroup v{ClassifierVersion}: nothing to move ({todo.Count} checked, {sw.ElapsedMilliseconds}ms)");
            RefreshAutoNames();
            return 0;
        }
        int n = Store.Regroup(moves.Select(m => m.Placement).ToList(), QuickCategories.Order);
        Log.Write($"auto-regroup v{ClassifierVersion} {n}/{todo.Count} {sw.ElapsedMilliseconds}ms: "
                  + string.Join(", ", moves.Take(40).Select(m => $"{Mail.Replace(m.Placement.Item.DisplayName, "…")} {m.Why}")));
        RefreshAutoNames();
        if (n > 0) Regrouped?.Invoke(n);
        return n;
    }

    // Filled once in the background at start, so naming a new group normally never touches the disk.
    private void WarmFacts()
    {
        var todo = Store.Items.Where(i => !_facts.ContainsKey(i.Path)).Select(i => (i.Path, i.DisplayName)).ToList();
        if (todo.Count > 0) _ = Task.Run(() => { foreach (var (p, n) in todo) _facts.TryAdd(p, ReadFacts(p, n)); });
    }

    // Groups named from their members follow them after a move; a name the user typed stays. A kind's group
    // is named for the members of its kind (终端 when they are all terminals, 办公 when no learning app is in
    // 办公学习), any other from all its members. The members' kinds are read off the UI thread first; only the
    // newest pass renames.
    private int _namesPass;
    private void RefreshAutoNames() => _ = RefreshAutoNamesAsync();

    private async Task RefreshAutoNamesAsync()
    {
        try { await RenameByMembersAsync(); }
        catch (Exception ex) { Log.Error("group-names", ex); }
    }

    private async Task RenameByMembersAsync()
    {
        int pass = ++_namesPass;
        var todo = Store.Groups.Where(g => g.AutoNamed).SelectMany(g => Store.ItemsOf(g)).Where(i => !_facts.ContainsKey(i.Path))
            .Select(i => (i.Path, i.DisplayName)).ToList();
        if (todo.Count > 0)
            await Task.Run(() => { foreach (var (p, n) in todo) if (!_facts.ContainsKey(p)) _facts[p] = ReadFacts(p, n); });
        if (pass != _namesPass) return;
        foreach (var g in Store.Groups.Where(g => g.AutoNamed).ToList())
        {
            var members = Store.ItemsOf(g).ToList();
            if (members.Count == 0) continue;
            string want;
            if (g.Kind is { } kind)
            {
                var facts = members.Select(Facts).ToList();
                want = GroupNamer.ForKindGroup(kind, facts.Select(f => f.Kind).ToList(), facts.Select(f => f.Sub).ToList(),
                    name => Store.Groups.Any(o => o != g && GroupNamer.IsNameFor(o.Name, name)));
            }
            else want = SuggestName(members, g);
            if (GroupNamer.IsNameFor(g.Name, want)) continue;
            string to = Store.UniqueGroupName(want);
            Log.Write($"group-rename \"{g.Name}\" -> \"{to}\"");
            Store.RenameGroup(g, to, auto: true);
        }
    }

    // Debounced so a batch of additions is grouped in one pass.
    public void ScheduleAutoGroup()
    {
        if (!Settings.QuickAutoGroup || !Store.Items.Any(QuickStore.NeedsGroup)) return;
        _autoTimer.Stop();
        _autoTimer.Start();
    }

    // Only items that are ungrouped and were never placed by hand are touched; names, shortcut targets
    // and program descriptions are read off the UI thread.
    public async Task<int> AutoGroupAsync()
    {
        if (_autoRunning)
        {
            _autoAgain = true;
            return 0;
        }
        _autoRunning = true;
        try
        {
            var todo = Store.Items.Where(QuickStore.NeedsGroup).Select(i => (Item: i, i.Path, Name: i.DisplayName)).ToList();
            if (todo.Count == 0) return 0;
            var manual = ManualPlacements();
            var kinds = GroupKinds();
            var sw = Stopwatch.StartNew();
            var picks = await Task.Run(() =>
            {
                var learned = Learn(manual, kinds);
                return todo.Select(t => { var p = Pick(t.Path, t.Name, learned); return (t.Item, p.Kind, p.Group, p.Why); }).ToList();
            });
            if (!Settings.QuickAutoGroup) return 0;
            int n = Store.AutoAssign(picks.Select(p => new Placement(p.Item, p.Kind, p.Group)), QuickCategories.Order);
            // Every decision and its evidence: enough to tell from the log why an item went where it did.
            Log.Write($"auto-group {n}/{todo.Count} {sw.ElapsedMilliseconds}ms: {Describe(picks)}");
            if (n > 0) RefreshAutoNames();
            return n;
        }
        catch (Exception ex)
        {
            Log.Error("auto-group", ex);
            return 0;
        }
        finally
        {
            _autoRunning = false;
            if (_autoAgain)
            {
                _autoAgain = false;
                ScheduleAutoGroup();
            }
        }
    }

    // Groups in their order with their items, then the ungrouped items, each in the current sort.
    public List<(QuickGroup? Group, List<QuickItem> Items)> Sections()
    {
        var sorted = View("").ToList();
        var result = new List<(QuickGroup?, List<QuickItem>)>();
        foreach (var g in Store.Groups)
        {
            var list = sorted.Where(i => string.Equals(i.Group, g.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (list.Count > 0) result.Add((g, list));
        }
        var loose = sorted.Where(i => i.Group.Length == 0).ToList();
        if (loose.Count > 0) result.Add((null, loose));
        return result;
    }

    public IEnumerable<QuickItem> View(string filter)
    {
        var items = Store.Items;
        string f = filter.Trim();
        if (f.Length > 0)
            return items.Select(i => (i, s: Math.Max(Math.Max(Fuzzy.Score(f, i.DisplayName), Fuzzy.Score(f, i.Path)), i.Url is { } u ? Fuzzy.Score(f, u) : -1)))
                .Where(x => x.s >= 0).OrderByDescending(x => x.s).Select(x => x.i);
        long now = QuickStore.Now;
        return Settings.QuickSort switch
        {
            "Name" => items.OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            "Recent" => items.OrderByDescending(i => i.LastOpenedUtc).ThenByDescending(i => i.AddedUtc),
            "Frequent" => items.OrderByDescending(i => i.Score(now)).ThenByDescending(i => i.OpenCount),
            "Type" => items.OrderBy(i => Directory.Exists(i.Path) ? "" : Path.GetExtension(i.Path).ToLowerInvariant()).ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            _ => items,
        };
    }

    public static string TypeName(string path)
    {
        if (path.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase)) return "应用";
        if (PathEngine.IsShell(path)) return "系统位置";
        string t = path.TrimEnd('\\');
        if (t.Length == 2 && t[1] == ':') return "磁盘";
        if (Directory.Exists(path)) return "文件夹";
        string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "" => "文件",
            "lnk" => "快捷方式",
            "exe" => "应用程序",
            "url" => "网址",
            _ => ext.ToUpperInvariant(),
        };
    }
}
