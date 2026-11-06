using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using ZoneQuanta.Core;

namespace ZoneQuanta.Platform;

// Apps & features entries that name both where a product is installed and its icon: the owner of programs
// in that folder that have no Start menu shortcut (Git's curl.exe and ssh.exe -> Git). Rebuilt every ten minutes.
internal static class InstalledPrograms
{
    public sealed record Product(string Name, string Icon, string Key);

    private sealed record Entry(string Root, Product Product);

    private static readonly object Gate = new();
    private static List<Entry>? _entries;
    private static long _builtAt;

    // The product installed in the deepest folder that contains path.
    public static Product? OwnerOf(string path)
    {
        Entry? best = null;
        foreach (var e in Entries())
            if (path.StartsWith(e.Root + "\\", StringComparison.OrdinalIgnoreCase) && (best is null || e.Root.Length > best.Root.Length)) best = e;
        return best?.Product;
    }

    private static List<Entry> Entries()
    {
        lock (Gate)
        {
            if (_entries is not null && Environment.TickCount64 - _builtAt < 600_000) return _entries;
            var list = new List<Entry>();
            var generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.Windows })
                generic.Add(Environment.GetFolderPath(f).TrimEnd('\\'));
            foreach (var (hive, key) in new[]
            {
                (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
            })
            {
                try
                {
                    using var root = hive.OpenSubKey(key);
                    foreach (string sub in root?.GetSubKeyNames() ?? Array.Empty<string>())
                    {
                        using var k = root!.OpenSubKey(sub);
                        if (k?.GetValue("DisplayName") is not string name || name.Length == 0) continue;
                        string icon = IconFile(k.GetValue("DisplayIcon") as string);
                        if (icon.Length == 0) continue;
                        string dir = (k.GetValue("InstallLocation") as string ?? "").Trim().Trim('"').TrimEnd('\\');
                        if (dir.Length == 0 || !Directory.Exists(dir)) dir = Path.GetDirectoryName(icon) ?? "";
                        // A folder like Program Files or a drive root says nothing about which product a file is.
                        if (dir.Length <= 3 || generic.Contains(dir)) continue;
                        list.Add(new Entry(dir, new Product(name.Trim(), icon, "arp:" + dir.ToLowerInvariant())));
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { Log.Error("installed-programs", ex); }
            }
            _builtAt = Environment.TickCount64;
            return _entries = list;
        }
    }

    // "\"C:\x\app.exe\",0" -> C:\x\app.exe; only an existing icon or program file that is not an uninstaller.
    private static string IconFile(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string v = Environment.ExpandEnvironmentVariables(raw.Trim());
        if (v.StartsWith('"')) v = v.IndexOf('"', 1) is int end and > 1 ? v[1..end] : v.Trim('"');
        int comma = v.LastIndexOf(',');
        if (comma > 2 && int.TryParse(v[(comma + 1)..].Trim(), out _)) v = v[..comma];
        string file = Path.GetFileName(v);
        if (file.Contains("unins", StringComparison.OrdinalIgnoreCase) || file.Contains("setup", StringComparison.OrdinalIgnoreCase)) return "";
        return (v.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) || v.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) && File.Exists(v) ? v : "";
    }
}
