using System;
using Microsoft.Win32;

namespace ZoneQuanta.Platform;

internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ZoneQuanta";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled, string? exePath = null)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            string exe = exePath ?? Environment.ProcessPath ?? string.Empty;
            if (exe.Length > 0) key.SetValue(ValueName, $"\"{exe}\" --autostart");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
