using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZoneQuanta.Core;
using ZoneQuanta.Core.Quick;
using ZoneQuanta.Platform;

namespace ZoneQuanta.UI.Quick;

// Imports bookmark files as quick items. Each link becomes an Internet shortcut under links\ (it opens in the
// default browser and remembers the folders it was filed in) with a picture drawn at once from its letters and
// colour, then replaced in the background by the best real icon found: the file's own image, else the site's
// icons. Links are filed by what they are (site, name, then folder names; a link nothing explains follows the
// kind most of its folder shares) into the panel's groups, one undo step; links already in the panel are skipped.
public sealed class QuickImport
{
    private readonly QuickActions _actions;
    private int _busy;

    public QuickImport(QuickActions actions) => _actions = actions;

    public static string LinksDir => DataPaths.File("links");
    private static string IconsDir => Path.Combine(LinksDir, "icons");

    // A message for the user when an import finishes ("导入 148 · 跳过 9").
    public event Action<string>? Finished;

    private sealed record Entry(string Path, string Name, string Url, string Icon, string Host, string? IconUrl, string Folders, string? Kind, string Why);
    private sealed record Plan(string Format, List<Entry> Entries, int Skipped, int Total, int Over, int Failed);

    // One import takes at most this many new links: a browser profile with thousands of bookmarks would bury the
    // panel and hold the disk and network for minutes; the rest can be imported from a smaller export.
    public const int MaxLinks = 800;

