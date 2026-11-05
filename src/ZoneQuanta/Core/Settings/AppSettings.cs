using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZoneQuanta.Core.Settings;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ZoneConfig
{
    public string Label { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "local";
    public double? Lat { get; set; }
    public double? Lon { get; set; }
}

public sealed class AppSettings : Observable
{
    private bool _widgetVisible = true, _clickThrough = true, _lockPosition = true, _topmost = true;
    private bool _hideOnFullscreen = true, _allowOffScreen = true, _autoStart;
    private bool _use24Hour = true, _showSeconds = true, _showDate = true, _showOffset = true, _showDayNight = true, _animate = true, _vertical;
    private bool _hotkeyEnabled = true, _autoCheckUpdate = true, _autoInstallUpdate, _useMirrors = true;
    private double _scale = 1.0, _opacity = 0.94;
    private double? _left, _top;
    private string _theme = "Graphite", _pin = "";
    private uint _hotkeyModifiers = 3, _hotkeyKey = 0x5A;
    private List<ZoneConfig> _zones = new()
    {
        new ZoneConfig { Label = "本地", TimeZoneId = "local" },
        new ZoneConfig { Label = "洛杉矶", TimeZoneId = "Pacific Standard Time" },
    };

    public bool WidgetVisible { get => _widgetVisible; set => Set(ref _widgetVisible, value); }
    public bool ClickThrough { get => _clickThrough; set => Set(ref _clickThrough, value); }
    public bool LockPosition { get => _lockPosition; set => Set(ref _lockPosition, value); }
    public bool Topmost { get => _topmost; set => Set(ref _topmost, value); }
    public bool HideOnFullscreen { get => _hideOnFullscreen; set => Set(ref _hideOnFullscreen, value); }
    public bool AllowOffScreen { get => _allowOffScreen; set => Set(ref _allowOffScreen, value); }
    public bool AutoStart { get => _autoStart; set => Set(ref _autoStart, value); }

    public bool Use24Hour { get => _use24Hour; set => Set(ref _use24Hour, value); }
    public bool ShowSeconds { get => _showSeconds; set => Set(ref _showSeconds, value); }
    public bool ShowDate { get => _showDate; set => Set(ref _showDate, value); }
    public bool ShowOffset { get => _showOffset; set => Set(ref _showOffset, value); }
    public bool ShowDayNight { get => _showDayNight; set => Set(ref _showDayNight, value); }
    public bool Animate { get => _animate; set => Set(ref _animate, value); }
    public bool Vertical { get => _vertical; set => Set(ref _vertical, value); }
    public string Theme { get => _theme; set => Set(ref _theme, value); }

    public double Scale { get => _scale; set => Set(ref _scale, Math.Clamp(value, 0.5, 2.5)); }
    public double Opacity { get => _opacity; set => Set(ref _opacity, Math.Clamp(value, 0.2, 1.0)); }
    public double? Left { get => _left; set => Set(ref _left, value); }
    public double? Top { get => _top; set => Set(ref _top, value); }
    public string Pin { get => _pin; set => Set(ref _pin, value ?? ""); }

    public bool HotkeyEnabled { get => _hotkeyEnabled; set => Set(ref _hotkeyEnabled, value); }
    public uint HotkeyModifiers { get => _hotkeyModifiers; set => Set(ref _hotkeyModifiers, value); }
    public uint HotkeyKey { get => _hotkeyKey; set => Set(ref _hotkeyKey, value); }

    public bool AutoCheckUpdate { get => _autoCheckUpdate; set => Set(ref _autoCheckUpdate, value); }
    public bool AutoInstallUpdate { get => _autoInstallUpdate; set => Set(ref _autoInstallUpdate, value); }
    public bool UseMirrors { get => _useMirrors; set => Set(ref _useMirrors, value); }

    private bool _bandVisible = true, _bandUp = true, _bandDown = true, _bandToday = true, _bandTotal = true, _bandMem = true, _bandCpu = true;
    private double _bandOffset;
    private bool _speedBits, _trackApps;
    private string _speedUnit = "Auto", _trafficRange = "Today", _bandPosition = "Left";

    public bool BandVisible { get => _bandVisible; set => Set(ref _bandVisible, value); }
    public bool BandUp { get => _bandUp; set => Set(ref _bandUp, value); }
    public bool BandDown { get => _bandDown; set => Set(ref _bandDown, value); }
    public bool BandToday { get => _bandToday; set => Set(ref _bandToday, value); }
    public bool BandTotal { get => _bandTotal; set => Set(ref _bandTotal, value); }
    public bool BandMem { get => _bandMem; set => Set(ref _bandMem, value); }
    public bool BandCpu { get => _bandCpu; set => Set(ref _bandCpu, value); }
    public string BandPosition { get => _bandPosition; set => Set(ref _bandPosition, value); }
    public double BandOffset { get => _bandOffset; set => Set(ref _bandOffset, Math.Clamp(value, -2000, 4000)); }

    private int _sampleIntervalMs = 500;

    // How often the taskbar band re-reads network / CPU / memory (milliseconds).
    public int SampleIntervalMs { get => _sampleIntervalMs; set => Set(ref _sampleIntervalMs, Math.Clamp(value, 250, 2000)); }

    public bool SpeedBits { get => _speedBits; set => Set(ref _speedBits, value); }
    public string SpeedUnit { get => _speedUnit; set => Set(ref _speedUnit, value); }
    public bool TrackApps { get => _trackApps; set => Set(ref _trackApps, value); }
    public string TrafficRange { get => _trafficRange; set => Set(ref _trafficRange, value); }

    public List<ZoneConfig> Zones { get => _zones; set => Set(ref _zones, value); }

    private bool _quotaEnabled, _quotaAlert = true;
    private double _quotaGB = 100;
    private int _quotaResetDay = 1, _quotaAlertPercent = 80;
    private string _quotaAlerted = "", _trafficChart = "Live", _appSort = "Total";

    public bool QuotaEnabled { get => _quotaEnabled; set => Set(ref _quotaEnabled, value); }
    public double QuotaGB { get => _quotaGB; set => Set(ref _quotaGB, Math.Clamp(value, 0.1, 1_000_000)); }
    public int QuotaResetDay { get => _quotaResetDay; set => Set(ref _quotaResetDay, Math.Clamp(value, 1, 31)); }
    public bool QuotaAlert { get => _quotaAlert; set => Set(ref _quotaAlert, value); }
    public int QuotaAlertPercent { get => _quotaAlertPercent; set => Set(ref _quotaAlertPercent, Math.Clamp(value, 50, 99)); }
    public string QuotaAlerted { get => _quotaAlerted; set => Set(ref _quotaAlerted, value ?? ""); }
    public string TrafficChart { get => _trafficChart; set => Set(ref _trafficChart, value); }
    public string AppSort { get => _appSort; set => Set(ref _appSort, value); }

    private bool _quickHotkeyEnabled = true, _pathHotkeyEnabled = true, _quickContextMenu = true, _quickCloseAfterOpen = true, _quickInTray = true;
    private bool _quickShowIcon = true, _quickShowName = true, _quickShowPath, _quickShowType, _quickShowSize, _quickShowModified, _quickShowCount;
    private uint _quickHotkeyModifiers = 3, _quickHotkeyKey = 0x20, _pathHotkeyModifiers = 3, _pathHotkeyKey = 0x47;
    private string _quickLayout = "Groups", _quickSort = "Custom";

    public bool QuickHotkeyEnabled { get => _quickHotkeyEnabled; set => Set(ref _quickHotkeyEnabled, value); }
    public uint QuickHotkeyModifiers { get => _quickHotkeyModifiers; set => Set(ref _quickHotkeyModifiers, value); }
    public uint QuickHotkeyKey { get => _quickHotkeyKey; set => Set(ref _quickHotkeyKey, value); }
    public bool PathHotkeyEnabled { get => _pathHotkeyEnabled; set => Set(ref _pathHotkeyEnabled, value); }
    public uint PathHotkeyModifiers { get => _pathHotkeyModifiers; set => Set(ref _pathHotkeyModifiers, value); }
    public uint PathHotkeyKey { get => _pathHotkeyKey; set => Set(ref _pathHotkeyKey, value); }
    public bool QuickContextMenu { get => _quickContextMenu; set => Set(ref _quickContextMenu, value); }
    public bool QuickCloseAfterOpen { get => _quickCloseAfterOpen; set => Set(ref _quickCloseAfterOpen, value); }
    public bool QuickInTray { get => _quickInTray; set => Set(ref _quickInTray, value); }
    public bool QuickShowIcon { get => _quickShowIcon; set => Set(ref _quickShowIcon, value); }
    public bool QuickShowName { get => _quickShowName; set => Set(ref _quickShowName, value); }
    public bool QuickShowPath { get => _quickShowPath; set => Set(ref _quickShowPath, value); }
    public bool QuickShowType { get => _quickShowType; set => Set(ref _quickShowType, value); }
    public bool QuickShowSize { get => _quickShowSize; set => Set(ref _quickShowSize, value); }
    public bool QuickShowModified { get => _quickShowModified; set => Set(ref _quickShowModified, value); }
    public bool QuickShowCount { get => _quickShowCount; set => Set(ref _quickShowCount, value); }
    // Groups (cards like a launcher home screen), Grid or List. A new key, so existing users start on Groups.
    public string QuickLayout { get => _quickLayout; set => Set(ref _quickLayout, value is "Grid" or "List" ? value : "Groups"); }
    public string QuickSort { get => _quickSort; set => Set(ref _quickSort, value); }

    private bool _quickStartSync = true, _quickAutoGroup = true;
    private double _quickWidth, _quickHeight;
    private int _quickCardCols, _quickCardRows;

    // Default group card size in icon cells (columns × rows); 0 sizes each card by its item count.
    public int QuickCardCols { get => _quickCardCols; set => Set(ref _quickCardCols, Math.Clamp(value, 0, 8)); }
    public int QuickCardRows { get => _quickCardRows; set => Set(ref _quickCardRows, Math.Clamp(value, 0, 6)); }
    private Dictionary<string, string> _localKeys = new();

    public bool QuickStartSync { get => _quickStartSync; set => Set(ref _quickStartSync, value); }
    public bool QuickAutoGroup { get => _quickAutoGroup; set => Set(ref _quickAutoGroup, value); }
    public double QuickWidth { get => _quickWidth; set => Set(ref _quickWidth, Math.Max(0, value)); }
    public double QuickHeight { get => _quickHeight; set => Set(ref _quickHeight, Math.Max(0, value)); }

    // In-launcher shortcuts, stored as "Ctrl+Return" style chords; missing entries fall back to defaults.
    public static readonly IReadOnlyDictionary<string, string> DefaultLocalKeys = new Dictionary<string, string>
    {
        ["Reveal"] = "Ctrl+Return", ["Rename"] = "F2", ["Remove"] = "Delete",
        ["CopyPath"] = "Ctrl+C", ["AddQuick"] = "Shift+Return", ["Complete"] = "Tab",
    };

    public Dictionary<string, string> LocalKeys { get => _localKeys; set => Set(ref _localKeys, value ?? new()); }

    // Panel navigation order by page title; empty keeps the built-in order, unknown titles are ignored.
    private List<string> _navOrder = new();
    public List<string> NavOrder { get => _navOrder; set => Set(ref _navOrder, value ?? new()); }

    public string LocalKey(string name) =>
        _localKeys.TryGetValue(name, out var v) && v.Length > 0 ? v : DefaultLocalKeys[name];

    // Both halves change together, so the hotkey is re-registered once with the final combination.
    public void SetGlobalHotkey(string which, uint mods, uint key)
    {
        switch (which)
        {
            case "Quick": _quickHotkeyModifiers = mods; _quickHotkeyKey = key; Raise(nameof(QuickHotkeyKey)); break;
            case "Path": _pathHotkeyModifiers = mods; _pathHotkeyKey = key; Raise(nameof(PathHotkeyKey)); break;
            default: _hotkeyModifiers = mods; _hotkeyKey = key; Raise(nameof(HotkeyKey)); break;
        }
    }

    public void SetLocalKey(string name, string chord) =>
        LocalKeys = new Dictionary<string, string>(_localKeys) { [name] = chord };
}
