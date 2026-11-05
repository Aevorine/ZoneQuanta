using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ZoneQuanta.Core.Quick;

public sealed class QuickItem
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public long AddedUtc { get; set; }
    public long LastOpenedUtc { get; set; }
    public int OpenCount { get; set; }
    public double Frecency { get; set; }

    public string DisplayName => Name.Length > 0 ? Name : QuickStore.DefaultName(Path);

    // Open score that halves every week: frequent and recent beats frequent long ago.
    public double Score(long nowUtc) =>
        Frecency * Math.Pow(2, -Math.Max(0, nowUtc - LastOpenedUtc) / QuickStore.HalfLifeSeconds);
}

public sealed class QuickData
{
    public List<QuickItem> Items { get; set; } = new();
    public List<string> History { get; set; } = new();
}

public sealed class QuickStore
{
    public const double HalfLifeSeconds = 7 * 86400;
    private const int HistoryLimit = 24;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;

    public QuickData Data { get; }
    public IReadOnlyList<QuickItem> Items => Data.Items;
    public event Action<QuickItem?>? Changed;

    public QuickStore(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZoneQuanta", "quick.json");
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
        var item = new QuickItem { Path = Normalize(path), AddedUtc = Now };
        if (index < 0 || index > Data.Items.Count) Data.Items.Add(item);
        else Data.Items.Insert(index, item);
        Save(item);
        return (item, true);
    }

    public void Remove(QuickItem item)
    {
        if (Data.Items.Remove(item)) Save(null);
    }

    public void Move(QuickItem item, int index)
    {
        int from = Data.Items.IndexOf(item);
        if (from < 0) return;
        index = Math.Clamp(index, 0, Data.Items.Count - 1);
        if (from == index) return;
        Data.Items.RemoveAt(from);
        Data.Items.Insert(index, item);
        Save(item);
    }

    public void Rename(QuickItem item, string name)
    {
        string n = name.Trim();
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
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Json));
            File.Move(tmp, _path, true);
        }
        catch (IOException ex) { Log.Error("quick-save", ex); }
        catch (UnauthorizedAccessException ex) { Log.Error("quick-save", ex); }
        if (notify) Changed?.Invoke(changed);
    }

    private static QuickData Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<QuickData>(File.ReadAllText(path)) is { } d)
            {
                d.Items.RemoveAll(i => string.IsNullOrWhiteSpace(i.Path));
                return d;
            }
        }
        catch (JsonException ex) { Log.Error("quick-load", ex); }
        catch (IOException ex) { Log.Error("quick-load", ex); }
        return new QuickData();
    }
}
