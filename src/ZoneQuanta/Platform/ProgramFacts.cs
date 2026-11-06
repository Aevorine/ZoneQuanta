using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ZoneQuanta.Core;
using ZoneQuanta.Core.Quick;

namespace ZoneQuanta.Platform;

// Slower evidence of what a program is for, read only when its name and description settle nothing:
// the file types and link protocols Windows opens with it, what its product says about itself (Apps & features
// comments, an Electron app's package description) and the opening of a read-me shipped with it. Small reads
// with hard size limits, cached; any thread.
internal static class ProgramFacts
{
    private const int MaxReadme = 16 * 1024, MaxPackage = 64 * 1024, MaxAsarHeader = 8 * 1024 * 1024;
    private static readonly object Gate = new();
    private static Dictionary<string, HashSet<string>>? _types;
    private static long _builtAt;
    private static readonly ConcurrentDictionary<string, string> AboutCache = new(StringComparer.OrdinalIgnoreCase), ReadmeCache = new(StringComparer.OrdinalIgnoreCase);

    // Programs that only start others: what they open says nothing about a shortcut.
    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "rundll32.exe", "explorer.exe", "dllhost.exe", "openwith.exe", "cmd.exe", "wscript.exe", "cscript.exe", "mshta.exe", "javaw.exe",
        "java.exe", "python.exe", "pythonw.exe", "powershell.exe", "pwsh.exe", "msiexec.exe", "regedit.exe", "notepad.exe", "applicationframehost.exe",
    };

    public static TargetHint With(TargetHint hint)
    {
        string exe = hint.Target;
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return hint;
        return hint with { About = () => About(exe), Handles = () => Handles(exe), Readme = () => Readme(exe) };
    }

    // What the product says about itself.
    public static string About(string exe) => AboutCache.GetOrAdd(exe, e =>
    {
        var parts = new List<string>();
        if (InstalledPrograms.OwnerOf(e)?.About is { Length: > 0 } arp) parts.Add(arp);
        if (Electron(Path.GetDirectoryName(e) ?? "") is { Length: > 0 } pkg) parts.Add(pkg);
        return string.Join(" ", parts);
    });

    // File types (".md") and protocols ("mailto") registered to open with this program.
    public static IReadOnlyCollection<string> Handles(string exe)
    {
        string name = Path.GetFileName(exe);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Index().TryGetValue(name, out var t)) found.UnionWith(t);
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            foreach (string type in Values(hive, $@"Software\Classes\Applications\{name}\SupportedTypes"))
                if (QuickCategories.InformativeTypes.Contains(type, StringComparer.OrdinalIgnoreCase)) found.Add(type.ToLowerInvariant());
        return found;
    }

    // Reverse index: program file name -> informative types it opens. Built from each type's default handler,
    // its "open with" list and the registered applications' capabilities; rebuilt every ten minutes.
    private static Dictionary<string, HashSet<string>> Index()
    {
        lock (Gate)
        {
            if (_types is not null && Environment.TickCount64 - _builtAt < 600_000) return _types;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var commands = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            void Add(string? exe, string type)
            {
                if (string.IsNullOrEmpty(exe) || Hosts.Contains(exe)) return;
                if (!map.TryGetValue(exe, out var set)) map[exe] = set = new(StringComparer.OrdinalIgnoreCase);
                set.Add(type.ToLowerInvariant());
            }
            string? ProgId(string progId) => commands.TryGetValue(progId, out var c) ? c : commands[progId] = CommandExe(progId);
            try
            {
                foreach (string type in QuickCategories.InformativeTypes)
                {
                    if (type.StartsWith('.'))
                    {
                        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                        {
                            using var k = Open(hive, $@"Software\Classes\{type}");
                            if (k?.GetValue("") is string def && def.Length > 0) Add(ProgId(def), type);
                            foreach (string progId in Names(hive, $@"Software\Classes\{type}\OpenWithProgids")) Add(ProgId(progId), type);
                            foreach (string exe in SubKeys(hive, $@"Software\Classes\{type}\OpenWithList")) Add(exe, type);
                        }
                        string exts = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{type}";
                        foreach (string progId in Names(Registry.CurrentUser, exts + @"\OpenWithProgids")) Add(ProgId(progId), type);
                        foreach (string exe in Values(Registry.CurrentUser, exts + @"\OpenWithList")) if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) Add(exe, type);
                    }
                    else Add(ProgId(type), type); // a protocol is its own ProgID
                }
                // Registered applications declare their types and protocols with the ProgID that opens each.
                foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    using var apps = Open(hive, @"Software\RegisteredApplications");
                    foreach (string app in apps?.GetValueNames() ?? Array.Empty<string>())
                    {
                        if (apps!.GetValue(app) is not string caps || caps.Length == 0) continue;
                        foreach (string sub in new[] { "FileAssociations", "URLAssociations" })
                        {
                            using var k = Open(hive, caps + "\\" + sub);
                            foreach (string type in k?.GetValueNames() ?? Array.Empty<string>())
                                if (QuickCategories.InformativeTypes.Contains(type, StringComparer.OrdinalIgnoreCase) && k!.GetValue(type) is string progId)
                                    Add(ProgId(progId), type);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { Log.Error("program-types", ex); }
            _builtAt = Environment.TickCount64;
            Log.Write($"program-types {map.Count} program(s) {sw.ElapsedMilliseconds}ms");
            return _types = map;
        }
    }

    // File name of the program a ProgID's open command runs.
    private static string? CommandExe(string progId)
    {
        if (progId.Length == 0 || progId.Length > 200 || progId.Contains('\\')) return null;
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var shell = Open(hive, $@"Software\Classes\{progId}\shell");
            if (shell is null) continue;
            string verb = shell.GetValue("") is string v && v.Length > 0 && !v.Contains(' ') ? v : "open";
            using var cmd = shell.OpenSubKey(verb + @"\command") ?? shell.OpenSubKey(@"open\command");
            if (cmd?.GetValue("") is string line && ExeOf(line) is { } exe) return exe;
        }
        return null;
    }

    // "\"C:\x\app.exe\" \"%1\"" or C:\x\app.exe %1 -> app.exe.
    private static string? ExeOf(string line)
    {
        string t = Environment.ExpandEnvironmentVariables(line.Trim());
        string path = t.StartsWith('"') ? t[1..Math.Max(1, t.IndexOf('"', 1))]
            : Regex.Match(t, @"^.*?\.exe\b", RegexOptions.IgnoreCase) is { Success: true } m ? m.Value : t.Split(' ')[0];
        string file = Path.GetFileName(path.Trim());
        return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file : null;
    }

    // An Electron app's own description: resources\app\package.json, or package.json inside resources\app.asar.
    private static string Electron(string dir)
    {
        try
        {
            string loose = Path.Combine(dir, "resources", "app", "package.json");
            string? json = File.Exists(loose) && new FileInfo(loose).Length <= MaxPackage ? File.ReadAllText(loose) : AsarPackage(Path.Combine(dir, "resources", "app.asar"));
            if (json is null) return "";
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            var sb = new StringBuilder();
            foreach (string field in new[] { "description", "productName" })
                if (root.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String) sb.Append(v.GetString()).Append(' ');
            if (root.TryGetProperty("keywords", out var kw) && kw.ValueKind == JsonValueKind.Array)
                foreach (var k in kw.EnumerateArray().Take(20)) if (k.ValueKind == JsonValueKind.String) sb.Append(k.GetString()).Append(' ');
            string s = sb.ToString().Trim();
            return s.Length > 600 ? s[..600] : s;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException
                                       or FormatException or OverflowException) { return ""; }
    }

    // asar: 16-byte prefix (sizes), a JSON directory of the files with offsets, then the files.
    private static string? AsarPackage(string asar)
    {
        if (!File.Exists(asar)) return null;
        using var fs = new FileStream(asar, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var head = new byte[16];
        if (fs.Read(head, 0, 16) != 16) return null;
        uint headerSize = BitConverter.ToUInt32(head, 4), jsonLen = BitConverter.ToUInt32(head, 12);
        if (jsonLen == 0 || jsonLen > MaxAsarHeader || jsonLen + 8 > headerSize || headerSize > fs.Length) return null;
        var buf = new byte[jsonLen];
        int read = 0, n;
        while (read < buf.Length && (n = fs.Read(buf, read, buf.Length - read)) > 0) read += n;
        if (read != buf.Length) return null;
        using var dir = JsonDocument.Parse(buf);
        if (!dir.RootElement.TryGetProperty("files", out var files) || !files.TryGetProperty("package.json", out var pkg)
            || !pkg.TryGetProperty("size", out var size) || !pkg.TryGetProperty("offset", out var off)) return null;
        long length = size.GetInt64(), at = 8 + headerSize + long.Parse(off.GetString() ?? "-1", System.Globalization.CultureInfo.InvariantCulture);
        if (length <= 0 || length > MaxPackage || at < 8 + headerSize || at + length > fs.Length) return null;
        fs.Position = at;
        var data = new byte[length];
        read = 0;
        while (read < data.Length && (n = fs.Read(data, read, data.Length - read)) > 0) read += n;
        return read == data.Length ? Encoding.UTF8.GetString(data) : null;
    }

    // The opening of the read-me next to the program (or in its release-info / docs folder, or the only
    // Markdown file there): headings and the first paragraphs as plain text, links and code left out.
    public static string Readme(string exe) => ReadmeCache.GetOrAdd(Path.GetDirectoryName(exe) ?? "", dir =>
    {
        try
        {
            if (dir.Length <= 3 || !Directory.Exists(dir)) return "";
            foreach (string folder in new[] { dir, Path.Combine(dir, "release-info"), Path.Combine(dir, "docs"), Path.Combine(dir, "doc") })
            {
                if (!Directory.Exists(folder)) continue;
                var files = Directory.EnumerateFiles(folder, "*.*").Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).Take(40).ToList();
                string? pick = files.FirstOrDefault(f => Regex.IsMatch(Path.GetFileName(f), @"^readme(\.[a-z-]+)?\.(md|txt)$", RegexOptions.IgnoreCase))
                    ?? (files.Count(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) is >= 1 and <= 3
                        ? files.FirstOrDefault(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(Path.GetFileName(f), @"^(license|changelog|notice|third|security|contributing|code_of)", RegexOptions.IgnoreCase))
                        : null);
                if (pick is not null && new FileInfo(pick).Length > 0) return Lead(pick);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return "";
    });

    private static string Lead(string file)
    {
        var buf = new char[MaxReadme];
        int n;
        using (var r = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true)) n = r.ReadBlock(buf, 0, buf.Length);
        var sb = new StringBuilder();
        bool code = false;
        foreach (string raw in new string(buf, 0, n).Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("```")) { code = !code; continue; }
            if (code || line.Length == 0 || line.StartsWith('|') || line.StartsWith("<") || line.StartsWith("![") || line.StartsWith("[!")) continue;
            line = Regex.Replace(line, @"!\[[^\]]*\]\([^)]*\)", " ");             // images
            line = Regex.Replace(line, @"\[([^\]]*)\]\([^)]*\)", "$1");           // links -> their text
            line = Regex.Replace(line, @"https?://\S+|`[^`]*`|<[^>]+>", " ");      // bare URLs, inline code, tags
            line = Regex.Replace(line, @"[#*_>~=]+", " ").Trim();
            if (line.Length == 0) continue;
            sb.Append(line).Append(' ');
            if (sb.Length > 1200) break;
        }
        return sb.ToString().Trim();
    }

    private static RegistryKey? Open(RegistryKey hive, string path)
    {
        try { return hive.OpenSubKey(path); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException) { return null; }
    }

    private static string[] Names(RegistryKey hive, string path)
    {
        using var k = Open(hive, path);
        return k?.GetValueNames().Where(v => v.Length > 0).ToArray() ?? Array.Empty<string>();
    }

    private static string[] SubKeys(RegistryKey hive, string path)
    {
        using var k = Open(hive, path);
        return k?.GetSubKeyNames() ?? Array.Empty<string>();
    }

    // String values of a key (OpenWithList a=app.exe, SupportedTypes value names).
    private static IEnumerable<string> Values(RegistryKey hive, string path)
    {
        using var k = Open(hive, path);
        if (k is null) return Array.Empty<string>();
        var list = new List<string>();
        foreach (string v in k.GetValueNames())
        {
            if (v.StartsWith('.')) list.Add(v);
            else if (v.Length == 1 && k.GetValue(v) is string s) list.Add(s);
        }
        return list;
    }
}
