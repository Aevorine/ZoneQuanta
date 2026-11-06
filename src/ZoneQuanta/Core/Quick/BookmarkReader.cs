using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZoneQuanta.Core.Quick;

// One link from a bookmark file: its title, address, the folders it was filed in (outermost first) and what the
// file says about its picture (an image address or inline bytes, a short label, a colour).
public sealed record Bookmark(string Title, string Url, IReadOnlyList<string> Folders, string? IconUrl = null, byte[]? IconData = null,
    string? Badge = null, string? Color = null);

// Reads links out of bookmark files: iTab backups (.itabdata), Chrome / Edge / Brave "Bookmarks", Firefox JSON
// backups, the bookmarks.html every browser exports, and any other JSON whose objects carry an address and a
// name (folders are the named objects that hold them). Only http(s) addresses are taken.
public static class BookmarkReader
{
    public const long MaxBytes = 32L * 1024 * 1024;

    // Containers every browser has; they say nothing about what a link is for.
    private static readonly HashSet<string> Roots = new(StringComparer.OrdinalIgnoreCase)
    {
        "bookmark_bar", "bookmarks bar", "书签栏", "other", "other bookmarks", "其他书签", "synced", "mobile bookmarks", "移动设备书签",
        "menu", "toolbar", "unfiled", "mobile", "书签菜单", "书签工具栏", "bookmarks menu", "bookmarks toolbar", "收藏夹栏", "favorites bar",
        "其他收藏夹", "other favorites", "favorites", "收藏夹", "bookmarks", "书签", "root", "roots", "navconfig", "children",
    };

    private static readonly string[] UrlKeys = { "url", "uri", "href", "link" };
    private static readonly string[] TitleKeys = { "name", "title", "text", "label" };
    private static readonly string[] IconKeys = { "src", "iconUrl", "iconuri", "icon", "favicon", "img", "image", "logo" };
    private static readonly string[] BadgeKeys = { "iconText", "shortName", "short" };
    private static readonly string[] ColorKeys = { "backgroundColor", "bgColor", "color" };

