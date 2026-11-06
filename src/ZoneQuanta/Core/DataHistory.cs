using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ZoneQuanta.Core;

public sealed record Snapshot(string File, DateTime Time, string Reason, string Summary, string Hash);

// Versions of settings.json and quick.json to go back to. A version is kept only when something the user
// arranged changed (window sizes, open counts and the like do not count), compressed, with a short summary
// of what changed. Kept: the newest 60, plus the last one of each day for 30 days.
public static class DataHistory
{
    private const int KeepRecent = 60, KeepDays = 30;
    private static readonly string[] Names = { "settings.json", "quick.json" };
    private static readonly HashSet<string> VolatileSettings = new() { "QuickBounds", "PanelBounds", "PanelPage", "QuotaAlerted", "QuickWidth", "QuickHeight" };
    private static readonly HashSet<string> VolatileItem = new() { "LastOpenedUtc", "OpenCount", "Frecency", "DisplayName" };
    private static readonly object Gate = new();
    private static string? _lastHash;

    private static string Dir => DataPaths.File("history");

    private sealed class Bundle
    {
        public DateTime Time { get; set; }
        public string Reason { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Hash { get; set; } = "";
        public Dictionary<string, string> Files { get; set; } = new();
    }

    // Any thread. True when a new version was written.
    public static bool Take(string reason)
    {
        lock (Gate)
        {
            try
            {
                var files = ReadCurrent();
                if (files.Count == 0) return false;
                string hash = HashOf(files);
                Bundle? last = null;
                if (_lastHash is null)
                {
                    last = Latest();
                    _lastHash = last?.Hash ?? "";
                }
                if (hash == _lastHash) return false;
                last ??= Latest();
                string summary = last is null ? "首个版本" : Summarize(last.Files, files);
                var b = new Bundle { Time = DateTime.Now, Reason = reason, Summary = summary, Hash = hash, Files = files };
                Directory.CreateDirectory(Dir);
                string path = Path.Combine(Dir, $"{b.Time:yyyyMMdd-HHmmss-fff}.json.gz");
                using (var fs = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write))
                using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
                    JsonSerializer.Serialize(gz, b);
                File.Move(path + ".tmp", path, overwrite: true);
                _lastHash = hash;
                Prune();
                Log.Write($"history: {reason} · {summary}");
                Taken?.Invoke();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                Log.Error("history", ex);
                return false;
            }
        }
    }

    // Raised on the taking thread after a new version was written.
    public static event Action? Taken;

