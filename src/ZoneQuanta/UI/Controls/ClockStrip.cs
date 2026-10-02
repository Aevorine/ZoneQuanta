using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.Core.Time;

namespace ZoneQuanta.UI.Controls;

public sealed class ClockStrip : Border
{
    private readonly UniformGrid _grid = new();
    private readonly List<ZoneCard> _cards = new();
    private readonly AppSettings _settings;
    private readonly TimeEngine _engine;
    private DateTimeOffset _last = DateTimeOffset.UtcNow;

    private readonly bool _allowAnimation;

    public ClockStrip(AppSettings settings, TimeEngine engine, bool allowAnimation = true)
    {
        _allowAnimation = allowAnimation;
        _settings = settings;
        _engine = engine;
        CornerRadius = new CornerRadius(16);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(4);
        this.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        this.SetResourceReference(BorderBrushProperty, "LineBrush");
        Child = _grid;
        SnapsToDevicePixels = true;

        Rebuild();
        _settings.PropertyChanged += OnSettingChanged;
        Unloaded += (_, _) => _settings.PropertyChanged -= OnSettingChanged;
        Loaded += (_, _) =>
        {
            _settings.PropertyChanged -= OnSettingChanged;
            _settings.PropertyChanged += OnSettingChanged;
            Render();
        };
    }

    public void Update(DateTimeOffset now)
    {
        _last = now;
        Render();
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.Zones):
            case nameof(AppSettings.Vertical):
                Rebuild();
                Render();
                break;
            case nameof(AppSettings.Theme):
                foreach (var c in _cards) c.Redraw();
                break;
            case nameof(AppSettings.Use24Hour):
            case nameof(AppSettings.ShowSeconds):
            case nameof(AppSettings.ShowDate):
            case nameof(AppSettings.ShowOffset):
            case nameof(AppSettings.ShowDayNight):
                Render();
                break;
        }
    }

    private void Rebuild()
    {
        _grid.Children.Clear();
        _cards.Clear();
        var zones = _settings.Zones;
        for (int i = 0; i < zones.Count; i++)
        {
            var card = new ZoneCard(i, _allowAnimation);
            _cards.Add(card);
            _grid.Children.Add(card);
        }
        _grid.Rows = _settings.Vertical ? zones.Count : 1;
        _grid.Columns = _settings.Vertical ? 1 : zones.Count;
    }

    private void Render()
    {
        var zones = _settings.Zones;
        for (int i = 0; i < _cards.Count && i < zones.Count; i++)
            _cards[i].Apply(_engine.Compute(zones[i], _last), _settings);
    }
}
