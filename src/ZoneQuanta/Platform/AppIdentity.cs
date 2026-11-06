using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using ZoneQuanta.Core;

namespace ZoneQuanta.Platform;

// Which application a program file belongs to, the name people know it by, and the icon to show for it.
// A helper without an icon of its own (a proxy core, a runtime) takes the icon of the application installed
// around it: the nearest Start menu shortcut whose program sits in the same install folder.
internal static class AppIdentity
{
    public const string WindowsGlyph = "glyph:windows";
    public const string ExitedGlyph = "glyph:exited";

    public sealed record Identity(string Icon, string Friendly, string Owner, string OwnerKey);

    public static Identity Describe(string path, string aumid, string name, string description)
    {
        if (aumid.Length == 0 && path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) aumid = PackagedAumid(path);
        if (aumid.Length > 0)
        {
            string shell = @"shell:AppsFolder\" + aumid;
            return new Identity(shell, ShellName(shell) ?? Pick(description, name), "", aumid);
        }

        var index = StartMenuIndex.Current;
        bool own = HasOwnIcon(path);
        if (index.ByTarget(path) is { } exact)
            return new Identity(exact.Lnk, exact.Name, "", exact.Target);
        var owner = index.OwnerOf(path);
        if (owner is not null) return new Identity(own ? path : owner.Lnk, Pick(description, name), owner.Name, owner.Target);
        // No Start menu entry around it: the installed program it belongs to (Apps & features), if that has an icon.
        if (!own && !IsWindows(path) && InstalledPrograms.OwnerOf(path) is { } product)
            return new Identity(product.Icon, Pick(description, name), product.Name, product.Key);
        // Otherwise the icon Windows itself shows for the file (the generic program icon when it carries none).
        return new Identity(File.Exists(path) ? path : "", Pick(description, name), "", path);
    }

    private static string Pick(string description, string name) =>
        description.Length is > 0 and <= 40 ? description : name;

    public static bool IsWindows(string path) =>
        path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\", StringComparison.OrdinalIgnoreCase);

    // An executable without icon resources shows the generic program icon, which is never the real one.
    public static bool HasOwnIcon(string path)
    {
        try { return File.Exists(path) && ExtractIconEx(path, -1, null, null, 0) > 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    // C:\Program Files\WindowsApps\<Name>_<Ver>_<Arch>_<Res>_<Publisher>\...\app.exe -> <Name>_<Publisher>!<AppId>.
    private static string PackagedAumid(string path)
    {
        try
        {
            int at = path.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) + @"\WindowsApps\".Length;
            int end = path.IndexOf('\\', at);
            if (end < 0) return "";
            string root = path[..end], package = path[at..end], relative = path[(end + 1)..];
            string manifest = Path.Combine(root, "AppxManifest.xml");
            if (!File.Exists(manifest)) return "";
            var apps = XDocument.Load(manifest).Descendants().Where(e => e.Name.LocalName == "Application").ToList();
            var app = apps.FirstOrDefault(a => string.Equals((string?)a.Attribute("Executable"), relative, StringComparison.OrdinalIgnoreCase)) ?? apps.FirstOrDefault();
            return app?.Attribute("Id")?.Value is { } id ? ProcessImages.AumidOf(package, id) : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException) { return ""; }
    }

    // Display name the shell gives an item (a Store app's tile name).
    private static string? ShellName(string parsingName)
    {
        if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, typeof(IShellItem).GUID, out var item) != 0 || item is null) return null;
        try
        {
            if (item.GetDisplayName(0, out IntPtr p) != 0 || p == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(p); }
            finally { Marshal.FreeCoTaskMem(p); }
        }
        finally { Marshal.ReleaseComObject(item); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem? item);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(uint sigdn, out IntPtr name);
    }
}

// Programs reachable from the Start menu, by target path; rebuilt at most every ten minutes, off the UI thread.
internal sealed class StartMenuIndex
{
    public sealed record Link(string Lnk, string Name, string Target);

    private static readonly object Gate = new();
    private static StartMenuIndex? _current;
    private static long _builtAt;

    private readonly Dictionary<string, Link> _byTarget = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Link>> _byDir = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Link> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _containers = new(StringComparer.OrdinalIgnoreCase);

    public static StartMenuIndex Current
    {
        get
        {
            lock (Gate)
            {
                if (_current is null || Environment.TickCount64 - _builtAt > 600_000)
                {
                    var sw = Stopwatch.StartNew();
                    _current = Build();
                    _builtAt = Environment.TickCount64;
                    Log.Write($"start-index {_current._byTarget.Count} programs {sw.ElapsedMilliseconds}ms");
                }
                return _current;
            }
        }
    }

    public Link? ByTarget(string path) => _byTarget.GetValueOrDefault(path);

    // A program file by its name ("Weixin" -> D:\APPS\Weixin\Weixin.exe), for traffic recorded before paths were kept.
    public string? PathOf(string name) => _byName.GetValueOrDefault(name)?.Target;

