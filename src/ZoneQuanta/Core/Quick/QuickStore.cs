using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZoneQuanta.Core;

namespace ZoneQuanta.Core.Quick;

public sealed class QuickItem
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public long AddedUtc { get; set; }
    public long LastOpenedUtc { get; set; }
    public int OpenCount { get; set; }
    public double Frecency { get; set; }

    // Group name, "" when ungrouped. GroupManual marks a placement the user made (including taking an
    // item out of its group): automatic grouping only ever fills items that have neither.
    public string Group { get; set; } = "";
    public bool GroupManual { get; set; }

    public string DisplayName => Name.Length > 0 ? Name : QuickStore.DefaultName(Path);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    // Open score that halves every week: frequent and recent beats frequent long ago.
    public double Score(long nowUtc) =>
        Frecency * Math.Pow(2, -Math.Max(0, nowUtc - LastOpenedUtc) / QuickStore.HalfLifeSeconds);
}

public sealed class QuickGroup
{
    public const int MaxCols = 8, MaxRows = 6;

    public string Name { get; set; } = "";
    public bool Expanded { get; set; }

    // Card size in icon cells, columns × rows, chosen by the user; 0 follows the default size.
    // Automatic grouping never sets or changes it.
    public int Cols { get; set; }
    public int Rows { get; set; }

    // Grid cell the user dropped the card on (null: flows in order); the latest drop wins an overlap.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Col { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Row { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long PinnedAt { get; set; }

    // Named from what its members are for; renamed with them until the user types a name.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AutoNamed { get; set; }

