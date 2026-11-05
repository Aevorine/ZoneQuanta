using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ZoneQuanta.Core.Quick;

public enum TargetKind { None, File, Directory, Url, Shell, Missing }

public readonly record struct PathTarget(TargetKind Kind, string Value);

public sealed record Suggestion(string Title, string Detail, string Target, bool IsDirectory, QuickItem? Item = null);

// Turns typed or pasted text into something openable, and offers completions while typing.
public static class PathEngine
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly (string[] Names, string Target)[] Known =
    {
        (new[] { "桌面", "Desktop" }, "shell:Desktop"),
        (new[] { "下载", "Downloads" }, "shell:Downloads"),
        (new[] { "文档", "Documents" }, "shell:Personal"),
        (new[] { "图片", "Pictures" }, "shell:My Pictures"),
        (new[] { "音乐", "Music" }, "shell:My Music"),
        (new[] { "视频", "Videos" }, "shell:My Video"),
        (new[] { "主目录", "Home" }, "shell:Profile"),
        (new[] { "回收站", "Recycle Bin" }, "shell:RecycleBinFolder"),
        (new[] { "启动", "Startup" }, "shell:Startup"),
        (new[] { "此电脑", "This PC" }, "shell:MyComputerFolder"),
        (new[] { "AppData", "Roaming" }, "shell:AppData"),
        (new[] { "LocalAppData" }, "shell:Local AppData"),
        (new[] { "临时", "Temp" }, Path.GetTempPath().TrimEnd('\\')),
        (new[] { "Program Files" }, "shell:ProgramFiles"),
        (new[] { "System32" }, "shell:System"),
    };

    public static string Clean(string input)
    {
        string s = input.Trim();
        if (s.Length >= 2 && IsQuote(s[0]) && IsQuote(s[^1])) s = s[1..^1].Trim();
        if (s.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            s = Uri.UnescapeDataString(s[8..]);
        if (s == "~" || s.StartsWith("~\\", StringComparison.Ordinal) || s.StartsWith("~/", StringComparison.Ordinal))
            s = Home + s[1..];
        if (s.Contains('%')) s = Environment.ExpandEnvironmentVariables(s);
        if (!IsUrl(s) && !IsShell(s)) s = s.Replace('/', '\\');
        return s;
    }

    public static bool IsUrl(string s) =>
        s.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
        || (Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" or "ftp" or "mailto");

    public static bool IsShell(string s) =>
        s.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || s.StartsWith("::{", StringComparison.Ordinal);

    // Friendly name of a shell location, e.g. shell:Downloads -> 下载.
    public static string ShellName(string target)
    {
        foreach (var (names, t) in Known)
            if (t.Equals(target, StringComparison.OrdinalIgnoreCase)) return names[0];
        return target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ? target[6..] : target;
    }

    // Rough "is this meant as a path" test, used to decide between search and open.
    public static bool LooksLikePath(string input)
    {
        string s = input.Trim().Trim('"');
        return s.Length > 0 && (s.Contains('\\') || s.Contains('/') || s.Contains(':') || s[0] is '%' or '~');
    }

    public static PathTarget Resolve(string input)
    {
        string s = Clean(input);
        if (s.Length == 0) return new(TargetKind.None, s);
        if (IsUrl(s)) return new(TargetKind.Url, s.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + s : s);
        if (IsShell(s)) return new(TargetKind.Shell, s);
        if (s.Length == 2 && s[1] == ':') s += "\\";

        if (!Path.IsPathFullyQualified(s))
        {
            foreach (var (names, target) in Known)
                if (names.Any(n => n.Equals(s, StringComparison.OrdinalIgnoreCase)))
                    return new(IsShell(target) ? TargetKind.Shell : TargetKind.Directory, target);
            string rel = Path.Combine(Home, s);
            if (Directory.Exists(rel)) return new(TargetKind.Directory, rel);
            if (File.Exists(rel)) return new(TargetKind.File, rel);
            return new(TargetKind.Missing, s);
        }
        if (Directory.Exists(s)) return new(TargetKind.Directory, s);
        if (File.Exists(s)) return new(TargetKind.File, s);
        return new(TargetKind.Missing, s);
    }

    public static List<Suggestion> Suggest(string input, IReadOnlyList<QuickItem> items, IReadOnlyList<string> history, int limit, CancellationToken ct)
    {
        string s = Clean(input);
        if (s.Length == 0) return History(history, limit);
        if (IsUrl(s)) return new() { new(s, "", s, false) };
        if (s.Contains('\\') || (s.Length >= 2 && s[1] == ':')) return Complete(s, limit, ct);
        if (s.Length == 1 && char.IsLetter(s[0])) return Drives(s, limit).Concat(Search(s, items, history, limit)).Take(limit).ToList();
        return Search(s, items, history, limit);
    }

    private static List<Suggestion> History(IReadOnlyList<string> history, int limit)
    {
        var list = new List<Suggestion>();
        foreach (var h in history)
        {
            if (list.Count >= limit) break;
            bool dir = Directory.Exists(h);
            if (!dir && !File.Exists(h) && !IsShell(h)) continue;
            list.Add(new(QuickStore.DefaultName(h), Parent(h), h, dir));
        }
        return list;
    }

    private static IEnumerable<Suggestion> Drives(string letter, int limit)
    {
        foreach (var d in DriveInfo.GetDrives())
            if (d.Name.StartsWith(letter, StringComparison.OrdinalIgnoreCase))
                yield return new(d.Name, "", d.Name, true);
    }

    // Directory completion: the OS does the prefix filtering, so huge folders stay fast.
    private static List<Suggestion> Complete(string s, int limit, CancellationToken ct)
    {
        var result = new List<Suggestion>();
        int cut = s.LastIndexOf('\\');
        if (cut < 0)
        {
            string root = s.Length == 2 ? s + "\\" : s;
            if (Directory.Exists(root)) result.Add(new(root, "", root, true));
            return result;
        }
        string dir = s[..(cut + 1)], prefix = s[(cut + 1)..];
        if (!Directory.Exists(dir)) return result;

        var options = new EnumerationOptions { IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive, RecurseSubdirectories = false };
        var found = new List<(FileSystemInfo Info, bool Dir, int Rank)>();
        try
        {
            foreach (var info in new DirectoryInfo(dir).EnumerateFileSystemInfos(prefix + "*", options))
            {
                if (ct.IsCancellationRequested || found.Count >= 400) break;
                bool isDir = (info.Attributes & FileAttributes.Directory) != 0;
                int rank = info.Name.StartsWith(prefix, StringComparison.Ordinal) ? 0 : 1;
                found.Add((info, isDir, rank));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }

        foreach (var f in found.OrderBy(f => f.Rank).ThenBy(f => f.Dir ? 0 : 1).ThenBy(f => f.Info.Name.Length).ThenBy(f => f.Info.Name, StringComparer.OrdinalIgnoreCase).Take(limit))
            result.Add(new(f.Info.Name, dir.Length <= 3 ? dir : dir.TrimEnd('\\'), f.Info.FullName, f.Dir));
        return result;
    }

    private static List<Suggestion> Search(string q, IReadOnlyList<QuickItem> items, IReadOnlyList<string> history, int limit)
    {
        long now = QuickStore.Now;
        var scored = new List<(Suggestion S, double Score)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items)
        {
            int sc = Math.Max(Fuzzy.Score(q, i.DisplayName), Fuzzy.Score(q, Path.GetFileName(i.Path)));
            if (sc < 0 || !seen.Add(i.Path)) continue;
            scored.Add((new(i.DisplayName, Parent(i.Path), i.Path, Directory.Exists(i.Path), i), sc + 4 * Math.Log2(1 + i.Score(now)) + 6));
        }
        foreach (var (names, target) in Known)
        {
            int sc = names.Max(n => Fuzzy.Score(q, n));
            if (sc < 0 || !seen.Add(target)) continue;
            scored.Add((new(names[0], names.Length > 1 ? names[1] : "", target, true), sc));
        }
        foreach (var h in history)
        {
            int sc = Fuzzy.Score(q, QuickStore.DefaultName(h));
            if (sc < 0 || !seen.Add(h)) continue;
            scored.Add((new(QuickStore.DefaultName(h), Parent(h), h, Directory.Exists(h)), sc + 2));
        }
        return scored.OrderByDescending(x => x.Score).Take(limit).Select(x => x.S).ToList();
    }

    public static string Parent(string path)
    {
        if (IsShell(path) || IsUrl(path)) return "";
        try { return Path.GetDirectoryName(path.TrimEnd('\\')) ?? ""; }
        catch (ArgumentException) { return ""; }
    }

    private static bool IsQuote(char c) => c is '"' or '\'' or '“' or '”' or '‘' or '’';
}

// Subsequence match with bonuses for word starts and runs (the fzf idea, greedy variant).
public static class Fuzzy
{
    public static int Score(string query, string text)
    {
        if (query.Length == 0) return 0;
        if (text.Length < query.Length) return -1;
        int qi = 0, score = 0, last = -2;
        for (int i = 0; i < text.Length && qi < query.Length; i++)
        {
            if (char.ToLowerInvariant(text[i]) != char.ToLowerInvariant(query[qi])) continue;
            int bonus = 1;
            if (i == 0) bonus += 8;
            else if (IsBoundary(text[i - 1], text[i])) bonus += 6;
            if (last == i - 1) bonus += 4;
            score += bonus;
            last = i;
            qi++;
        }
        if (qi < query.Length) return -1;
        if (text.Contains(query, StringComparison.OrdinalIgnoreCase)) score += 10;
        return score - (text.Length - query.Length) / 8;
    }

    private static bool IsBoundary(char prev, char cur) =>
        prev is ' ' or '_' or '-' or '.' or '\\' or '/' || (char.IsLower(prev) && char.IsUpper(cur));
}
