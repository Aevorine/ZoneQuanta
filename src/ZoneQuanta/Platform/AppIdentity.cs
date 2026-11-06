using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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

    // Web: where a brand icon can be fetched when nothing installed has a fitting one (AppBrands.Web.Spec).
    public sealed record Identity(string Icon, string Friendly, string Owner, string OwnerKey, string Web = "");

    public static Identity Describe(string path, string aumid, string name, string description)
    {
        if (aumid.Length == 0 && path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) aumid = PackagedAumid(path);
        if (aumid.Length > 0)
        {
            string shell = @"shell:AppsFolder\" + aumid;
            return new Identity(shell, ShellName(shell) ?? Pick(description, name), "", aumid);
        }

        var index = StartMenuIndex.Current;
        bool own = HasOwnIcon(path) && !AppBrands.RuntimeBuilt(path, name);
        if (index.ByTarget(path) is { } exact)
            return new Identity(exact.Lnk, exact.Name, "", exact.Target);
        var owner = index.OwnerOf(path);
        if (owner is not null) return new Identity(own ? path : owner.Lnk, Pick(description, name), owner.Name, owner.Target);
        // No Start menu entry around it: the installed program it belongs to (Apps & features), if that has an icon.
        if (!own && !IsWindows(path) && InstalledPrograms.OwnerOf(path) is { } product)
            return new Identity(product.Icon, Pick(description, name), product.Name, product.Key);
        if (own || !File.Exists(path)) return new Identity(File.Exists(path) ? path : "", Pick(description, name), "", path);
        // No icon in the file and no product around it: a related one, else the generic program icon until the
        // brand icon has been fetched.
        var (icon, web) = Borrowed(path, name, description);
        return new Identity(icon ?? path, Pick(description, name), "", path, web?.Spec ?? "");
    }

    // For a program that carries no icon, nearest relation first: Windows' artwork for its components (where a
    // brand is known, curl in System32 say, that brand's icon replaces it once fetched); the main application of
    // the same vendor installed beside it; the editor extension it came with; a Store app or shortcut of the same
    // name; an installed product of its family; the application of the same company anywhere. Otherwise only
    // where its brand icon can be fetched. path is "" for a program whose file is gone.
    public static (string? Icon, AppBrands.Web? Web) Borrowed(string path, string name, string description)
    {
        var index = StartMenuIndex.Current;
        var web = (path.Length > 0 ? AppBrands.NpmHome(path) : null) ?? AppBrands.Known(name, path);
        if (path.Length > 0 && IsWindows(path))
        {
            // Another company's service installed into the Windows folder (VMware's vmnat) is not part of Windows.
            string company = index.Company(path);
            if (company.Length == 0 || company.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) return (AppBrands.WindowsArt(name), web);
            return index.CompanyApp(path) is { } app ? (app.Lnk, null) : (null, web);
        }
        string? local = (path.Length > 0 ? index.SameVendor(path)?.Lnk ?? AppBrands.ExtensionIcon(path) : null)
                        ?? SameName(name, path, description)
                        ?? AppBrands.ProductIcon(name, path)
                        ?? (path.Length > 0 ? index.CompanyApp(path)?.Lnk : null);
        return (local, local is null ? web : null);
    }

    // Exactly the same name: "codex" -> the Codex Store app, "GitHub CLI" -> a shortcut called that.
    private static string? SameName(string name, string path, string description)
    {
        string product = "";
        try { if (path.Length > 0 && File.Exists(path)) product = FileVersionInfo.GetVersionInfo(path).ProductName?.Trim() ?? ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        foreach (string n in new[] { name, product, description }.Where(n => n.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ExecutableIndex.PackageNamed(n) is { } aumid) return @"shell:AppsFolder\" + aumid;
            if (StartMenuIndex.Current.Named(n) is { } link) return link.Lnk;
        }
        return null;
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
    private readonly Dictionary<string, Link> _byLinkName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _containers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _generic = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _company = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _shortcuts = new(StringComparer.OrdinalIgnoreCase); // per target, all shortcuts

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

    // A program file by its name ("Weixin" -> X:\Apps\Weixin\Weixin.exe), for traffic recorded before paths were kept.
    public string? PathOf(string name) => _byName.GetValueOrDefault(name)?.Target;

    // Nearest shortcut program installed around path: first one sitting directly in the closest enclosing folder,
    // never past a folder that holds several applications (Program Files, a shared apps folder, the user profile).
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

    // A shortcut with exactly this name.
    public Link? Named(string text) => _byLinkName.GetValueOrDefault(text);

    // The main application of the same vendor (company name in the version information) installed in the same
    // vendor folder: C:\Program Files\Huawei\BasicService -> Huawei PC Manager. Never across a folder holding
    // several vendors' programs (Program Files, a shared apps folder).
    public Link? SameVendor(string path)
    {
        if (AppIdentity.IsWindows(path)) return null;
        string company = CompanyOf(path), vendor = Vendor(company);
        if (vendor.Length == 0) return null;
        for (string? a = Path.GetDirectoryName(path); a is not null && !_generic.Contains(a); a = Path.GetDirectoryName(a))
        {
            var inside = _byTarget.Values.Where(l => l.Target.StartsWith(a + "\\", StringComparison.OrdinalIgnoreCase)).ToList();
            if (inside.Count == 0) continue;
            if (inside.Exists(l => Vendor(CompanyOf(l.Target)) != vendor)) return null;
            // Same full company name first, then the program folder most shortcuts point into, and in it the
            // program most shortcuts start (the vendor's main application rather than one of its tools).
            return inside
                .GroupBy(l => l.Target[(a.Length + 1)..].Split('\\')[0], StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Any(l => CompanyOf(l.Target) == company))
                .ThenByDescending(g => g.Sum(l => _shortcuts.GetValueOrDefault(l.Target)))
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(l => _shortcuts.GetValueOrDefault(l.Target))
                    .ThenBy(l => l.Target.Count(c => c == '\\'))
                    .ThenBy(l => l.Name.Length)
                    .ThenBy(l => l.Target, StringComparer.OrdinalIgnoreCase)
                    .First())
                .FirstOrDefault();
        }
        return null;
    }

    public string Company(string exe) => CompanyOf(exe);

    // The application of exactly the same company, wherever it is installed, that most shortcuts start
    // (vmnat in System32 -> VMware Workstation). Not for Microsoft, whose name is on too many unrelated products.
    public Link? CompanyApp(string path)
    {
        string company = CompanyOf(path);
        if (company.Length < 3 || company.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) return null;
        string product = Product(path);
        return _byTarget.Values
            .Where(l => !string.Equals(l.Target, path, StringComparison.OrdinalIgnoreCase) && string.Equals(CompanyOf(l.Target), company, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => _shortcuts.GetValueOrDefault(l.Target))
            .ThenByDescending(l => product.Length > 2 && l.Name.StartsWith(product, StringComparison.OrdinalIgnoreCase))
            .ThenBy(l => l.Target.Count(c => c == '\\'))
            .ThenBy(l => l.Name.Length)
            .ThenBy(l => l.Target, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private string CompanyOf(string exe)
    {
        lock (_company)
            if (_company.TryGetValue(exe, out var c)) return c;
        string company = "";
        try { if (File.Exists(exe)) company = FileVersionInfo.GetVersionInfo(exe).CompanyName?.Trim() ?? ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        lock (_company) _company[exe] = company;
        return company;
    }

    private static readonly HashSet<string> NotNames = new(StringComparer.Ordinal) { "the", "inc", "ltd", "llc", "co", "corp", "corporation", "company", "gmbh", "limited" };

    // "Huawei Device Co., Ltd." and "Huawei Technologies Co., Ltd." are one vendor: its first proper word.
    private static string Vendor(string company)
    {
        foreach (string w in Regex.Split(company.ToLowerInvariant(), @"[^\p{L}\p{N}]+"))
            if (w.Length >= 2 && !NotNames.Contains(w)) return w;
        return "";
    }

    // Folders that hold several installed applications (e.g. X:\Apps), found from where shortcuts point.
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
                index._byLinkName[name] = link;
                index._shortcuts[target] = index._shortcuts.GetValueOrDefault(target) + 1;
            }
        }

        foreach (string g in GenericRoots())
        {
            index._containers.Add(g);
            index._generic.Add(g);
        }
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
