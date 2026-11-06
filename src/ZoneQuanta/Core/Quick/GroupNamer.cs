using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZoneQuanta.Core.Quick;

// Names a group after what its members are for. One kind or a majority: the narrower kind they all share
// (终端, 电脑管家), the side of a two-part kind they all sit on (办公学习 holding only office apps is 办公),
// else the kind; when another group already has that name, what sets these members apart (narrower kind,
// maker, name start, a word every name contains). Two kinds covering two thirds: "A与B" when short, else the
// word that covers both (沟通, 创作, 工具...); otherwise the maker or name prefix they share, else "常用".
// Catch-all kinds (其他应用, 文件) give way to any specific kind and do not vote against it.
public static class GroupNamer
{
    public const string Fallback = "常用";
    private static readonly HashSet<string> CatchAll = new() { QuickCategories.OtherApps, QuickCategories.Files };

    // Kinds whose name joins two narrower ones, and which narrower kinds belong to each side.
    private static readonly (string Kind, string A, string[] ASubs, string B, string[] BSubs)[] Halves =
    {
        (QuickCategories.Office, "办公", new[] { "办公文档", "笔记", "日程待办" }, "学习", new[] { "学习" }),
        (QuickCategories.Network, "网络工具", new[] { "代理加速", "测速传输" }, "远程", new[] { "远程" }),
        (QuickCategories.Cloud, "网盘", new[] { "网盘" }, "下载", new[] { "下载" }),
        (QuickCategories.Engineering, "嵌入式", new[] { "嵌入式", "电路设计" }, "工程", new[] { "仿真计算", "机械设计" }),
    };

    // One word for two kinds whose joined name would be long (short ones read best joined: 设计与影音).
    private static readonly (string A, string B, string Name)[] Pairs =
    {
        (QuickCategories.Social, QuickCategories.Mail, "沟通"), (QuickCategories.Office, QuickCategories.Mail, "办公"),
        (QuickCategories.SystemTools, QuickCategories.Network, "工具"), (QuickCategories.SystemTools, QuickCategories.Cloud, "工具"),
        (QuickCategories.Network, QuickCategories.Cloud, "网络"), (QuickCategories.News, QuickCategories.Social, "社交"),
        (QuickCategories.Dev, QuickCategories.Engineering, "开发"), (QuickCategories.Design, QuickCategories.Engineering, "设计"),
        (QuickCategories.Office, QuickCategories.News, "阅读"),
    };

    // The name for members of one kind, from their narrower kinds ("" where unknown).
    public static string ForKind(string kind, IReadOnlyList<string> subs)
    {
        var known = subs.Where(x => x.Length > 0).ToList();
        if (known.Count >= 2 && known.Distinct().Count() == 1 && known.Count * 3 >= subs.Count * 2) return known[0];
        foreach (var h in Halves)
            if (h.Kind == kind && known.Count > 0 && known.Count * 2 >= subs.Count)
            {
                if (known.All(x => h.ASubs.Contains(x))) return h.A;
                if (known.All(x => h.BSubs.Contains(x))) return h.B;
            }
        return kind;
    }

    // The bin automatic grouping fills with one kind: named for the members of that kind (items of other kinds
    // dropped in by hand do not count); a narrower name another group already has falls back to the kind.
    public static string ForKindGroup(string kind, IReadOnlyList<string> kinds, IReadOnlyList<string> subs, Func<string, bool> taken)
    {
        var mine = Enumerable.Range(0, kinds.Count).Where(i => kinds[i] == kind).Select(i => i < subs.Count ? subs[i] : "").ToList();
        if (mine.Count == 0) return kind;
        string name = ForKind(kind, mine);
        return name != kind && taken(name) ? kind : name;
    }
    private static readonly Regex Marks = new(@"[®©™]|\b(?:Corporation|Corp|Inc|Ltd|LLC|GmbH|Limited|Co|s\.r\.o|Operating System)\b\.?,?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Suggest(IReadOnlyList<string> kinds, IReadOnlyList<string> names, IReadOnlyList<string> makers,
        IReadOnlyList<string>? subs = null, Func<string, bool>? taken = null)
    {
        if (kinds.Count == 0) return Fallback;
        taken ??= _ => false;
        var ranked = kinds.Where(k => k.Length > 0).GroupBy(k => k)
            .Select(g => (Kind: g.Key, N: g.Count()))
            .OrderByDescending(x => x.N).ThenBy(x => Rank(x.Kind)).ToList();
        var specific = ranked.Where(x => !CatchAll.Contains(x.Kind)).ToList();
        int voters = specific.Sum(x => x.N);
        if (specific.Count > 0 && specific[0].N * 2 > voters)
        {
            string kind = specific[0].Kind;
            var mine = Enumerable.Range(0, kinds.Count).Where(i => kinds[i] == kind).Select(i => subs is not null && i < subs.Count ? subs[i] : "").ToList();
            string name = ForKind(kind, mine);
            if (!taken(name)) return name;
            return SetApart(kind, kinds, subs, names, makers, taken) ?? (!taken(kind) ? kind : name);
        }
        if (specific.Count >= 2 && (specific[0].N + specific[1].N) * 3 >= voters * 2 && Join(specific[0].Kind, specific[1].Kind) is { } joined && !taken(joined))
            return joined;
        if (Shared(makers) is { } maker) return maker;
        if (SharedPrefix(names) is { } prefix) return prefix;
        if (specific.Count == 0 && ranked.Count == 1) return ranked[0].Kind;
        return Fallback;
    }