    // Nearest shortcut program installed around path: first one sitting directly in the closest enclosing folder,
    // never past a folder that holds several applications (Program Files, D:\APPS, the user profile).
    public Link? OwnerOf(string path)
    {
        // Windows' own programs belong to no Start menu entry (System32 holds dozens of unrelated tools).
        if (AppIdentity.IsWindows(path)) return null;
        string? dir = Path.GetDirectoryName(path);
        string? top = null;
        for (string? a = dir; a is not null && !_containers.Contains(a); a = Path.GetDirectoryName(a))
        {
            top = a;
            if (_byDir.TryGetValue(a, out var here)) return Unique(here, path);
        }
        if (top is null) return null;
        // Nothing directly above it: only one application anywhere inside its install folder, or one whose
        // product name matches; otherwise none rather than a wrong icon.
        var inside = _byTarget.Values.Where(l => l.Target.StartsWith(top + "\\", StringComparison.OrdinalIgnoreCase))
            .GroupBy(l => l.Target, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        if (inside.Count <= 1) return inside.FirstOrDefault();
        string product = Product(path);
        return product.Length > 0 && inside.Where(l => Product(l.Target) == product).ToList() is { Count: 1 } same ? same[0] : null;
    }

    // Folders that hold several installed applications (e.g. D:\APPS), found from where shortcuts point.
    public IEnumerable<string> AppFolders => _appFolders;
    private readonly List<string> _appFolders = new();

    // Shortcuts inside one install folder are one product (folders holding several products are containers and
    // never reached), so one of them is always chosen: same product name as the helper, else the program
    // nearest the install folder's top, else the one named most like the folder, else the shortest name.
    private static Link? Unique(List<Link> links, string path)
    {
        var distinct = links.GroupBy(l => l.Target, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        if (distinct.Count <= 1) return distinct.FirstOrDefault();
        string product = Product(path);
        if (product.Length > 0 && distinct.Where(l => Product(l.Target) == product).ToList() is { Count: 1 } same) return same[0];
        return distinct
            .OrderBy(l => l.Target.Count(c => c == '\\'))
            .ThenByDescending(l => Similarity(l, path))
            .ThenBy(l => l.Name.Length)
            .ThenBy(l => l.Target, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    // Shared leading letters of the shortcut name and the install folder holding both ("Git Bash" ~ "Git").
    private static int Similarity(Link l, string path)
    {
        string folder = Path.GetFileName(Path.GetDirectoryName(l.Target) ?? "") ?? "";
        int n = 0;
        while (n < folder.Length && n < l.Name.Length && char.ToLowerInvariant(folder[n]) == char.ToLowerInvariant(l.Name[n])) n++;
        return n;
    }

    private static string Product(string exe)
    {
        try { return File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).ProductName?.Trim() ?? "" : ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    private static StartMenuIndex Build()
    {
        var index = new StartMenuIndex();
        var roots = new[] { Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.Programs };
        foreach (var root in roots)
        {
            string dir = Environment.GetFolderPath(root);
            if (dir.Length == 0 || !Directory.Exists(dir)) continue;
            IEnumerable<string> links;
            try { links = Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (string lnk in links)
            {
                string name = Path.GetFileNameWithoutExtension(lnk);
                if (IsUninstaller(name)) continue;
                string target = ShellLinks.Target(lnk);
                if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || IsUninstaller(Path.GetFileName(target)) || !File.Exists(target)) continue;
                var link = new Link(lnk, name, target);
                // User shortcuts are read last and win: they are the ones people renamed.
                index._byTarget[target] = link;
                index._byName[ProcessImages.NameOf(target)] = link;
            }
        }

        foreach (string g in GenericRoots()) index._containers.Add(g);
        var children = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in index._byTarget.Values)
        {
            if (AppIdentity.IsWindows(link.Target)) continue;
            string dir = Path.GetDirectoryName(link.Target)!;
            if (!index._byDir.TryGetValue(dir, out var list)) index._byDir[dir] = list = new List<Link>();
            list.Add(link);
            for (string child = dir, a = Path.GetDirectoryName(dir) ?? ""; a.Length > 0; child = a, a = Path.GetDirectoryName(a) ?? "")
            {
                if (!children.TryGetValue(a, out var set)) children[a] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(child);
            }
        }
        // A folder holding two or more separate installs is a place for applications, not one application.
        var generic = new HashSet<string>(GenericRoots(), StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, set) in children)
        {
            if (set.Count < 2) continue;
            index._containers.Add(folder);
            if (!generic.Contains(folder) && !AppIdentity.IsWindows(folder + "\\")) index._appFolders.Add(folder);
        }
        return index;
    }

    private static bool IsUninstaller(string name) =>
        name.Contains("uninst", StringComparison.OrdinalIgnoreCase) || name.Contains("卸载", StringComparison.Ordinal);

    private static IEnumerable<string> GenericRoots()
    {
        foreach (string d in Environment.GetLogicalDrives()) yield return d.TrimEnd('\\');
        foreach (var f in new[]
        {
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.CommonProgramFilesX86, Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms,
        })
        {
            string p = Environment.GetFolderPath(f);
            if (p.Length > 0) yield return p.TrimEnd('\\');
        }
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Programs");
        yield return Path.Combine(local, "Microsoft", "WindowsApps");
        string? users = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (users is not null) yield return users;
    }
}
