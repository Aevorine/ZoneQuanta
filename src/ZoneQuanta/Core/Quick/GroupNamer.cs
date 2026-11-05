using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZoneQuanta.Core.Quick;

// Names a hand-made group after what its members are for. One kind or a majority: that kind; two kinds
// covering two thirds: "A与B"; otherwise the maker or name prefix they share, else "常用". Catch-all kinds
// (其他应用, 文件) give way to any specific kind and do not vote against it.
public static class GroupNamer
{
    public const string Fallback = "常用";
    private static readonly HashSet<string> CatchAll = new() { QuickCategories.OtherApps, QuickCategories.Files };
    private static readonly Regex Marks = new(@"[®©™]|\b(?:Corporation|Corp|Inc|Ltd|LLC|GmbH|Limited|Co|s\.r\.o|Operating System)\b\.?,?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Suggest(IReadOnlyList<string> kinds, IReadOnlyList<string> names, IReadOnlyList<string> makers)
    {
        if (kinds.Count == 0) return Fallback;
        var ranked = kinds.Where(k => k.Length > 0).GroupBy(k => k)
            .Select(g => (Kind: g.Key, N: g.Count()))
            .OrderByDescending(x => x.N).ThenBy(x => Rank(x.Kind)).ToList();
        var specific = ranked.Where(x => !CatchAll.Contains(x.Kind)).ToList();
        int voters = specific.Sum(x => x.N);
        if (specific.Count > 0 && specific[0].N * 2 > voters) return specific[0].Kind;
        if (specific.Count >= 2 && (specific[0].N + specific[1].N) * 3 >= voters * 2) return $"{specific[0].Kind}与{specific[1].Kind}";
        if (Shared(makers) is { } maker) return maker;
        if (SharedPrefix(names) is { } prefix) return prefix;
        if (specific.Count == 0 && ranked.Count == 1) return ranked[0].Kind;
        return Fallback;
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
