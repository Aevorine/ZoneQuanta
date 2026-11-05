using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ZoneQuanta.Core.Monitor;

namespace ZoneQuanta.UI.Controls;

// Per-day usage bars with an optional "daily allowance" line; bars grow in when the data changes.
public sealed class DailyBars : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(DailyBars), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly CultureInfo Zh = CultureInfo.GetCultureInfo("zh-CN");
    private long[] _values = Array.Empty<long>();
    private DateTime _first;
    private int _today = -1, _hover = -1;
    private double _budget;

    public DailyBars()
    {
        ToolTipService.SetInitialShowDelay(this, 0);
        ToolTipService.SetBetweenShowDelay(this, 0);
    }

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool Bits { get; set; }
    public string Unit { get; set; } = "Auto";

    public void SetData(long[] values, DateTime first, int todayIndex, double budget, bool animate)
    {
        bool same = first == _first && todayIndex == _today && budget.Equals(_budget) && values.AsSpan().SequenceEqual(_values);
        if (same && !animate) return;
        _values = values;
        _first = first;
        _today = todayIndex;
        _budget = budget;
        if (animate) BeginAnimation(ProgressProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(560)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        else InvalidateVisual();
    }

    private double Scale()
    {
        long max = 1;
        foreach (long v in _values) max = Math.Max(max, v);
        return Math.Max(max, _budget) * 1.12;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        int n = _values.Length;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (w < 20 || h < 30 || n == 0) return;

        const double top = 18, bottom = 18;
        double plot = h - top - bottom, slot = w / n, barW = Math.Max(2, slot * 0.62), scale = Scale();
        var line = (Brush)(TryFindResource("LineBrush") ?? Brushes.DimGray);
        var muted = (Brush)(TryFindResource("MutedBrush") ?? Brushes.Gray);
        var a1 = (SolidColorBrush)(TryFindResource("Accent1Brush") ?? Brushes.Teal);
        var a2 = (Brush)(TryFindResource("Accent2Brush") ?? Brushes.Orange);
        var past = new SolidColorBrush(Color.FromArgb(150, a1.Color.R, a1.Color.G, a1.Color.B));
        past.Freeze();
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var grid = new Pen(line, 1) { DashStyle = DashStyles.Dot };
        for (int i = 0; i <= 3; i++)
        {
            double y = top + plot * i / 4;
            dc.DrawLine(grid, new Point(0, y), new Point(w, y));
        }
        dc.DrawLine(new Pen(line, 1), new Point(0, top + plot), new Point(w, top + plot));

        for (int i = 0; i < n; i++)
        {
            double x = i * slot + (slot - barW) / 2;
            if (_today >= 0 && i > _today)
            {
                dc.DrawRoundedRectangle(line, null, new Rect(x, top + plot - 2, barW, 2), 1, 1);
                continue;
            }
            double bh = Math.Max(_values[i] > 0 ? 2 : 0, _values[i] / scale * plot * Progress);
            if (bh <= 0) continue;
            Brush fill = i == _today || i == _hover ? a1 : past;
            double rad = Math.Min(3, barW / 2);
            dc.DrawRoundedRectangle(fill, null, new Rect(x, top + plot - bh, barW, bh), rad, rad);
        }

        if (_budget > 0)
        {
            double y = top + plot - _budget / scale * plot;
            dc.DrawLine(new Pen(a2, 1.4) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) }, new Point(0, y), new Point(w, y));
            var bt = Text(UnitFormat.Size(_budget, Bits, Unit), 10.5, a2, dip);
            dc.DrawText(bt, new Point(w - bt.Width - 2, Math.Max(0, y - bt.Height - 1)));
        }

        dc.DrawText(Text(UnitFormat.Size(scale / 1.12, Bits, Unit), 10.5, muted, dip), new Point(2, 0));

        int step = Math.Max(1, (int)Math.Ceiling(n / Math.Max(1, w / 44)));
        for (int i = 0; i < n; i += step) Label(dc, i, slot, top + plot + 3, muted, dip);
        if (_today >= 0 && _today < n) Label(dc, _today, slot, top + plot + 3, a1, dip);
    }

    private void Label(DrawingContext dc, int i, double slot, double y, Brush brush, double dip)
    {
        var t = Text(_first.AddDays(i).Day.ToString(CultureInfo.InvariantCulture), 10.5, brush, dip);
        dc.DrawText(t, new Point(i * slot + slot / 2 - t.Width / 2, y));
    }

    private FormattedText Text(string s, double size, Brush brush, double dip) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface((FontFamily)(TryFindResource("AppFont") ?? new FontFamily("Segoe UI")), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush, dip);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int n = _values.Length;
        if (n == 0) return;
        int i = Math.Clamp((int)(e.GetPosition(this).X / (ActualWidth / n)), 0, n - 1);
        if (i == _hover) return;
        _hover = i;
        var day = _first.AddDays(i);
        ToolTip = _today >= 0 && i > _today
            ? day.ToString("M月d日 ddd", Zh)
            : $"{day.ToString("M月d日 ddd", Zh)}  {UnitFormat.Size(_values[i], Bits, Unit)}";
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        InvalidateVisual();
    }
}
