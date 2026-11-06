using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ZoneQuanta.Core.Quick;

// Which shortcuts in a Start menu folder are apps worth a quick-panel icon: not uninstallers, help files,
// readme / licence / website links, or repair and settings helpers of an installer.
public static class ShortcutFilter
{
    public static readonly string[] Extensions = { ".lnk", ".url", ".appref-ms" };

    private static readonly Regex JunkName = new(
        @"(^|\b)(uninstall|uninst|remove|deinstall|repair|readme|read me|release notes|changelog|change log|license|licence|eula|" +
        @"help|manual|documentation|docs|user guide|getting started|website|web site|homepage|home page|faq|what's new|whats new|" +
        @"debuggable package manager|safe mode)(\b|$)|卸载|删除|修复|帮助|说明|手册|文档|许可|协议|官网|官方网站|主页|更新日志|发行说明|自述|常见问题|安全模式",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex JunkTarget = new(
        @"\\(unins\w*|uninst\w*|uninstall\w*|setup|msiexec|helppane|hh|maintenancetool)\.exe$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] DocExtensions = { ".txt", ".pdf", ".chm", ".hlp", ".htm", ".html", ".rtf", ".md", ".log", ".doc", ".docx", ".xps" };

    public static bool IsShortcut(string path) => Array.Exists(Extensions, e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    // Why a shortcut is skipped, or null when it is an app. target is "" when unknown (Store apps, .url).
    public static string? Skip(string path, string target)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (JunkName.IsMatch(name)) return "name";
        if (target.Length == 0) return null;
        if (JunkTarget.IsMatch(target)) return "uninstaller";
        string ext = Path.GetExtension(target);
        if (Array.Exists(DocExtensions, e => ext.Equals(e, StringComparison.OrdinalIgnoreCase))) return "document";
        return null;
    }
}
