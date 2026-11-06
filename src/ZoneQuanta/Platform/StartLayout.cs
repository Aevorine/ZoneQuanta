using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ZoneQuanta.Core;

namespace ZoneQuanta.Platform;

// Pinned items of the Windows 11 Start menu, read through the StartLayout PowerShell module.
internal static class StartLayout
{
    public sealed record Pin(string Target, string? Name);

    // Windows rewrites this file whenever the Start layout changes.
    public static string StateDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Packages", "Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy", "LocalState");

    public static async Task<List<Pin>?> ReadAsync()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ZoneQuanta" + DataPaths.InstanceSuffix); // a sandbox must not lock the user's copy
        Directory.CreateDirectory(dir);
        string layout = Path.Combine(dir, "start-layout.json"), apps = Path.Combine(dir, "start-apps.json");
        string script = "$ErrorActionPreference='Stop';" +
            $"Export-StartLayout -Path '{Quote(layout)}';" +
            $"Get-StartApps | Select-Object Name,AppID | ConvertTo-Json -Compress | Out-File -Encoding utf8 '{Quote(apps)}'";
        string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        var psi = new ProcessStartInfo(ps, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " +
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true };
        try
        {
            File.Delete(layout);
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stderr = p.StandardError.ReadToEndAsync();
            var exit = p.WaitForExitAsync();
            if (await Task.WhenAny(exit, Task.Delay(30000)) != exit)
            {
                p.Kill(true);
                Log.Write("start-layout timeout");
                return null;
            }
            if (p.ExitCode != 0 || !File.Exists(layout))
            {
                Log.Write($"start-layout exit {p.ExitCode}: {(await stderr).Trim()}");
                return null;
            }
            return Parse(File.ReadAllText(layout), File.Exists(apps) ? File.ReadAllText(apps) : "[]");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or JsonException or InvalidOperationException)
        {
            Log.Error("start-layout", ex);
            return null;
        }
        finally
        {
            try { File.Delete(layout); File.Delete(apps); }
            catch (IOException) { }
        }
    }

    private static string Quote(string s) => s.Replace("'", "''");

    private static List<Pin> Parse(string layoutJson, string appsJson)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var doc = JsonDocument.Parse(appsJson.Trim().Length == 0 ? "[]" : appsJson))
        {
            var root = doc.RootElement;
            IEnumerable<JsonElement> list = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : new[] { root };
            foreach (var a in list)
                if (a.TryGetProperty("AppID", out var id) && a.TryGetProperty("Name", out var n) && id.GetString() is { } i && n.GetString() is { } name)
                    names[i] = name;
        }

        var pins = new List<Pin>();
        using var layout = JsonDocument.Parse(layoutJson);
        if (!layout.RootElement.TryGetProperty("pinnedList", out var pinned)) return pins;
        foreach (var e in pinned.EnumerateArray())
        {
            if (e.TryGetProperty("desktopAppLink", out var link) && link.GetString() is { } l)
            {
                string path = Environment.ExpandEnvironmentVariables(l);
                if (File.Exists(path)) pins.Add(new Pin(path, null));
            }
            else if ((e.TryGetProperty("packagedAppId", out var app) || e.TryGetProperty("desktopAppId", out app)) && app.GetString() is { } id)
            {
                pins.Add(new Pin(@"shell:AppsFolder\" + id, names.TryGetValue(id, out var n) ? n : null));
            }
        }
        return pins;
    }
}
