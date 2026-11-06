using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ZoneQuanta.Core;

// Where the user's data lives. "--data-dir <dir>" (or ZONEQUANTA_DATA) starts an isolated instance on a copy:
// its own single-instance names, and no hotkeys, autostart, Explorer verb, traffic helper or updates, so a
// test run can never overwrite the real settings or quick panel.
public static class DataPaths
{
    private static readonly string Default = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZoneQuanta");

    public static string Root { get; private set; } = Default;

    // Written by the elevated traffic helper (always the real folder); the UI only reads it.
    public static string HelperDir => Default;

    public static bool Sandboxed { get; private set; }

    // Appended to mutex / event / pipe names so a sandbox runs next to the user's instance.
    public static string InstanceSuffix { get; private set; } = "";

    // Set while restoring a snapshot: nothing may write the files being replaced.
    public static volatile bool WritesFrozen;

    public static string File(string name) => Path.Combine(Root, name);

    // Throws ArgumentException when a sandbox was asked for but its folder is unusable: running on the real
    // data instead would defeat the point of the flag.
    public static void Init(string[] args)
    {
        string? dir = null;
        int i = Array.FindIndex(args, a => a.Equals("--data-dir", StringComparison.OrdinalIgnoreCase));
        if (i >= 0)
        {
            dir = i + 1 < args.Length ? args[i + 1].Trim('"') : "";
            if (dir.Length == 0 || dir.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("--data-dir needs a folder");
        }
        dir ??= Environment.GetEnvironmentVariable("ZONEQUANTA_DATA");
        if (string.IsNullOrWhiteSpace(dir)) return;
        string full;
        try
        {
            full = Path.GetFullPath(dir);
            Directory.CreateDirectory(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new ArgumentException($"--data-dir \"{dir}\": {ex.Message}", ex);
        }
        if (string.Equals(full.TrimEnd('\\'), Default, StringComparison.OrdinalIgnoreCase)) return;
        Root = full;
        Sandboxed = true;
        InstanceSuffix = "." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToUpperInvariant())))[..8];
    }
}
