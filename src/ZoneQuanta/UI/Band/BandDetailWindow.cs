using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ZoneQuanta.Core.Monitor;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.Core.Traffic;
using ZoneQuanta.Platform;
using ZoneQuanta.UI.Controls;

namespace ZoneQuanta.UI.Band;

public sealed class BandDetailWindow : Window
{
    private const double CardWidth = 320;

    private readonly AppSettings _settings;
    private readonly TrafficFile _totals;
    private readonly SystemMetrics _metrics;
    private readonly Func<Rect> _bandRect;

    private readonly SpeedChart _chart = new() { Height = 38, Margin = new Thickness(0, 6, 0, 4) };
    private readonly TextBlock _up = Value("Accent2Brush"), _down = Value("Accent1Brush"), _sum = Value("TextBrush");
    private readonly TextBlock _todayUp = Value("TextBrush"), _todayDown = Value("TextBrush"), _todaySum = Value("TextBrush");
    private readonly TextBlock _adapter = Value("MutedBrush");
    private readonly TextBlock _cpu = Value("TextBrush"), _cores = Value("TextBrush"), _procs = Value("TextBrush");
    private readonly TextBlock _mem = Value("TextBrush"), _memFree = Value("TextBrush"), _commit = Value("TextBrush");
    private readonly TextBlock _uptime = Value("TextBrush");
    private readonly Bar _cpuBar = new("Accent1Brush"), _memBar = new("Accent2Brush");
    private readonly StackPanel _apps = new();
    private readonly TranslateTransform _slide = new();

    private TrafficFile? _appsFile;
    private DateTime _appsStamp;
    private Dictionary<string, (long Up, long Down)> _today = new();
    private int _ticks;
    private bool _wanted;
    private Metrics _last;
    private IntPtr _hwnd;