    // The kind automatic grouping files into this group (QuickCategories), kept when the user renames it.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

// Where automatic grouping puts an item: the group of its kind, or a group the user taught (Group).
public readonly record struct Placement(QuickItem Item, string Kind, string? Group = null);

public sealed class QuickData
{
    public List<QuickItem> Items { get; set; } = new();
    public List<QuickGroup> Groups { get; set; } = new();
    public List<string> History { get; set; } = new();
    public List<string> StartSeen { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class QuickStore
{
    public const double HalfLifeSeconds = 7 * 86400;
    private const int HistoryLimit = 24;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;

    private const int UndoLimit = 50;
    // Each step keeps when it was taken: items Start sync added later survive undo / redo.
    private readonly LinkedList<(string State, long At)> _undo = new(), _redo = new();
    private readonly Dictionary<string, long> _synced = new(StringComparer.OrdinalIgnoreCase);
    private long _lastCheckpoint;

    public QuickData Data { get; }
    public IReadOnlyList<QuickItem> Items => Data.Items;
    public IReadOnlyList<QuickGroup> Groups => Data.Groups;
    public event Action<QuickItem?>? Changed;
    public event Action? Saved;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public QuickStore(string? path = null)
    {
        _path = path ?? DataPaths.File("quick.json");
        Data = Load(_path);
    }

    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static string DefaultName(string path)
    {
        if (PathEngine.IsShell(path)) return PathEngine.ShellName(path);
        string trimmed = path.TrimEnd('\\', '/');
        if (trimmed.Length == 2 && trimmed[1] == ':') return char.ToUpperInvariant(trimmed[0]) + ":\\";
        string name = System.IO.Path.GetFileName(trimmed);
        if (name.Length == 0) return path;
        string ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
        return ext is ".lnk" or ".url" or ".exe" or ".appref-ms" ? System.IO.Path.GetFileNameWithoutExtension(name) : name;
    }

    public static string Normalize(string path)
    {
        string p = path.Trim().Trim('"');
        if (PathEngine.IsShell(p) || !System.IO.Path.IsPathFullyQualified(p)) return p;
        try { p = System.IO.Path.GetFullPath(p); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        if (p.Length > 3) p = p.TrimEnd('\\');
        return p;
    }

    public QuickItem? Find(string path)
    {
        string n = Normalize(path);
        foreach (var i in Data.Items)
            if (string.Equals(i.Path, n, StringComparison.OrdinalIgnoreCase)) return i;
        return null;
    }

    // Returns the item and whether it is new; an existing path is not duplicated.
    public (QuickItem Item, bool Added) Add(string path, int index = -1)
    {
        if (Find(path) is { } existing) return (existing, false);
        Checkpoint($"add {Normalize(path)}");
        var item = new QuickItem { Path = Normalize(path), AddedUtc = Now };
        if (index < 0 || index > Data.Items.Count) Data.Items.Add(item);
        else Data.Items.Insert(index, item);
        Save(item);
        return (item, true);
    }

    // Batch add with one save and one change notification.
    public List<QuickItem> AddRange(IEnumerable<(string Path, string? Name)> entries)
    {
        var added = new List<QuickItem>();
        foreach (var (path, name) in entries)
        {
            if (Find(path) is not null) continue;
            var item = new QuickItem { Path = Normalize(path), Name = name ?? "", AddedUtc = Now };
            Data.Items.Add(item);
            added.Add(item);
            _synced[item.Path] = Environment.TickCount64;
        }
        Save(added.Count > 0 ? added[0] : null, notify: added.Count > 0);
        return added;
    }

    public void Remove(QuickItem item)
    {
        if (!Data.Items.Contains(item)) return;
        Checkpoint($"remove \"{item.DisplayName}\"");
        Data.Items.Remove(item);
        Prune(Data);
        Save(null);
    }

    public static bool NeedsGroup(QuickItem item) => item.Group.Length == 0 && !item.GroupManual;

    public QuickGroup? FindGroup(string name)
    {
        foreach (var g in Data.Groups)
            if (string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)) return g;
        return null;
    }

    public IEnumerable<QuickItem> ItemsOf(QuickGroup group)
    {
        foreach (var i in Data.Items)
            if (string.Equals(i.Group, group.Name, StringComparison.OrdinalIgnoreCase)) yield return i;
    }

    // The group automatic grouping fills with this kind: the one bound to it, else an unbound group named
    // after it (adopted).
    public QuickGroup? KindGroup(string kind)
    {
        foreach (var g in Data.Groups)
            if (g.Kind == kind) return g;
        if (FindGroup(kind) is { Kind: null } named)
        {
            named.Kind = kind;
            return named;
        }
        return null;
    }

    public string UniqueGroupName(string wanted)
    {
        string name = wanted.Trim().Length > 0 ? wanted.Trim() : "新建分组";
        if (FindGroup(name) is null) return name;
        for (int n = 2; ; n++)
            if (FindGroup($"{name} {n}") is null) return $"{name} {n}";
    }

    // The user's placement: into a group (created when missing), or out of any group with "".
    public QuickGroup? SetGroup(IReadOnlyCollection<QuickItem> items, string group)
    {
        QuickGroup? target = null;
        string name = group.Trim();
        Checkpoint(name.Length > 0 ? $"group {items.Count} item(s) -> \"{name}\"" : $"ungroup {items.Count} item(s)");
        if (name.Length > 0)
        {
            target = FindGroup(name);
            if (target is null) Data.Groups.Add(target = new QuickGroup { Name = name });
        }
        foreach (var i in items)
        {
            i.Group = target?.Name ?? "";
            i.GroupManual = true;
        }
        Prune(Data);
        Save(items.Count > 0 ? items.First() : null);
        return target;
    }

    // Automatic grouping: applied only to items that are still ungrouped and were never placed by hand,
    // so a group the user or an earlier pass chose is never changed. New groups follow the given order.
    public int AutoAssign(IEnumerable<Placement> picks, IReadOnlyList<string> order)
    {
        var valid = picks.Where(p => p.Kind.Length > 0 && NeedsGroup(p.Item) && Data.Items.Contains(p.Item)).ToList();
        if (valid.Count == 0) return 0;
        int count = Apply(valid, order);
        Save(null);
        return count;
    }

    // Items this app placed, moved to the group better rules now give them: one undo step, and they stay
    // automatically placed. Items the user placed by hand are never moved.
    public int Regroup(IReadOnlyCollection<Placement> moves, IReadOnlyList<string> order)
    {
        var valid = moves.Where(p => p.Kind.Length > 0 && !p.Item.GroupManual && Data.Items.Contains(p.Item)).ToList();
        if (valid.Count == 0) return 0;
        Checkpoint($"auto-regroup {valid.Count} item(s)");
        int count = Apply(valid, order);
        Prune(Data);
        Save(null);
        return count;
    }

    // Into the learned group when it still exists, else the kind's group, created (named after the kind,
    // bound to it, named from its members from now on) when missing.
    private int Apply(List<Placement> picks, IReadOnlyList<string> order)
    {
        var created = new List<QuickGroup>();
        foreach (var p in picks)
        {
            var g = (p.Group is { Length: > 0 } learned ? FindGroup(learned) : null) ?? KindGroup(p.Kind);
            if (g is null)
            {
                g = new QuickGroup { Name = UniqueGroupName(p.Kind), Kind = p.Kind, AutoNamed = true };
                created.Add(g);
                Data.Groups.Add(g);
            }
            p.Item.Group = g.Name;
        }
        if (created.Count > 1)
        {
            int Rank(QuickGroup g) { int r = -1; for (int i = 0; i < order.Count; i++) if (order[i] == g.Kind) r = i; return r < 0 ? int.MaxValue : r; }
            Data.Groups.RemoveAll(created.Contains);
            Data.Groups.AddRange(created.OrderBy(Rank));
        }
        return picks.Count;
    }

    // Renaming onto an existing name merges the two groups. auto: a name derived from the members.
    public void RenameGroup(QuickGroup group, string name, bool auto = false)
    {
        string n = name.Trim();
        if (n.Length == 0 || n == group.Name || !Data.Groups.Contains(group)) return;
        Checkpoint($"rename-group \"{group.Name}\" -> \"{n}\"{(auto ? " (auto)" : "")}");
        group.AutoNamed = auto;
        var other = FindGroup(n);
        string old = group.Name;
        if (other is not null && other != group)
        {
            Data.Groups.Remove(group);
            other.Kind ??= group.Kind; // the merged group keeps receiving that kind
        }
        else group.Name = n;
        string final = other is not null && other != group ? other.Name : n;
        foreach (var i in Data.Items)
            if (string.Equals(i.Group, old, StringComparison.OrdinalIgnoreCase)) i.Group = final;
        Save(null);
    }

    // Items stay in the panel, ungrouped, and are not picked up again by automatic grouping.
    public void Dissolve(QuickGroup group)
    {
        Checkpoint($"dissolve \"{group.Name}\"");
        foreach (var i in ItemsOf(group).ToList())
        {
            i.Group = "";
            i.GroupManual = true;
        }
        Data.Groups.Remove(group);
        Save(null);
    }

    // Pins a card at a grid cell; the newest pin wins where pinned cards overlap.
    public void PlaceGroup(QuickGroup group, int col, int row)
    {
        if (!Data.Groups.Contains(group)) return;
        Checkpoint($"place \"{group.Name}\" at {col},{row}");
        group.Col = Math.Max(0, col);
        group.Row = Math.Max(0, row);
        group.PinnedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Save(null);
    }

    // One card (or all with null) back to flowing in order.
    public void Unpin(QuickGroup? group)
    {
        if (!Data.Groups.Any(g => g.Col is not null && (group is null || g == group))) return;
        Checkpoint(group is null ? "unpin all" : $"unpin \"{group.Name}\"");
        bool any = false;
        foreach (var g in Data.Groups)
        {
            if ((group is not null && g != group) || g.Col is null) continue;
            g.Col = g.Row = null;
            g.PinnedAt = 0;
            any = true;
        }
        if (any) Save(null);
    }

    public void MarkAutoNamed(QuickGroup group)
    {
        if (group.AutoNamed) return;
        group.AutoNamed = true;
        Save(null, notify: false);
    }

    public void SetExpanded(QuickGroup group, bool expanded)
    {
        if (group.Expanded == expanded) return;
        group.Expanded = expanded;
        Save(null, notify: false);
    }

    // 0 × 0 returns the card to the default size. Picking a size also folds the card back to it.
    public void SetSize(QuickGroup group, int cols, int rows)
    {
        cols = cols <= 0 || rows <= 0 ? 0 : Math.Min(cols, QuickGroup.MaxCols);
        rows = cols == 0 ? 0 : Math.Min(rows, QuickGroup.MaxRows);
        if (!Data.Groups.Contains(group) || (group.Cols == cols && group.Rows == rows && !group.Expanded)) return;
        Checkpoint($"size \"{group.Name}\" {cols}x{rows}");
        group.Cols = cols;
        group.Rows = rows;
        group.Expanded = false;
        Save(null);
    }

    // Every referenced group exists, and a group that lost its last item goes away.
    private static void Prune(QuickData d)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in d.Items) if (i.Group.Length > 0) used.Add(i.Group);
        d.Groups.RemoveAll(g => string.IsNullOrWhiteSpace(g.Name) || !used.Contains(g.Name));
        var known = new HashSet<string>(d.Groups.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var name in used)
            if (known.Add(name)) d.Groups.Add(new QuickGroup { Name = name });
    }