    // addIfNotBookmarks: a file that looked like a bookmark file but holds no links is added as a file instead.
    public async Task<int> RunAsync(string file, bool addIfNotBookmarks = false)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return 0;
        var sw = Stopwatch.StartNew();
        try
        {
            var known = _actions.Store.Items.Select(i => (i.Path, i.Url, i.DisplayName)).ToList();
            Plan? plan;
            try { plan = await Sta(() => Prepare(file, known)); }
            catch (Exception ex)
            {
                Log.Error("quick-import", ex);
                Finished?.Invoke("导入失败");
                return 0;
            }
            if (plan is null && addIfNotBookmarks)
            {
                Log.Write($"quick-import {Path.GetFileName(file)}: no links, added as a file");
                _actions.AddFile(file);
                return 0;
            }
            if (plan is null || plan.Entries.Count == 0)
            {
                Log.Write($"quick-import {Path.GetFileName(file)}: {(plan is null ? "not a bookmark file" : $"nothing new ({plan.Skipped} already here)")}");
                Finished?.Invoke(plan is null ? "没有可导入的链接" : $"已全部在面板中 · {plan.Skipped}");
                return 0;
            }
            var store = _actions.Store;
            var added = store.Import(plan.Entries.Select(e => (e.Path, e.Name, e.Url, (string?)e.Icon)).ToList(), plan.Format);
            var byPath = added.ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase);
            QuickItem? Of(Entry e) => byPath.TryGetValue(QuickStore.Normalize(e.Path), out var i) ? i : null;
            // Links nothing explains stay loose icons rather than filling a catch-all group.
            store.KeepLoose(plan.Entries.Where(e => e.Kind is null).Select(Of).OfType<QuickItem>().ToList());
            var picks = plan.Entries.Where(e => e.Kind is not null).Select(e => (Item: Of(e), e.Kind)).Where(p => p.Item is not null)
                .Select(p => new Placement(p.Item!, p.Kind!)).ToList();
            int grouped = store.AutoAssign(picks, QuickCategories.Order);
            _actions.Imported(added);
            int loose = added.Count(i => i.Group.Length == 0);
            var groups = added.Where(i => i.Group.Length > 0).GroupBy(i => i.Group).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}");
            Log.Write($"quick-import {plan.Format} {Path.GetFileName(file)}: {plan.Total} link(s), {added.Count} new, {plan.Skipped} already here, "
                      + $"{plan.Over} over the {MaxLinks} limit, {plan.Failed} unwritable, {grouped} grouped, {loose} loose, {sw.ElapsedMilliseconds}ms; "
                      + $"groups: {string.Join(", ", groups)}");
            Log.Write("quick-import why: " + string.Join(", ", plan.Entries.Take(240).Select(e => $"{e.Name} -> {e.Kind ?? "loose"} ({e.Why})")));
            Finished?.Invoke($"导入 {added.Count}" + (plan.Skipped > 0 ? $" · 跳过 {plan.Skipped}" : "") + (plan.Over > 0 ? $" · 超出上限 {plan.Over}" : ""));
            var fresh = plan.Entries.Where(e => Of(e) is not null).ToList();
            _ = Task.Run(() => UpgradeIcons(fresh));
            return added.Count;
        }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    // Background (STA, for drawing): read, drop duplicates, write the shortcuts and first pictures, classify.
    private static Plan? Prepare(string file, List<(string Path, string? Url, string Name)> known)
    {
        if (BookmarkReader.Read(file) is not { } read) return null;
        Directory.CreateDirectory(IconsDir);
        // What the panel already opens: links by their address, and web apps by their name (a site installed as an
        // app has no address to compare: "Kimi" the app is "Kimi" the bookmark).
        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var webNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, url, display) in known)
        {
            var hint = url is null && (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) ? ShellLinks.Describe(path) : null;
            if ((url ?? hint?.Url?.AbsoluteUri) is { } u) have.Add(Key(u));
            if (url is null && hint is { IsWebApp: true, Url: null }) webNames.Add(NameKey(display));
        }
        webNames.Remove("");
        var reuse = ExistingLinks();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<Entry>();
        int skipped = 0, over = 0, failed = 0;
        foreach (var b in read.Links)
        {
            if (!Uri.TryCreate(b.Url, UriKind.Absolute, out var uri)) continue;
            string name = BookmarkReader.ShortTitle(b.Title, b.Url);
            string key = Key(b.Url), landing = Landing(uri, name);
            if (have.Contains(key) || webNames.Contains(NameKey(name))) { skipped++; continue; }
            if (!seen.Add(key) || (landing.Length > 0 && !seen.Add(landing))) { skipped++; continue; }
            if (entries.Count >= MaxLinks) { over++; continue; }
            string folders = string.Join(" / ", b.Folders.Select(f => f.Replace('/', '／')));
            string host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            string icon = Path.Combine(IconsDir, Hash(key) + ".png");
            // One link that cannot be written (a locked file, a name the file system refuses) is skipped, not the import.
            try
            {
                string path = reuse.TryGetValue(key, out var old) ? old : NewLinkPath(name);
                WriteLink(path, uri.AbsoluteUri, folders, read.Format);
                if (!File.Exists(icon))
                {
                    if (b.IconData is { } bytes && WebIcons.Picture(bytes) is { } pic) WebIcons.SavePng(icon, pic.Pix, pic.W, pic.H);
                    else LinkIcons.Letter(icon, name, b.Badge, b.Color, host);
                }
                var e = QuickCategories.Explain(path, name, ShellLinks.Describe(path));
                entries.Add(new Entry(path, name, uri.AbsoluteUri, icon, host, b.IconUrl, folders, QuickCategories.IsCatchAll(e.Group) ? null : e.Group, e.Why));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                failed++;
                Log.Write($"quick-import skipped \"{name}\" ({host}): {ex.GetType().Name}");
            }
        }
        FollowFolders(entries);
        return new Plan(read.Format, entries, skipped, read.Links.Count, over, failed);
    }

    // A link nothing explains takes the kind most links filed beside it share: in its own folder, else in its
    // top folder, when that kind holds at least half of the explained ones there (and two or more).
    private static void FollowFolders(List<Entry> entries)
    {
        static string Top(string folders) => folders.Split(" / ")[0];
        // Each folder's majority is counted once (linear in the number of links).
        static Dictionary<string, string> Majorities(IEnumerable<Entry> all, Func<Entry, string> folderOf) =>
            all.Where(p => p.Folders.Length > 0).GroupBy(folderOf).Select(g =>
            {
                var known = g.Where(p => p.Kind is not null).ToList();
                var top = known.GroupBy(p => p.Kind!).OrderByDescending(k => k.Count()).FirstOrDefault();
                return (Folder: g.Key, Kind: top is not null && top.Count() >= 2 && top.Count() * 2 >= known.Count ? top.Key : null);
            }).Where(x => x.Kind is not null).ToDictionary(x => x.Folder, x => x.Kind!);
        var inFolder = Majorities(entries, p => p.Folders);
        var inTop = Majorities(entries, p => Top(p.Folders));
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.Kind is not null || e.Folders.Length == 0) continue;
            if (inFolder.TryGetValue(e.Folders, out var k1)) entries[i] = e with { Kind = k1, Why = $"most of \"{e.Folders}\", {e.Why}" };
            else if (inTop.TryGetValue(Top(e.Folders), out var k2)) entries[i] = e with { Kind = k2, Why = $"most of \"{Top(e.Folders)}\", {e.Why}" };
        }
    }

    // Background, after the links are in the panel: a sharper real icon replaces the drawn one when found.
    private void UpgradeIcons(List<Entry> entries)
    {
        if (!WebIcons.Enabled || entries.Count == 0) return;
        var sw = Stopwatch.StartNew();
        int own = 0, site = 0;
        var changed = new System.Collections.Concurrent.ConcurrentBag<string>();
        var best = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // 1. The image the bookmark file names (the picture the user chose there).
            Parallel.ForEach(entries.Where(e => e.IconUrl is not null), new ParallelOptions { MaxDegreeOfParallelism = 4 }, e =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                if (!Uri.TryCreate(e.IconUrl, UriKind.Absolute, out var u) || WebIcons.Picture(u, cts.Token) is not { } p || !IconLike(p.W, p.H)) return;
                WebIcons.SavePng(e.Icon, p.Pix, p.W, p.H);
                best[e.Icon] = Math.Min(p.W, p.H);
                changed.Add(e.Icon);
                Interlocked.Increment(ref own);
            });
            // 2. The site's own icons (manifest, <link rel=icon>, apple-touch-icon, favicon), host then its domain.
            foreach (bool parent in new[] { false, true })
            {
                var need = entries.Where(e => best.GetValueOrDefault(e.Icon) < 64).ToList();
                var webs = need.Select(e => (e, Web: AppBrands.Web.Make(parent ? QuickCategories.Registrable(e.Host) : e.Host, null)))
                    .Where(x => x.Web is not null).ToList();
                foreach (var batch in webs.Select(x => x.Web!).DistinctBy(w => w.Key).Chunk(16)) WebIcons.Fetch(batch, wait: true);
                foreach (var (e, web) in webs)
                {
                    if (WebIcons.Cached(web!) is not { } file || IconImages.Read(file) is not { } img || !IconLike(img.W, img.H)) continue;
                    int side = Math.Min(img.W, img.H);
                    if (side <= best.GetValueOrDefault(e.Icon)) continue;
                    File.Copy(file, e.Icon, overwrite: true);
                    best[e.Icon] = side;
                    changed.Add(e.Icon);
                    site++;
                }
            }
        }
        catch (Exception ex) { Log.Error("quick-import-icons", ex); }
        var files = changed.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Log.Write($"quick-import icons {entries.Count}: {own} own, {site} from sites, {entries.Count - files.Count} drawn, {sw.ElapsedMilliseconds}ms");
        if (files.Count > 0) _actions.RefreshIcons(files);
    }

    // At least 32 px and roughly square: a wide banner logo shrunk into a tile reads worse than the drawn letters.
    private static bool IconLike(int w, int h) => Math.Min(w, h) >= 32 && Math.Max(w, h) <= Math.Min(w, h) * 1.5;

    // Shortcuts already written under links\, by address: a re-import reuses them.
    private static Dictionary<string, string> ExistingLinks()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!Directory.Exists(LinksDir)) return map;
            foreach (var f in Directory.EnumerateFiles(LinksDir, "*.url"))
                if (ShellLinks.Describe(f)?.Url is { } u) map.TryAdd(Key(u.AbsoluteUri), f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error("quick-import-scan", ex); }
        return map;
    }

    private static string NewLinkPath(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name) sb.Append(Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c) ? ' ' : c);
        string stem = sb.ToString().Trim().TrimEnd('.');
        if (stem.Length == 0) stem = "link";
        if (stem.Length > 60) stem = stem[..(char.IsHighSurrogate(stem[59]) ? 59 : 60)].Trim();
        // Device names (CON, NUL, COM1 ...) are not file names, with or without an extension.
        if (System.Text.RegularExpressions.Regex.IsMatch(stem, @"^(con|prn|aux|nul|com\d|lpt\d)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) stem = "_" + stem;
        string path = Path.Combine(LinksDir, stem + ".url");
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(LinksDir, $"{stem} ({n}).url");
        return path;
    }

    // The address line stays ASCII (AbsoluteUri), so the shell opens it; the folders line is read back by ShellLinks.
    private static void WriteLink(string path, string url, string folders, string source)
    {
        Directory.CreateDirectory(LinksDir);
        string text = $"[InternetShortcut]\r\nURL={url}\r\n[ZoneQuanta]\r\nFolder={folders}\r\nSource={source}\r\n";
        File.WriteAllText(path + ".tmp", text, new UTF8Encoding(false));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    // The same address however it is written: no scheme, no www., no trailing slash, case-folded host.
    private static string Key(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url.Trim().ToLowerInvariant();
        string host = u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
        string path = u.AbsolutePath.TrimEnd('/');
        return (host.ToLowerInvariant() + (u.IsDefaultPort ? "" : ":" + u.Port) + path + u.Query + u.Fragment).TrimEnd('/', '#');
    }

    // A site's home page under one name (kimi.com and kimi.com/zh/ titled "Kimi" are one link).
    private static string Landing(Uri u, string name)
    {
        var parts = u.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        bool home = u.Query.Length == 0 && (parts.Length == 0 || (parts.Length == 1 && System.Text.RegularExpressions.Regex.IsMatch(parts[0], @"^[a-z]{2}([-_][a-zA-Z]{2,4})?$")));
        string host = u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
        return home ? "home:" + host.ToLowerInvariant() + "|" + name.Trim().ToLowerInvariant() : "";
    }

    // "ChatGPT-Web", "Gemini (Yuan)", "chatgpt" -> "chatgpt", "gemini": an app's account note and "Web" suffix dropped.
    private static string NameKey(string name)
    {
        string s = System.Text.RegularExpressions.Regex.Replace(name, @"[\(（\[【][^\)）\]】]*[\)）\]】]", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"[\s\-_]*web$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..20].ToLowerInvariant();

    private static Task<T> Sta<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true, Name = "QuickImport", Priority = ThreadPriority.BelowNormal };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return done.Task;
    }
}