    // Versions, newest first, and the hash of the files as they are on disk now (the "current" row matches it).
    public static (List<Snapshot> List, string Current) List()
    {
        var list = new List<Snapshot>();
        lock (Gate)
        {
            foreach (string f in Files())
                if (Read(f) is { } b) list.Add(new Snapshot(f, b.Time, b.Reason, b.Summary, b.Hash));
            string current = "";
            try { current = HashOf(ReadCurrent()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Log.Error("history-current", ex); }
            return (list, current);
        }
    }

    // Shares read, write and delete: a snapshot must never make the app's own save of these files fail.
    private static Dictionary<string, string> ReadCurrent()
    {
        var files = new Dictionary<string, string>();
        foreach (string n in Names)
        {
            string p = DataPaths.File(n);
            if (!File.Exists(p)) continue;
            using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            files[n] = r.ReadToEnd();
        }
        return files;
    }

    // Writes the version's files back. The caller restarts the app so every part reads them fresh.
    public static bool Restore(Snapshot s)
    {
        lock (Gate)
        {
            if (Read(s.File) is not { } b) return false;
            foreach (var (name, text) in b.Files)
                if (Array.IndexOf(Names, name) >= 0) DurableFile.Write(DataPaths.File(name), text);
            _lastHash = null;
            Log.Write($"history: restored {Path.GetFileName(s.File)} ({s.Summary})");
            return true;
        }
    }

    private static IEnumerable<string> Files() =>
        Directory.Exists(Dir) ? Directory.GetFiles(Dir, "*.json.gz").OrderByDescending(f => f, StringComparer.Ordinal) : Enumerable.Empty<string>();

    private static Bundle? Latest() => Files().Select(Read).FirstOrDefault(b => b is not null);

    private static Bundle? Read(string file)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<Bundle>(gz);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Log.Error("history-read", ex);
            return null;
        }
    }

    private static void Prune()
    {
        var all = Files().ToList();
        var keepDays = new HashSet<string>();
        var cutoff = DateTime.Now.Date.AddDays(-KeepDays);
        for (int i = 0; i < all.Count; i++)
        {
            string name = Path.GetFileName(all[i]);
            string day = name.Length >= 8 ? name[..8] : name;
            bool recent = i < KeepRecent;
            bool dailyKeeper = keepDays.Add(day) && DateTime.TryParseExact(day, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d) && d >= cutoff;
            if (recent || dailyKeeper) continue;
            try { File.Delete(all[i]); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error("history-prune", ex); }
        }
    }

    private static string HashOf(Dictionary<string, string> files)
    {
        var sb = new StringBuilder();
        foreach (string n in Names)
            if (files.TryGetValue(n, out var t)) sb.Append(n).Append('\n').Append(Meaningful(n, t)?.ToJsonString() ?? t).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    // The file without what changes on its own (window places, open counts, the last page shown).
    private static JsonNode? Meaningful(string name, string text)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(text); }
        catch (JsonException) { return null; }
        if (root is not JsonObject o) return root;
        if (name == "settings.json")
        {
            foreach (string k in VolatileSettings) o.Remove(k);
        }
        else
        {
            o.Remove("History");
            o.Remove("StartSeen");
            if (o["Items"] is JsonArray items)
                foreach (var i in items.OfType<JsonObject>())
                    foreach (string k in VolatileItem) i.Remove(k);
            if (o["Groups"] is JsonArray groups)
                foreach (var g in groups.OfType<JsonObject>()) g.Remove("Expanded");
        }
        return o;
    }

    private static readonly (string Prefix, string Label)[] Labels =
    {
        ("NavOrder", "导航顺序"), ("NavCollapsed", "导航收起"), ("Layouts", "页面布局"), ("Zones", "时区"), ("Theme", "主题"),
        ("Scale", "缩放"), ("Opacity", "不透明度"), ("Left", "时钟位置"), ("Top", "时钟位置"), ("Pin", "时钟位置"),
        ("WidgetVisible", "显示时钟"), ("ClickThrough", "鼠标穿透"), ("LockPosition", "锁定位置"), ("Topmost", "总是置顶"),
        ("HideOnFullscreen", "全屏时隐藏"), ("AllowOffScreen", "超出屏幕"), ("AutoStart", "开机自启"),
        ("Use24Hour", "时间格式"), ("Show", "时间格式"), ("Animate", "时间格式"), ("Vertical", "排列方向"),
        ("QuickHotkey", "快捷键"), ("PathHotkey", "快捷键"), ("Hotkey", "快捷键"), ("LocalKeys", "面板快捷键"),
        ("AutoCheckUpdate", "更新"), ("AutoInstallUpdate", "更新"), ("UseMirrors", "更新"),
        ("Band", "监控条"), ("SampleIntervalMs", "刷新频率"), ("Speed", "网速单位"), ("TrackApps", "应用流量"),
        ("Traffic", "流量页"), ("AppSort", "流量页"), ("AppGroupBy", "流量页"), ("Quota", "流量额度"), ("Quick", "快捷面板"),
    };

    private static string Summarize(Dictionary<string, string> before, Dictionary<string, string> after)
    {
        var parts = new List<string>();
        void Add(string s) { if (!parts.Contains(s)) parts.Add(s); }

        if (Meaningful("settings.json", before.GetValueOrDefault("settings.json", "{}")) is JsonObject a
            && Meaningful("settings.json", after.GetValueOrDefault("settings.json", "{}")) is JsonObject b)
        {
            foreach (var key in a.Select(kv => kv.Key).Union(b.Select(kv => kv.Key)))
                if (a[key]?.ToJsonString() != b[key]?.ToJsonString())
                    Add(Labels.FirstOrDefault(l => key.StartsWith(l.Prefix, StringComparison.Ordinal)).Label ?? key);
        }

        if (Meaningful("quick.json", before.GetValueOrDefault("quick.json", "{}")) is JsonObject qa
            && Meaningful("quick.json", after.GetValueOrDefault("quick.json", "{}")) is JsonObject qb)
        {
            var ia = Items(qa);
            var ib = Items(qb);
            int added = ib.Keys.Count(k => !ia.ContainsKey(k)), removed = ia.Keys.Count(k => !ib.ContainsKey(k));
            int moved = ib.Count(kv => ia.TryGetValue(kv.Key, out var o) && o.Group != kv.Value.Group);
            int renamed = ib.Count(kv => ia.TryGetValue(kv.Key, out var o) && o.Name != kv.Value.Name);
            if (moved > 0) Add($"分组调整 {moved} 项");
            if (added > 0) Add($"添加 {added} 项");
            if (removed > 0) Add($"移除 {removed} 项");
            if (renamed > 0) Add($"改名 {renamed} 项");
            var common = ib.Keys.Where(ia.ContainsKey).ToList();
            if (!common.SequenceEqual(ia.Keys.Where(ib.ContainsKey))) Add("排序");
            var ga = Groups(qa);
            var gb = Groups(qb);
            if (!ga.Keys.ToHashSet().SetEquals(gb.Keys)) Add("分组名称");
            if (gb.Any(kv => ga.TryGetValue(kv.Key, out var o) && (o.Col, o.Row) != (kv.Value.Col, kv.Value.Row))) Add("卡片位置");
            if (gb.Any(kv => ga.TryGetValue(kv.Key, out var o) && (o.Cols, o.Rows) != (kv.Value.Cols, kv.Value.Rows))) Add("卡片大小");
        }
        if (parts.Count == 0) return "其他";
        return parts.Count <= 4 ? string.Join("、", parts) : string.Join("、", parts.Take(4)) + " 等";
    }

    private static Dictionary<string, (string Group, string Name)> Items(JsonObject q)
    {
        var d = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        if (q["Items"] is JsonArray arr)
            foreach (var i in arr.OfType<JsonObject>())
                if (i["Path"]?.GetValue<string>() is { } p) d.TryAdd(p, (i["Group"]?.GetValue<string>() ?? "", i["Name"]?.GetValue<string>() ?? ""));
        return d;
    }

    private static Dictionary<string, (string? Col, string? Row, string? Cols, string? Rows)> Groups(JsonObject q)
    {
        var d = new Dictionary<string, (string?, string?, string?, string?)>(StringComparer.OrdinalIgnoreCase);
        if (q["Groups"] is JsonArray arr)
            foreach (var g in arr.OfType<JsonObject>())
                if (g["Name"]?.GetValue<string>() is { } n)
                    d.TryAdd(n, (g["Col"]?.ToJsonString(), g["Row"]?.ToJsonString(), g["Cols"]?.ToJsonString(), g["Rows"]?.ToJsonString()));
        return d;
    }
}
