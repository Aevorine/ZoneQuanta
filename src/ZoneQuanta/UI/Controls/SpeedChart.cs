using System;
using System.Windows;
using System.Windows.Media;
using ZoneQuanta.Core.Monitor;

namespace ZoneQuanta.UI.Controls;

public sealed class SpeedChart : FrameworkElement
{
    private const int Capacity = 60;
    private readonly double[] _up = new double[Capacity], _down = new double[Capacity];
    private int _count;
    private double _scale = 1024;

    public bool Bits { get; set; }
    public string Unit { get; set; } = "Auto";

    public void Push(double up, double down)
    {
        if (_count < Capacity) _count++;
        Array.Copy(_up, 1, _up, 0, Capacity - 1);
        Array.Copy(_down, 1, _down, 0, Capacity - 1);
        _up[^1] = up;
        _down[^1] = down;

        double peak = 1024;
        for (int i = Capacity - _count; i < Capacity; i++) peak = Math.Max(peak, Math.Max(_up[i], _down[i]));
        _scale += (peak * 1.15 - _scale) * 0.35;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 10 || h < 10) return;

        var line = (Brush)(TryFindResource("LineBrush") ?? Brushes.DimGray);
        var muted = (Brush)(TryFindResource("MutedBrush") ?? Brushes.Gray);
        var c1 = (SolidColorBrush)(TryFindResource("Accent1Brush") ?? Brushes.Teal);
        var c2 = (SolidColorBrush)(TryFindResource("Accent2Brush") ?? Brushes.Orange);

        var grid = new Pen(line, 1) { DashStyle = DashStyles.Dot };
        for (int i = 1; i <= 3; i++)
        {
            double y = h * i / 4;
            dc.DrawLine(grid, new Point(0, y), new Point(w, y));
        }

        Series(dc, _down, c1, w, h);
        Series(dc, _up, c2, w, h);

        var label = new FormattedText(UnitFormat.Speed(_scale / 1.15, Bits, Unit), System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, muted, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(label, new Point(6, 2));
    }

    private void Series(DrawingContext dc, double[] data, SolidColorBrush color, double w, double h)
    {
        if (_count < 2) return;
        double step = w / (Capacity - 1);
        var pts = new Point[Capacity];
        for (int i = 0; i < Capacity; i++)
            pts[i] = new Point(i * step, h - 4 - Math.Min(1, data[i] / _scale) * (h - 12));

        int first = Capacity - _count;
        var curve = new StreamGeometry();
        using (var ctx = curve.Open())
        {
            ctx.BeginFigure(pts[first], false, false);
            for (int i = first + 1; i < Capacity; i++)
            {
                var prev = pts[i - 1];
                var cur = pts[i];
                double mx = (prev.X + cur.X) / 2;
                ctx.BezierTo(new Point(mx, prev.Y), new Point(mx, cur.Y), cur, true, true);
            }
        }
        curve.Freeze();

        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(pts[first].X, h), true, true);
            ctx.LineTo(pts[first], false, false);
            for (int i = first + 1; i < Capacity; i++)
            {
                var prev = pts[i - 1];
                var cur = pts[i];
                double mx = (prev.X + cur.X) / 2;
                ctx.BezierTo(new Point(mx, prev.Y), new Point(mx, cur.Y), cur, false, true);
            }
            ctx.LineTo(new Point(pts[^1].X, h), false, false);
        }
        area.Freeze();

        var fill = new LinearGradientBrush(
            Color.FromArgb(90, color.Color.R, color.Color.G, color.Color.B),
            Color.FromArgb(0, color.Color.R, color.Color.G, color.Color.B), 90);
        fill.Freeze();
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(color, 1.8) { LineJoin = PenLineJoin.Round }, curve);
    }
}
