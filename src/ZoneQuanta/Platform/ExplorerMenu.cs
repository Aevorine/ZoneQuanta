using System;
using Microsoft.Win32;
using ZoneQuanta.Core;

namespace ZoneQuanta.Platform;

// Per-user Explorer verb "添加到快捷面板" (classic menu; on Windows 11 under "显示更多选项").
internal static class ExplorerMenu
{
    private const string Verb = "ZoneQuanta.Quick";

    private static readonly (string Key, string Arg, string Text)[] Targets =
    {
        (@"Software\Classes\*\shell\" + Verb, "%1", "添加到快捷面板"),
        (@"Software\Classes\Directory\shell\" + Verb, "%1", "添加到快捷面板"),
        (@"Software\Classes\Drive\shell\" + Verb, "%1", "添加到快捷面板"),
        (@"Software\Classes\Directory\Background\shell\" + Verb, "%V", "将此文件夹添加到快捷面板"),
    };

    public static bool IsRegistered(string exe)
    {
        foreach (var t in Targets)
        {
            using var key = Registry.CurrentUser.OpenSubKey(t.Key + @"\command");
            if (key?.GetValue(null) as string != Command(exe, t.Arg)) return false;
        }
        return true;
    }

    public static void Set(bool on, string exe)
    {
        try
        {
            foreach (var t in Targets)
            {
                if (!on)
                {
                    Registry.CurrentUser.DeleteSubKeyTree(t.Key, false);
                    continue;
                }
                using var verb = Registry.CurrentUser.CreateSubKey(t.Key);
                verb.SetValue(null, t.Text);
                verb.SetValue("Icon", $"\"{exe}\",0");
                verb.SetValue("MultiSelectModel", "Player");
                using var cmd = verb.CreateSubKey("command");
                cmd.SetValue(null, Command(exe, t.Arg));
            }
            Log.Write($"explorer-menu {(on ? "on" : "off")}");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            Log.Error("explorer-menu", ex);
        }
    }

    private static string Command(string exe, string arg) => $"\"{exe}\" --quick-add \"{arg}\"";
}
