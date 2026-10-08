using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ZoneQuanta.Core.Monitor;
using ZoneQuanta.Core.Traffic;

namespace ZoneQuanta.UI.Panel.Pages;

public partial class TrafficPage : UserControl
{
    private static readonly (string Key, string Text)[] RangeDefs =
    {
        ("Today", "今天"), ("Day", "24 小时"), ("Week", "本周"), ("Month", "一月"), ("Year", "一年"), ("All", "全部时间"),
    };

    private readonly IPanelHost _host;
    private int _ticks;
    private TrafficFile? _apps;
    private DateTime _appsStamp;

    public TrafficPage(IPanelHost host)
    {
        _host = host;
        InitializeComponent();
        var s = host.Settings;
        DataContext = s;

        foreach (var (key, text) in RangeDefs)
        {
            var r = new RadioButton
            {
                Content = text,
                Style = (Style)FindResource("Segment"),
                GroupName = "range",
                Margin = new Thickness(2, 0, 2, 0),
                IsChecked = s.TrafficRange == key,
            };
            r.Checked += (_, _) =>
            {
                s.TrafficRange = key;
                Refresh(animate: true);
            };
            Ranges.Children.Add(r);
        }

        TrackToggle.IsChecked = s.TrackApps;
        TrackToggle.Click += async (_, _) =>
        {
            bool want = TrackToggle.IsChecked == true;
            TrackToggle.IsEnabled = false;
            bool ok = await host.SetTrackingAsync(want);
            TrackToggle.IsChecked = ok ? want : !want;
            TrackToggle.IsEnabled = true;
            Refresh(animate: true);
        };

        s.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Core.Settings.AppSettings.TrackApps)) return;
            TrackToggle.IsChecked = s.TrackApps;
            Refresh(animate: false);
        };
        host.Tick += _ => OnTick();
        Loaded += (_, _) => Refresh(animate: true);
    }

    private void OnTick()
    {
        if (!IsVisible) return;
        var s = _host.Settings;
        Chart.Bits = s.SpeedBits;
        Chart.Unit = s.SpeedUnit;
        Chart.Push(_host.Latest.UpBps, _host.Latest.DownBps);
        if (++_ticks % 5 == 0) Refresh(animate: false);
    }

    private void Refresh(bool animate)
    {
        var s = _host.Settings;
        long from = TrafficFile.RangeStart(s.TrafficRange);

        var totals = _host.Totals.Query(from);
        totals.TryGetValue(TrafficFile.TotalKey, out var t);
        UpText.Text = UnitFormat.Size(t.Up, s.SpeedBits, s.SpeedUnit);
        DownText.Text = UnitFormat.Size(t.Down, s.SpeedBits, s.SpeedUnit);
        TotalText.Text = UnitFormat.Size(t.Up + t.Down, s.SpeedBits, s.SpeedUnit);

        Apps.Children.Clear();
        if (!s.TrackApps)
        {
            Apps.Children.Add(Placeholder("开启下方开关后，这里按应用统计流量"));
            return;
        }

        string path = Path.Combine(TrafficFile.DefaultDir, TrafficHelper.AppsFileName);
        DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (_apps is null || stamp != _appsStamp)
        {
            _apps = TrafficFile.Load(path);
            _appsStamp = stamp;
        }

        var rows = _apps.Query(from)
            .Where(kv => kv.Key != TrafficFile.TotalKey && kv.Value.Up + kv.Value.Down > 0)
            .OrderByDescending(kv => kv.Value.Up + kv.Value.Down)
            .Take(14)
            .ToList();
        if (rows.Count == 0)
        {
            Apps.Children.Add(Placeholder("暂无数据"));
            return;
        }

        double max = rows[0].Value.Up + rows[0].Value.Down;
        int index = 0;
        foreach (var kv in rows)
            Apps.Children.Add(BuildRow(kv.Key, kv.Value.Up, kv.Value.Down, max, animate, index++));
    }

    private UIElement BuildRow(string name, long up, long down, double max, bool animate, int index)
    {
        var s = _host.Settings;
        double total = up + down;
        double ratio = Math.Max(0.02, total / max);

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 7), Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });

        grid.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });

        var track = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        var back = new Border { CornerRadius = new CornerRadius(4) };
        back.SetResourceReference(Border.BackgroundProperty, "LineBrush");
        track.Children.Add(back);

        var fill = new Grid { RenderTransformOrigin = new Point(0, 0.5) };
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - ratio + 0.0001, GridUnitType.Star) });
        var parts = new Grid();
        parts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(up, 1), GridUnitType.Star) });
        parts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(down, 1), GridUnitType.Star) });
        var upBar = new Border { CornerRadius = new CornerRadius(4, 0, 0, 4) };
        upBar.SetResourceReference(Border.BackgroundProperty, "Accent2Brush");
        var downBar = new Border { CornerRadius = new CornerRadius(0, 4, 4, 0) };
        downBar.SetResourceReference(Border.BackgroundProperty, "Accent1Brush");
        Grid.SetColumn(downBar, 1);
        parts.Children.Add(upBar);
        parts.Children.Add(downBar);
        fill.Children.Add(parts);
        track.Children.Add(fill);
        Grid.SetColumn(track, 1);
        grid.Children.Add(track);

        var value = new TextBlock
        {
            Text = UnitFormat.Size(total, s.SpeedBits, s.SpeedUnit),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
        };
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);

        grid.ToolTip = $"{name}\n上传 {UnitFormat.Size(up, s.SpeedBits, s.SpeedUnit)}\n下载 {UnitFormat.Size(down, s.SpeedBits, s.SpeedUnit)}";

        if (animate)
        {
            var scale = new ScaleTransform(0, 1);
            fill.RenderTransform = scale;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520))
            {
                BeginTime = TimeSpan.FromMilliseconds(index * 40),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
        return grid;
    }

    private static TextBlock Placeholder(string text)
    {
        var t = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return t;
    }
}
