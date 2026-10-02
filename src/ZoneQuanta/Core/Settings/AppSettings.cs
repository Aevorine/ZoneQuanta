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
}

public sealed class ZoneConfig
{
    public string Label { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "local";
}

public sealed class AppSettings : Observable
{
    private bool _widgetVisible = true, _clickThrough = true, _lockPosition = true, _topmost = true;
    private bool _hideOnFullscreen = true, _allowOffScreen = true, _autoStart;
    private bool _use24Hour = true, _showSeconds = true, _showDate = true, _showOffset = true, _showDayNight = true, _animate = true, _vertical;
    private bool _hotkeyEnabled = true, _autoCheckUpdate = true, _autoInstallUpdate, _useMirrors = true;
    private double _scale = 1.0, _opacity = 0.94;
    private double? _left, _top;
    private string _theme = "Graphite";
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

    public bool HotkeyEnabled { get => _hotkeyEnabled; set => Set(ref _hotkeyEnabled, value); }
    public uint HotkeyModifiers { get => _hotkeyModifiers; set => Set(ref _hotkeyModifiers, value); }
    public uint HotkeyKey { get => _hotkeyKey; set => Set(ref _hotkeyKey, value); }

    public bool AutoCheckUpdate { get => _autoCheckUpdate; set => Set(ref _autoCheckUpdate, value); }
    public bool AutoInstallUpdate { get => _autoInstallUpdate; set => Set(ref _autoInstallUpdate, value); }
    public bool UseMirrors { get => _useMirrors; set => Set(ref _useMirrors, value); }

    public List<ZoneConfig> Zones { get => _zones; set => Set(ref _zones, value); }
}