    // The format and the links; null when the file is not a bookmark file this reader knows.
    public static (string Format, List<Bookmark> Links)? Read(string file)
    {
        var info = new FileInfo(file);
        if (!info.Exists || info.Length == 0 || info.Length > MaxBytes) return null;
        string text = File.ReadAllText(file);
        string head = text.Length > 4096 ? text[..4096] : text;
        if (head.Contains("NETSCAPE-Bookmark-file", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(head, @"<dl\b", RegexOptions.IgnoreCase))
        {
            var html = FromHtml(text);
            return html.Count > 0 ? ("bookmarks.html", html) : null;
        }
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 256 });
            var root = doc.RootElement;
            string format = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("navConfig", out _) ? "iTab"
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("roots", out _) ? "Chromium"
                : text.Contains("x-moz-place", StringComparison.Ordinal) ? "Firefox" : "JSON";
            var links = new List<Bookmark>();
            // An iTab backup also keeps notes, stocks and settings: only its navigation holds links.
            Walk(format == "iTab" ? root.GetProperty("navConfig") : root, new List<string>(), links, 0);
            return links.Count > 0 ? (format, links) : null;
        }
        catch (JsonException) { return null; }
    }

    // Worth importing instead of adding as a file: an iTab backup, an exported bookmarks.html, a browser's
    // Bookmarks file or a JSON bookmark backup.
    public static bool LooksLikeBookmarks(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || info.Length == 0 || info.Length > MaxBytes) return false;
            string ext = info.Extension.ToLowerInvariant();
            if (ext == ".itabdata") return true;
            if (ext is not (".html" or ".htm" or ".json" or "")) return false;
            using var r = new StreamReader(file);
            var buf = new char[64 * 1024];
            string head = new(buf, 0, r.ReadBlock(buf, 0, buf.Length));
            // Only the shapes bookmark files really have: a browser's roots with its bookmark bar, iTab's navigation,
            // Firefox's place containers (a package.json with a "roots" setting is not one).
            return ext is ".html" or ".htm"
                ? head.Contains("NETSCAPE-Bookmark-file", StringComparison.OrdinalIgnoreCase)
                : head.TrimStart().StartsWith('{') && ((head.Contains("\"roots\"") && head.Contains("\"bookmark_bar\""))
                    || head.Contains("\"navConfig\"") || head.Contains("text/x-moz-place"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    private static void Walk(JsonElement e, List<string> folders, List<Bookmark> links, int depth)
    {
        if (depth > 64) return;
        if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in e.EnumerateArray()) Walk(x, folders, links, depth + 1);
            return;
        }
        if (e.ValueKind != JsonValueKind.Object) return;
        string title = First(e, TitleKeys);
        if (Address(First(e, UrlKeys)) is { } url)
        {
            string icon = First(e, IconKeys);
            byte[]? data = icon.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? DataUri(icon) : null;
            links.Add(new Bookmark(Clean(title), url, folders.ToArray(), Address(icon), data, NullIfEmpty(Clean(First(e, BadgeKeys))), NullIfEmpty(First(e, ColorKeys))));
            return;
        }
        // A named object that holds others is a folder; a browser's built-in containers are not counted.
        string name = Clean(title);
        bool folder = name.Length > 0 && !Roots.Contains(name) && e.EnumerateObject().Any(p => p.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object);
        if (folder) folders.Add(name);
        foreach (var p in e.EnumerateObject())
            if (p.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object) Walk(p.Value, folders, links, depth + 1);
        if (folder) folders.RemoveAt(folders.Count - 1);
    }

    private static string First(JsonElement e, string[] keys)
    {
        foreach (var k in keys)
            foreach (var p in e.EnumerateObject())
                if (string.Equals(p.Name, k, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                    return p.Value.GetString() ?? "";
        return "";
    }

    // <DT><H3>folder</H3><DL> ... </DL> nests; <DT><A HREF ICON>title</A> is a link. The toolbar folder an
    // export starts with is not counted.
    private static List<Bookmark> FromHtml(string html)
    {
        var links = new List<Bookmark>();
        var stack = new List<string?>();
        string? pending = null;
        foreach (Match m in Regex.Matches(html, @"<(?<close>/?)(?<tag>dl|h3|a)\b(?<attrs>[^>]*)>(?<text>[^<]*)", RegexOptions.IgnoreCase))
        {
            string tag = m.Groups["tag"].Value.ToLowerInvariant(), attrs = m.Groups["attrs"].Value;
            bool close = m.Groups["close"].Value.Length > 0;
            if (tag == "h3" && !close)
            {
                string name = Clean(WebUtility.HtmlDecode(m.Groups["text"].Value));
                bool builtIn = Regex.IsMatch(attrs, @"PERSONAL_TOOLBAR_FOLDER|UNFILED_BOOKMARKS_FOLDER", RegexOptions.IgnoreCase) || Roots.Contains(name);
                pending = builtIn ? "" : name;
            }
            else if (tag == "dl" && !close)
            {
                stack.Add(pending);
                pending = null;
            }
            else if (tag == "dl" && stack.Count > 0) stack.RemoveAt(stack.Count - 1);
            else if (tag == "a" && !close && Address(WebUtility.HtmlDecode(Attr(attrs, "href"))) is { } url)
            {
                string icon = Attr(attrs, "icon");
                byte[]? data = icon.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? DataUri(icon) : null;
                var folders = stack.Where(f => f is { Length: > 0 }).Select(f => f!).ToArray();
                links.Add(new Bookmark(Clean(WebUtility.HtmlDecode(m.Groups["text"].Value)), url, folders, Address(WebUtility.HtmlDecode(Attr(attrs, "icon_uri"))), data));
            }
        }
        return links;
    }

    private static string Attr(string attrs, string name) =>
        Regex.Match(attrs, $@"\b{name}\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase) is { Success: true } m
            ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value).Trim()
            : "";

    // An absolute http(s) address, normalised; anything else is not a link.
    public static string? Address(string raw)
    {
        string s = raw.Trim();
        if (s.Length == 0 || s.Length > 4096) return null;
        return Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && u.Host.Length > 0 ? u.AbsoluteUri : null;
    }

    private static byte[]? DataUri(string s)
    {
        int comma = s.IndexOf(',');
        if (comma < 0 || !s[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase) || s.Length - comma > 2_000_000) return null;
        try { return Convert.FromBase64String(s[(comma + 1)..]); }
        catch (FormatException) { return null; }
    }

    public const int MaxTitle = 200;

    // One line, single spaces, no control characters, at most MaxTitle characters (no title is longer in
    // practice, and every later pattern stays linear).
    public static string Clean(string s)
    {
        string t = s ?? "";
        if (t.Length > MaxTitle * 4) t = t[..(MaxTitle * 4)];
        t = Regex.Replace(Regex.Replace(t, @"[\p{Cc}\p{Cf}]", " "), @"\s+", " ").Trim();
        if (t.Length > MaxTitle) t = t[..(char.IsHighSurrogate(t[MaxTitle - 1]) ? MaxTitle - 1 : MaxTitle)].TrimEnd();
        return t;
    }

    private static string? NullIfEmpty(string s) => s.Length > 0 ? s : null;

    // A display name for a link: its title without the tagline after a separator ("闲鱼 - 闲不住？上闲鱼！" -> "闲鱼"),
    // when the title is long and the first part names something; the host when there is no title.
    public static string ShortTitle(string title, string url)
    {
        string t = Clean(title);
        // No title, or the address itself as the title (an export of a page without one): the host only, since
        // the full address may carry a token and the name ends up in the log.
        if (t.Length == 0 || t.Contains("://", StringComparison.Ordinal) || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            return Uri.TryCreate(url, UriKind.Absolute, out var u) ? (u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host) : "link";
        if (t.Length >= 12)
        {
            var parts = Regex.Split(t, @"\s+[-–—|·]+\s+|\s*[|｜]\s*|\s*——\s*|_|\s*[：:]\s*(?=\S)|\s+-\s*$");
            string head = parts[0].Trim(' ', '-', '–', '—', '|', '_');
            if (head.Length >= 2 && head.Length < t.Length) return head;
        }
        return t.Trim(' ', '-', '–', '—', '|', '_');
    }
}
