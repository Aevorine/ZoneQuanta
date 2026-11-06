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
        _autoTimer.Tick += (_, _) =>
        {
            _autoTimer.Stop();
            _ = AutoGroupAsync();
        };
        store.Changed += _ => ScheduleAutoGroup();
        WarmFacts();
        settings.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppSettings.QuickAutoGroup)) ScheduleAutoGroup(); };
    }

    public QuickStore Store { get; }
    public AppSettings Settings { get; }
    public QuickItem? Highlight { get; private set; }

    // A group just made from two items; the view starts renaming it once it is on screen.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Kind, string Maker)> _facts = new(StringComparer.OrdinalIgnoreCase);
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
        string want = SuggestName(members);
        if (!GroupNamer.IsNameFor(group.Name, want)) Store.RenameGroup(group, Store.UniqueGroupName(want), auto: true);
        else Store.MarkAutoNamed(group);
    }
    public void RenameGroup(QuickGroup group, string name) => Store.RenameGroup(group, name);
    public void Dissolve(QuickGroup group) => Store.Dissolve(group);
    public void SetExpanded(QuickGroup group, bool expanded) => Store.SetExpanded(group, expanded);
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

    public string SuggestName(IReadOnlyCollection<QuickItem> items)
    {
        var facts = items.Select(Facts).ToList();
        return GroupNamer.Suggest(facts.Select(f => f.Kind).ToList(), items.Select(i => i.DisplayName).ToList(), facts.Select(f => f.Maker).ToList());
    }

    // What an item is for and who makes it, cached per path. Read off the UI thread: a shortcut on an
    // offline network drive gets 200 ms, then counts as unknown while the read finishes in the background.
    private (string Kind, string Maker) Facts(QuickItem item)
    {
        if (_facts.TryGetValue(item.Path, out var f)) return f;
        string path = item.Path, name = item.DisplayName;
        var read = Task.Run(() => _facts[path] = ReadFacts(path, name));
        return read.Wait(200) ? read.Result : ("", "");
    }

    private static (string Kind, string Maker) ReadFacts(string path, string name)
    {
        var hint = ShellLinks.Describe(path);
        return (QuickCategories.Classify(path, name, hint), hint?.Company ?? "");
    }

    // Filled once in the background at start, so naming a new group normally never touches the disk.
    private void WarmFacts()
    {
        var todo = Store.Items.Where(i => !_facts.ContainsKey(i.Path)).Select(i => (i.Path, i.DisplayName)).ToList();
        if (todo.Count > 0) _ = Task.Run(() => { foreach (var (p, n) in todo) _facts.TryAdd(p, ReadFacts(p, n)); });
    }

    // Groups named from their members follow them after a hand move; a name the user typed stays.
    private void RefreshAutoNames()
    {
        foreach (var g in Store.Groups.Where(g => g.AutoNamed).ToList())
        {
            var members = Store.ItemsOf(g).ToList();
            if (members.Count == 0) continue;
            string want = SuggestName(members);
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
            var sw = Stopwatch.StartNew();
            var picks = await Task.Run(() => todo.Select(t => (t.Item, QuickCategories.Classify(t.Path, t.Name, ShellLinks.Describe(t.Path)))).ToList());
            if (!Settings.QuickAutoGroup) return 0;
            int n = Store.AutoAssign(picks, QuickCategories.Order);
            Log.Write($"auto-group {n}/{todo.Count} {sw.ElapsedMilliseconds}ms");
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
            return items.Select(i => (i, s: Math.Max(Fuzzy.Score(f, i.DisplayName), Fuzzy.Score(f, i.Path))))
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