    public void Move(QuickItem item, int index)
    {
        int from = Data.Items.IndexOf(item);
        if (from < 0) return;
        index = Math.Clamp(index, 0, Data.Items.Count - 1);
        if (from == index) return;
        Checkpoint($"move \"{item.DisplayName}\" {from}->{index}");
        Data.Items.RemoveAt(from);
        Data.Items.Insert(index, item);
        Save(item);
    }

    public void Rename(QuickItem item, string name)
    {
        string n = name.Trim();
        Checkpoint($"rename \"{item.DisplayName}\" -> \"{n}\"");
        item.Name = n == DefaultName(item.Path) ? "" : n;
        Save(item);
    }

    public void Touch(QuickItem item)
    {
        long now = Now;
        item.Frecency = item.Score(now) + 1;
        item.LastOpenedUtc = now;
        item.OpenCount++;
        Save(item);
    }

    public void Remember(string path)
    {
        var h = Data.History;
        h.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
        h.Insert(0, path);
        if (h.Count > HistoryLimit) h.RemoveRange(HistoryLimit, h.Count - HistoryLimit);
        Save(null, notify: false);
    }

    public void Save(QuickItem? changed, bool notify = true)
    {
        if (!DataPaths.WritesFrozen)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                DurableFile.Write(_path, JsonSerializer.Serialize(Data, Json));
                Saved?.Invoke();
            }
            catch (IOException ex) { Log.Error("quick-save", ex); }
            catch (UnauthorizedAccessException ex) { Log.Error("quick-save", ex); }
        }
        if (notify) Changed?.Invoke(changed);
    }

    // Taken before each change the user makes. A change within 400 ms of the previous one (one gesture
    // and the renames it causes) joins the same undo step.
    private void Checkpoint(string what)
    {
        long now = Environment.TickCount64;
        if (_undo.Count == 0 || now - _lastCheckpoint > 400)
        {
            _undo.AddLast((JsonSerializer.Serialize(Data, Json), now));
            if (_undo.Count > UndoLimit) _undo.RemoveFirst();
            _redo.Clear();
        }
        _lastCheckpoint = now;
        Log.Write($"quick {what}");
    }

    public bool Undo() => Step(_undo, _redo, "undo");
    public bool Redo() => Step(_redo, _undo, "redo");

    // Brings back the layout of a step; open counts and recency stay as they are now.
    private bool Step(LinkedList<(string State, long At)> from, LinkedList<(string State, long At)> to, string what)
    {
        if (from.Count == 0) return false;
        var (state, at) = from.Last!.Value;
        from.RemoveLast();
        QuickData? d;
        try { d = JsonSerializer.Deserialize<QuickData>(state); }
        catch (JsonException ex)
        {
            Log.Error($"quick-{what}", ex);
            return false;
        }
        if (d is null) return false;
        to.AddLast((JsonSerializer.Serialize(Data, Json), Environment.TickCount64));
        var now = new Dictionary<string, QuickItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in Data.Items) now.TryAdd(i.Path, i);
        foreach (var i in d.Items)
        {
            i.Extra?.Remove(nameof(QuickItem.DisplayName));
            if (now.TryGetValue(i.Path, out var cur)) (i.LastOpenedUtc, i.OpenCount, i.Frecency) = (cur.LastOpenedUtc, cur.OpenCount, cur.Frecency);
        }
        var restored = new HashSet<string>(d.Items.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
        var keep = Data.Items.Where(i => !restored.Contains(i.Path) && _synced.TryGetValue(i.Path, out long t) && t > at).ToList();
        Data.Items.Clear();
        Data.Items.AddRange(d.Items);
        Data.Items.AddRange(keep);
        foreach (var i in keep)
            if (i.Group.Length > 0 && !d.Groups.Exists(g => string.Equals(g.Name, i.Group, StringComparison.OrdinalIgnoreCase))) i.Group = "";
        Data.Groups.Clear();
        Data.Groups.AddRange(d.Groups);
        Prune(Data);
        _lastCheckpoint = 0;
        Log.Write($"quick {what} ({from.Count} more{(keep.Count > 0 ? $", kept {keep.Count} synced" : "")})");
        Save(null);
        return true;
    }

    private static QuickData Load(string path)
    {
        var d = DurableFile.Read(path, t => JsonSerializer.Deserialize<QuickData>(t), "quick");
        if (d is null) return new QuickData();
        d.Items ??= new();
        d.Items.RemoveAll(i => string.IsNullOrWhiteSpace(i.Path));
        foreach (var i in d.Items)
        {
            i.Group = i.Group?.Trim() ?? "";
            i.Extra?.Remove(nameof(QuickItem.DisplayName)); // computed, never stored back
        }
        d.Groups ??= new();
        Prune(d);
        // Groups automatic grouping made before groups were bound to a kind carry the kind's name: bind them.
        // Their names follow their members only when nothing in them was placed by hand (else the user may have
        // typed that name).
        foreach (var g in d.Groups)
            if (g.Kind is null && QuickCategories.Order.Contains(g.Name) && !d.Groups.Any(o => o.Kind == g.Name))
            {
                g.Kind = g.Name;
                if (!d.Items.Any(i => i.GroupManual && string.Equals(i.Group, g.Name, StringComparison.OrdinalIgnoreCase))) g.AutoNamed = true;
            }
        return d;
    }
}