    // "AI与开发" when short and plain, else the word that covers both, else nothing.
    private static string? Join(string a, string b)
    {
        if (a.Length + b.Length <= 5 && !a.Contains('与') && !b.Contains('与')) return $"{a}与{b}";
        foreach (var p in Pairs)
            if ((p.A == a && p.B == b) || (p.A == b && p.B == a)) return p.Name;
        return null;
    }

    // A name for members of one kind that another group already uses: their own narrower kind when most
    // share it, else the maker, the start of their names or a word all their names contain.
    private static string? SetApart(string kind, IReadOnlyList<string> kinds, IReadOnlyList<string>? subs, IReadOnlyList<string> names,
        IReadOnlyList<string> makers, Func<string, bool> taken)
    {
        if (subs is not null && subs.Count == kinds.Count)
        {
            var mine = Enumerable.Range(0, kinds.Count).Where(i => kinds[i] == kind).Select(i => subs[i]).ToList();
            var top = mine.Where(s => s.Length > 0).GroupBy(s => s).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (top is not null && top.Count() * 2 > mine.Count && !taken(top.Key)) return top.Key;
        }
        foreach (string? candidate in new[] { Shared(makers), SharedPrefix(names), CommonWord(names) })
            if (candidate is not null && !taken(candidate)) return candidate;
        return null;
    }

    // The longest run of two or more Chinese characters every name contains ("华为电脑管家", "腾讯电脑管家" -> "电脑管家").
    private static string? CommonWord(IReadOnlyList<string> names)
    {
        if (names.Count < 2) return null;
        string shortest = names.OrderBy(n => n.Length).First();
        for (int len = shortest.Length; len >= 2; len--)
            for (int at = 0; at + len <= shortest.Length; at++)
            {
                string w = shortest.Substring(at, len);
                if (w.All(c => c >= 0x4E00 && c <= 0x9FFF) && names.All(n => n.Contains(w, StringComparison.Ordinal))) return w;
            }
        return null;
    }

    // The group already has this name, or this name plus the number that kept it unique ("浏览器 2");
    // a number that is part of the name itself ("Microsoft 365") is not taken for that suffix.
    public static bool IsNameFor(string groupName, string want)
    {
        string g = groupName.Trim();
        return string.Equals(g, want, StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(g, "^" + Regex.Escape(want) + @"\s+\d+$", RegexOptions.IgnoreCase);
    }

    private static int Rank(string kind)
    {
        for (int i = 0; i < QuickCategories.Order.Count; i++)
            if (QuickCategories.Order[i] == kind) return i;
        return int.MaxValue;
    }

    // The same maker for every member ("Adobe", "JetBrains"), long product names cut to their brand word.
    private static string? Shared(IReadOnlyList<string> makers)
    {
        if (makers.Count < 2) return null;
        var clean = makers.Select(m => Regex.Replace(Marks.Replace(m ?? "", " "), @"\s+", " ").Trim()).ToList();
        if (clean.Any(m => m.Length < 2) || clean.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1) return null;
        string s = clean[0];
        return s.Length > 16 ? s.Split(' ')[0] : s;
    }

    // Longest common start of the names, ending on a word boundary for Latin names ("Adobe", "网易").
    private static string? SharedPrefix(IReadOnlyList<string> names)
    {
        if (names.Count < 2) return null;
        string first = names[0].Trim();
        int len = first.Length;
        foreach (var n in names.Skip(1))
        {
            string t = n.Trim();
            int i = 0;
            while (i < len && i < t.Length && char.ToLowerInvariant(first[i]) == char.ToLowerInvariant(t[i])) i++;
            len = i;
        }
        string p = first[..len];
        if (len < first.Length && len > 0 && char.IsAsciiLetterOrDigit(first[len]) && char.IsAsciiLetterOrDigit(p[^1]))
        {
            int cut = p.LastIndexOfAny(new[] { ' ', '-', '_', '.' });
            p = cut > 0 ? p[..cut] : "";
        }
        p = p.TrimEnd(' ', '-', '_', '.', '(', '（', '·');
        return p.Length >= 2 ? p : null;
    }
}
