using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.Core.Time;

namespace ZoneQuanta.UI.Panel.Pages;

public partial class ZonesPage : UserControl
{
    private const int MaxZones = 4;

    private sealed record Option(string Id, string Name, string? Label = null, double? Lat = null, double? Lon = null);

    private readonly IPanelHost _host;
    private readonly List<Option> _all;

    public ZonesPage(IPanelHost host)
    {
        _host = host;
        InitializeComponent();

        _all = new List<Option> { new("local", "本机时区", "本地") };
        _all.AddRange(CityCatalog.Popular.Select(c => new Option(c.TimeZoneId, "★ " + c.Display, c.Name, c.Lat, c.Lon)));
        _all.AddRange(TimeZoneInfo.GetSystemTimeZones().Select(z => new Option(z.Id, z.DisplayName)));

        var strip = new ZoneQuanta.UI.Controls.ClockStrip(host.Settings, host.Engine, allowAnimation: false, forceVertical: true);
        Preview.Content = strip;
        host.Tick += now => { if (IsVisible) strip.Update(now); };

        Filter.TextChanged += (_, _) => ApplyFilter();
        AddButton.Click += (_, _) => AddSelected();
        Options.MouseDoubleClick += (_, _) => AddSelected();
        ApplyFilter();
        RebuildRows();
    }

    private IReadOnlyList<ZoneConfig> Zones => _host.Settings.Zones;

    private void ApplyFilter()
    {
        string q = Filter.Text.Trim();
        Options.ItemsSource = q.Length == 0
            ? _all
            : _all.Where(o => o.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || o.Id.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void AddSelected()
    {
        if (Options.SelectedItem is not Option o || Zones.Count >= MaxZones) return;
        Commit(Zones.Append(new ZoneConfig { Label = DefaultLabel(o), TimeZoneId = o.Id, Lat = o.Lat, Lon = o.Lon }));
    }

    private static string DefaultLabel(Option o) =>
        o.Label ?? Regex.Replace(o.Name, @"^\(.*?\)\s*", string.Empty).Trim();

    private void Commit(IEnumerable<ZoneConfig> zones)
    {
        _host.Settings.Zones = zones.ToList();
        RebuildRows();
    }

    private void RebuildRows()
    {
        Rows.Children.Clear();
        var zones = Zones.ToList();
        for (int i = 0; i < zones.Count; i++)
        {
            int index = i;
            var zone = zones[i];

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int c = 0; c < 3; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBox { Text = zone.Label, Style = (Style)FindResource("Field") };
            void Rename()
            {
                string text = label.Text.Trim();
                if (text.Length == 0 || text == zone.Label) return;
                Commit(Zones.Select((z, k) => k == index ? new ZoneConfig { Label = text, TimeZoneId = z.TimeZoneId, Lat = z.Lat, Lon = z.Lon } : z));
            }
            label.LostKeyboardFocus += (_, _) => Rename();
            label.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
            grid.Children.Add(label);

            AddButtonTo(grid, 1, "Icon.Up", index > 0, () => Move(index, -1));
            AddButtonTo(grid, 2, "Icon.Down", index < zones.Count - 1, () => Move(index, 1));
            AddButtonTo(grid, 3, "Icon.Close", zones.Count > 1, () => Commit(Zones.Where((_, k) => k != index)));

            Rows.Children.Add(grid);
        }
        AddButton.IsEnabled = zones.Count < MaxZones;
    }

    private void AddButtonTo(Grid grid, int column, string icon, bool enabled, Action action)
    {
        var b = new Button
        {
            Style = (Style)FindResource("IconButton"),
            Tag = FindResource(icon),
            IsEnabled = enabled,
            Opacity = enabled ? 1 : 0.3,
            Margin = new Thickness(4, 0, 0, 0),
        };
        b.Click += (_, _) => action();
        Grid.SetColumn(b, column);
        grid.Children.Add(b);
    }

    private void Move(int index, int delta)
    {
        var list = Zones.ToList();
        int target = index + delta;
        if (target < 0 || target >= list.Count) return;
        (list[index], list[target]) = (list[target], list[index]);
        Commit(list);
    }
}
