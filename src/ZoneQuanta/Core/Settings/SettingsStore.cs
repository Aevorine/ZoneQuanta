using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace ZoneQuanta.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    // Window placements are written at once: a restart right after moving a window must not lose it.
    private static readonly HashSet<string> Immediate = new() { nameof(AppSettings.QuickBounds), nameof(AppSettings.PanelBounds) };
    private readonly string _path;
    private readonly DispatcherTimer _debounce;

    public AppSettings Current { get; }

    public SettingsStore()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZoneQuanta");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        Current = Load(_path, out bool migrated);
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) => Flush();
        Current.PropertyChanged += (_, e) =>
        {
            _debounce.Stop();
            if (e.PropertyName is { } name && Immediate.Contains(name)) Flush();
            else _debounce.Start();
        };
        if (migrated) Flush();
    }

    public void Flush()
    {
        _debounce.Stop();
        try { DurableFile.Write(_path, JsonSerializer.Serialize(Current, Json)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error("settings-save", ex); }
    }

    private static AppSettings Load(string path, out bool migrated)
    {
        var s = DurableFile.Read(path, t => JsonSerializer.Deserialize<AppSettings>(t), "settings") ?? new AppSettings();
        if (s.Zones.Count == 0) s.Zones = new AppSettings().Zones;
        migrated = s.MigrateLegacy();
        return s;
    }
}