    public BandDetailWindow(AppSettings settings, TrafficFile totals, SystemMetrics metrics, Func<Rect> bandRect)
    {
        _settings = settings;
        _totals = totals;
        _metrics = metrics;
        _bandRect = bandRect;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        SizeToContent = SizeToContent.Height;
        Width = CardWidth + 24;
        Title = "ZoneQuanta Detail";
        Opacity = 0;

        var card = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(16, 14, 16, 14),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.35, Color = Colors.Black },
            RenderTransform = _slide,
            Child = Build(),
        };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        System.Windows.Documents.TextElement.SetFontFamily(card, (FontFamily)Application.Current.FindResource("AppFont"));
        Content = card;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.SetExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT, true);
        };
    }

    private UIElement Build()
    {
        var root = new StackPanel();

        root.Children.Add(Section("网络", "Accent1Brush", first: true));
        root.Children.Add(Stats(("上传", _up), ("下载", _down), ("合计", _sum)));
        root.Children.Add(_chart);
        root.Children.Add(Stats(("今日上传", _todayUp), ("今日下载", _todayDown), ("今日合计", _todaySum)));
        root.Children.Add(Row("网卡", _adapter));

        root.Children.Add(Section("处理器", "Accent2Brush"));
        root.Children.Add(Row("使用率", _cpu));
        root.Children.Add(_cpuBar.Host);
        root.Children.Add(Stats(("核心", _cores), ("进程 · 线程", _procs), ("已开机", _uptime)));

        root.Children.Add(Section("内存", "Accent1Brush"));
        root.Children.Add(Row("已用", _mem));
        root.Children.Add(_memBar.Host);
        root.Children.Add(Stats(("可用", _memFree), ("已提交", _commit)));

        root.Children.Add(_apps);
        return root;
    }

    public void SetHover(bool on)
    {
        _wanted = on;
        if (on)
        {
            _ticks = 0;
            Refresh(_last);
            if (!IsVisible) Show();
            UpdateLayout();
            Place();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
            _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(_slide.Y == 0 ? 8 : _slide.Y, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        }
        else if (IsVisible)
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
            fade.Completed += (_, _) => { if (!_wanted) Hide(); };
            BeginAnimation(OpacityProperty, fade);
        }
    }

    public void Update(Metrics m)
    {
        _last = m;
        if (!_wanted || !IsVisible) return;
        _chart.Bits = _settings.SpeedBits;
        _chart.Unit = _settings.SpeedUnit;
        _chart.Push(m.UpBps, m.DownBps);
        Refresh(m);
        if (_ticks % 5 == 0) Place();
    }

    private void Refresh(Metrics m)
    {
        bool bits = _settings.SpeedBits;
        string unit = _settings.SpeedUnit;

        _up.Text = UnitFormat.Speed(m.UpBps, bits, unit);
        _down.Text = UnitFormat.Speed(m.DownBps, bits, unit);
        _sum.Text = UnitFormat.Speed(m.UpBps + m.DownBps, bits, unit);

        if (_ticks % 5 == 0)
        {
            var d = SystemDetail.Read();
            _today = _totals.Query(TrafficFile.RangeStart("Today"));
            _today.TryGetValue(TrafficFile.TotalKey, out var t);
            _todayUp.Text = UnitFormat.Size(t.Up, bits, unit);
            _todayDown.Text = UnitFormat.Size(t.Down, bits, unit);
            _todaySum.Text = UnitFormat.Size(t.Up + t.Down, bits, unit);
            _adapter.Text = _metrics.AdapterSummary();

            _cores.Text = $"{d.Cores}";
            _procs.Text = $"{d.Processes} · {d.Threads}";
            _memFree.Text = Gb(d.Available);
            _commit.Text = $"{Gb(d.CommitUsed)} / {Gb(d.CommitLimit)}";
            _uptime.Text = Uptime(d.Uptime);
            RebuildApps();
        }

        _cpu.Text = $"{m.Cpu:0.0}%";
        _cpuBar.Set(m.Cpu / 100.0);
        _mem.Text = m.MemTotal > 0 ? $"{Gb(m.MemUsed)} / {Gb(m.MemTotal)} · {m.Mem:0}%" : $"{m.Mem:0}%";
        _memBar.Set(m.Mem / 100.0);
        _ticks++;
    }

    private void RebuildApps()
    {
        _apps.Children.Clear();
        if (!_settings.TrackApps) return;

        string path = Path.Combine(TrafficFile.DefaultDir, TrafficHelper.AppsFileName);
        DateTime stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (_appsFile is null || stamp != _appsStamp)
        {
            _appsFile = TrafficFile.Load(path);
            _appsStamp = stamp;
        }

        var rows = _appsFile.Query(TrafficFile.RangeStart("Today"))
            .Where(kv => kv.Key != TrafficFile.TotalKey && kv.Value.Up + kv.Value.Down > 0)
            .OrderByDescending(kv => kv.Value.Up + kv.Value.Down)
            .Take(3)
            .ToList();
        if (rows.Count == 0) return;

        _apps.Children.Add(Section("今日流量排行", "Accent2Brush"));
        foreach (var kv in rows)
        {
            var size = Value("TextBrush");
            size.Text = UnitFormat.Size(kv.Value.Up + kv.Value.Down, _settings.SpeedBits, _settings.SpeedUnit);
            _apps.Children.Add(Row(kv.Key, size));
        }
    }

    private void Place()
    {
        if (_hwnd == IntPtr.Zero) return;
        var band = _bandRect();
        if (band.IsEmpty) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
        var screen = System.Windows.Forms.Screen.FromRectangle(new System.Drawing.Rectangle((int)band.Left, (int)band.Top, (int)band.Width, (int)band.Height));
        var work = new Rect(screen.WorkingArea.Left / sx, screen.WorkingArea.Top / sy, screen.WorkingArea.Width / sx, screen.WorkingArea.Height / sy);

        double bandLeft = band.Left / sx, bandTop = band.Top / sy, bandBottom = band.Bottom / sy;
        double h = ActualHeight > 0 ? ActualHeight : 420;
        bool below = bandTop < work.Top + work.Height / 2;

        double x = Math.Clamp(bandLeft - 6, work.Left, Math.Max(work.Left, work.Right - Width));
        double y = below ? bandBottom - 4 : bandTop - h + 4;
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - h));
        Left = x;
        Top = y;
    }

    private static string Gb(long bytes) => $"{bytes / 1073741824.0:0.0} GB";

    private static string Uptime(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays} 天 {t.Hours} 小时 {t.Minutes} 分" : $"{t.Hours} 小时 {t.Minutes} 分";

    private static TextBlock Value(string brush)
    {
        var t = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    private static UIElement Section(string title, string accent, bool first = false)
    {
        var t = new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(0, first ? 0 : 10, 0, 4) };
        t.SetResourceReference(TextBlock.ForegroundProperty, accent);
        return t;
    }

    private static UIElement Stats(params (string Label, TextBlock Value)[] items)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        for (int i = 0; i < items.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var cell = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0) };
            var caption = new TextBlock { Text = items[i].Label, FontSize = 11 };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            items[i].Value.HorizontalAlignment = HorizontalAlignment.Left;
            cell.Children.Add(caption);
            cell.Children.Add(items[i].Value);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        return grid;
    }

    private static UIElement Row(string label, TextBlock value)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(0, 0, 14, 0) };
        l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Grid.SetColumn(value, 1);
        grid.Children.Add(l);
        grid.Children.Add(value);
        return grid;
    }

    private sealed class Bar
    {
        private readonly ScaleTransform _scale = new(0, 1);
        public Border Host { get; }

        public Bar(string accent)
        {
            var fill = new Border { CornerRadius = new CornerRadius(3), RenderTransform = _scale, RenderTransformOrigin = new Point(0, 0.5) };
            fill.SetResourceReference(Border.BackgroundProperty, accent);
            Host = new Border { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 3, 0, 5), Child = fill };
            Host.SetResourceReference(Border.BackgroundProperty, "LineBrush");
        }

        public void Set(double ratio) =>
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(Math.Clamp(ratio, 0, 1), TimeSpan.FromMilliseconds(400)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
