using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ZoneQuanta.Core.Monitor;
using ZoneQuanta.Core.Quick;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.Core.Traffic;
using ZoneQuanta.Platform;

namespace ZoneQuanta.UI.Panel.Pages;

public partial class TrafficPage : UserControl
{
    private const double GiB = 1024d * 1024 * 1024;
    private const int AppRows = 60;

    private static readonly (string Key, string Text)[] RangeDefs =
    {
        ("Today", "今天"), ("Day", "24 小时"), ("Week", "本周"), ("Month", "一月"), ("Cycle", "账期"), ("Year", "一年"), ("All", "全部时间"),
    };

    private static readonly (string Key, string Text)[] SortDefs = { ("Total", "合计"), ("Up", "上传"), ("Down", "下载") };

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

        foreach (var (key, text) in SortDefs)
        {
            var r = new RadioButton { Content = text, GroupName = "app-sort", Width = 56, Margin = new Thickness(4, 0, 0, 0), IsChecked = s.AppSort == key };
            r.SetResourceReference(StyleProperty, "Segment");
            r.Checked += (_, _) =>
            {
                s.AppSort = key;
                Refresh(animate: true);
            };
            Sorts.Children.Add(r);
        }
        AppSearch.TextChanged += (_, _) => Refresh(animate: false);

        LiveMode.IsChecked = s.TrafficChart != "Daily";
        DailyMode.IsChecked = s.TrafficChart == "Daily";
        LiveMode.Checked += (_, _) => { s.TrafficChart = "Live"; SyncChart(animate: true); };
        DailyMode.Checked += (_, _) => { s.TrafficChart = "Daily"; SyncChart(animate: true); };

