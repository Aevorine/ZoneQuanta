using System;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace ZoneQuanta.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly DispatcherTimer _debounce;

    public AppSettings Current { get; }

    public SettingsStore()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZoneQuanta");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        Current = Load(_path);
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Flush(); };
        Current.PropertyChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
    }

    public void Flush()
    {
        try
        {
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
            File.Move(tmp, _path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
                if (s is not null)
                {
                    if (s.Zones.Count == 0) s.Zones = new AppSettings().Zones;
                    return s;
                }
            }
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new AppSettings();
    }
}