        QuotaCard.MouseLeftButtonUp += (_, _) => QuotaPopup.IsOpen = !QuotaPopup.IsOpen;
        QuotaPopup.Opened += (_, _) => CapBox.Text = s.QuotaGB.ToString("0.##", CultureInfo.InvariantCulture);
        CapBox.LostKeyboardFocus += (_, _) => ApplyCap();
        CapBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyCap(); };

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
            switch (e.PropertyName)
            {
                case nameof(AppSettings.TrackApps):
                    TrackToggle.IsChecked = s.TrackApps;
                    Refresh(animate: false);
                    break;
                case nameof(AppSettings.QuotaEnabled):
                case nameof(AppSettings.QuotaGB):
                case nameof(AppSettings.QuotaResetDay):
                case nameof(AppSettings.QuotaAlertPercent):
                    if (IsVisible) Refresh(animate: true);
                    break;
            }
        };
        host.Tick += _ => OnTick();
        Loaded += (_, _) => { SyncChart(animate: false); Refresh(animate: true); };
    }

    private void ApplyCap()
    {
        string t = CapBox.Text.Trim();
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double gb) || double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out gb))
            if (gb > 0) _host.Settings.QuotaGB = gb;
        CapBox.Text = _host.Settings.QuotaGB.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private bool Daily => _host.Settings.TrafficChart == "Daily";

    private void SyncChart(bool animate)
    {
        Chart.Visibility = Daily ? Visibility.Collapsed : Visibility.Visible;
        Bars.Visibility = Daily ? Visibility.Visible : Visibility.Collapsed;
        RefreshQuota(animate);
    }

    private void OnTick()
    {
        if (!IsVisible) return;
        var s = _host.Settings;
        Chart.Bits = Bars.Bits = s.SpeedBits;
        Chart.Unit = Bars.Unit = s.SpeedUnit;
        Chart.Push(_host.Latest.UpBps, _host.Latest.DownBps);
        if (++_ticks % 5 == 0) Refresh(animate: false);
    }

    private string Size(double bytes) => UnitFormat.Size(bytes, _host.Settings.SpeedBits, _host.Settings.SpeedUnit);

    private void Refresh(bool animate)
    {
        var s = _host.Settings;
        long from = TrafficFile.RangeStart(s.TrafficRange, s.QuotaResetDay);

        var totals = _host.Totals.Query(from);
        totals.TryGetValue(TrafficFile.TotalKey, out var t);
        UpText.Text = Size(t.Up);
        DownText.Text = Size(t.Down);
        TotalText.Text = Size(t.Up + t.Down);
        RefreshQuota(animate);

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

        string q = AppSearch.Text.Trim();
        Func<(long Up, long Down), long> key = s.AppSort switch
        {
            "Up" => v => v.Up,
            "Down" => v => v.Down,
            _ => v => v.Up + v.Down,
        };
        var all = _apps.Query(from).Where(kv => kv.Key != TrafficFile.TotalKey && kv.Value.Up + kv.Value.Down > 0).ToList();
        double sum = Math.Max(1, all.Sum(kv => (double)kv.Value.Up + kv.Value.Down));
        var rows = all
            .Where(kv => q.Length == 0 || Fuzzy.Score(q, kv.Key) >= 0 || (AppDirectory.Get(kv.Key)?.Description is { Length: > 0 } d && Fuzzy.Score(q, d) >= 0))
            .OrderByDescending(kv => key(kv.Value))
            .Take(AppRows)
            .ToList();
        AppHeader.Content = rows.Count > 0 ? HeaderRow() : null;
        if (rows.Count == 0)
        {
            Apps.Children.Add(Placeholder("暂无数据"));
            return;
        }

        double max = Math.Max(1, key(rows[0].Value));
        int index = 0;
        foreach (var kv in rows)
            Apps.Children.Add(BuildRow(kv.Key, kv.Value.Up, kv.Value.Down, key(kv.Value), max, (kv.Value.Up + kv.Value.Down) / sum, animate, index++));

        // Exe path and description are looked up off the UI thread; rows refresh once something new is known.
        var names = rows.Select(r => r.Key).ToList();
        System.Threading.Tasks.Task.Run(() => AppDirectory.Resolve(names)).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && t.Result) Dispatcher.BeginInvoke(new Action(() => Refresh(animate: false)));
        }, System.Threading.Tasks.TaskScheduler.Default);
    }

    private const double ColValue = 86, ColShare = 54, ColIcon = 34;

    private static Grid Columns()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColIcon) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star), MinWidth = 110 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColValue) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColValue) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColValue) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColShare) });
        return g;
    }

    private static UIElement HeaderRow()
    {
        var g = Columns();
        void Cell(string text, int col, HorizontalAlignment align)
        {
            var t = new TextBlock { Text = text, FontSize = 11.5, HorizontalAlignment = align };
            t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            Grid.SetColumn(t, col);
            g.Children.Add(t);
        }
        Cell("应用", 1, HorizontalAlignment.Left);
        Cell("上传", 3, HorizontalAlignment.Right);
        Cell("下载", 4, HorizontalAlignment.Right);
        Cell("合计", 5, HorizontalAlignment.Right);
        Cell("占比", 6, HorizontalAlignment.Right);
        return g;
    }

    private void RefreshQuota(bool animate)
    {
        var s = _host.Settings;
        var now = DateTime.Now;
        Stats.Children.Clear();

        if (s.QuotaEnabled)
        {
            var r = Quota.Compute(_host.Totals, (long)(s.QuotaGB * GiB), s.QuotaResetDay, now);
            Ring.Active = true;
            Ring.Warn = s.QuotaAlertPercent / 100.0;
            Ring.InvalidateVisual();
            Ring.Show(r.Fraction, animate);
            QuotaLabel.Text = "剩余额度";
            QuotaText.Text = Size(r.Remaining);
            QuotaCard.ToolTip =
                $"账期 {r.Start:M/d} – {r.End.AddDays(-1):M/d}\n已用 {Size(r.Used)} / {Size(r.Cap)}（{r.Fraction:P0}）\n" +
                $"日均 {Size(r.DailyRate)} · 每日可用 {Size(r.DailyBudget)}\n预计账期用量 {Size(r.Projected)}\n剩余 {r.DaysLeft:0.#} 天";
            if (Daily)
            {
                Bars.SetData(r.Days, r.Start, r.TodayIndex, r.Remaining > 0 ? r.DailyBudget : 0, animate);
                Stat("已用", $"{Size(r.Used)} / {Size(r.Cap)}");
                Stat("日均", Size(r.DailyRate));
                Stat("每日可用", Size(r.DailyBudget), "Accent2Brush");
                Stat("预计", Size(r.Projected), r.Projected > r.Cap ? "Accent2Brush" : null);
                if (r.ExhaustAt is { } at) Stat("用完", at.ToString("M/d HH:mm", CultureInfo.InvariantCulture), "Accent2Brush");
                Stat("剩", $"{r.DaysLeft:0.#} 天");
            }
            return;
        }

        Ring.Active = false;
        Ring.Show(0, false);
        Ring.InvalidateVisual();
        QuotaLabel.Text = "额度";
        QuotaText.Text = "—";
        QuotaCard.ToolTip = "设置流量额度";
        if (!Daily) return;

        const int span = 30;
        var first = now.Date.AddDays(-(span - 1));
        long[] days = _host.Totals.Daily(TrafficFile.TotalKey, first, span);
        Bars.SetData(days, first, span - 1, 0, animate);
        var since = _host.Totals.FirstDay(TrafficFile.TotalKey) ?? now.Date;
        var complete = Enumerable.Range(0, span - 1).Where(i => first.AddDays(i) > since).Select(i => days[i]).ToList();
        Stat("30 天", Size(days.Sum()));
        Stat("日均", Size(complete.Count > 0 ? complete.Average() : days[^1]));
        Stat("峰值", Size(days.Max()));
    }

    private void Stat(string label, string value, string? brush = null)
    {
        var l = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 6, 0) };
        l.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var v = new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        if (brush is not null) v.SetResourceReference(TextBlock.ForegroundProperty, brush);
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        p.Children.Add(l);
        p.Children.Add(v);
        Stats.Children.Add(p);
    }

    private UIElement BuildRow(string name, long up, long down, double value, double max, double share, bool animate, int index)
    {
        double ratio = Math.Clamp(value / max, 0.02, 1);
        var meta = AppDirectory.Get(name);
        var grid = Columns();
        grid.Margin = new Thickness(0, 0, 0, 6);
        grid.MinHeight = 34;
        grid.Background = Brushes.Transparent;

        grid.Children.Add(AppIcon(name, meta?.Path));

        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        title.Children.Add(new TextBlock { Text = name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
        if (meta is { Description.Length: > 0 } && !meta.Description.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            var d = new TextBlock { Text = meta.Description, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            d.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            title.Children.Add(d);
        }
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        var track = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var back = new Border { CornerRadius = new CornerRadius(4) };
        back.SetResourceReference(Border.BackgroundProperty, "LineBrush");
        track.Children.Add(back);
        var fill = new Grid { RenderTransformOrigin = new Point(0, 0.5) };
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - ratio + 0.0001, GridUnitType.Star) });
        var parts = new Grid();
        string sort = _host.Settings.AppSort;
        parts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(sort == "Down" ? 0.0001 : Math.Max(up, 1), GridUnitType.Star) });
        parts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(sort == "Up" ? 0.0001 : Math.Max(down, 1), GridUnitType.Star) });
        var upBar = new Border { CornerRadius = sort == "Up" ? new CornerRadius(4) : new CornerRadius(4, 0, 0, 4) };
        upBar.SetResourceReference(Border.BackgroundProperty, "Accent2Brush");
        var downBar = new Border { CornerRadius = sort == "Down" ? new CornerRadius(4) : new CornerRadius(0, 4, 4, 0) };
        downBar.SetResourceReference(Border.BackgroundProperty, "Accent1Brush");
        Grid.SetColumn(downBar, 1);
        parts.Children.Add(upBar);
        parts.Children.Add(downBar);
        fill.Children.Add(parts);
        track.Children.Add(fill);
        Grid.SetColumn(track, 2);
        grid.Children.Add(track);

        Value(grid, 3, Size(up), "Accent2Brush", false);
        Value(grid, 4, Size(down), "Accent1Brush", false);
        Value(grid, 5, Size(up + down), "TextBrush", true);
        Value(grid, 6, share >= 0.001 ? share.ToString("P1", System.Globalization.CultureInfo.InvariantCulture) : "<0.1%", "MutedBrush", false);

        grid.ToolTip = $"{name}{(meta is null ? "" : "\n" + meta.Path)}\n上传 {Size(up)} · 下载 {Size(down)} · 合计 {Size(up + down)} · 占比 {share:P1}";

        if (animate)
        {
            var scale = new ScaleTransform(0, 1);
            fill.RenderTransform = scale;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520))
            {
                BeginTime = TimeSpan.FromMilliseconds(Math.Min(index, 14) * 40),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
        return grid;
    }

    private static void Value(Grid grid, int col, string text, string brush, bool strong)
    {
        var t = new TextBlock
        {
            Text = text, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        Grid.SetColumn(t, col);
        grid.Children.Add(t);
    }

    // Program icon when the executable is known, otherwise the first letter on a tinted tile.
    private UIElement AppIcon(string name, string? path)
    {
        int px = (int)Math.Round(22 * Math.Max(1, VisualTreeHelper.GetDpi(this).DpiScaleX));
        var host = new Grid { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        if (path is not null && ShellIcons.Cached(path, px) is { } cached)
        {
            host.Children.Add(new Image { Source = cached });
            return host;
        }
        var tile = new Border { CornerRadius = new CornerRadius(6) };
        tile.SetResourceReference(Border.BackgroundProperty, "RaisedBrush");
        var letter = new TextBlock { Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?", FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        letter.SetResourceReference(TextBlock.ForegroundProperty, "Accent1Brush");
        tile.Child = letter;
        host.Children.Add(tile);
        if (path is not null)
            ShellIcons.Request(path, px, bmp =>
            {
                if (bmp is not null) Dispatcher.BeginInvoke(new Action(() => { host.Children.Clear(); host.Children.Add(new Image { Source = bmp }); }));
            });
        return host;
    }

    private static TextBlock Placeholder(string text)
    {
        var t = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return t;
    }
}
