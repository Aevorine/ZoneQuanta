using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ZoneQuanta.UI.Controls;

// Ring gauge for "share of the data cap used"; the arc eases to each new value.
public sealed class QuotaRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(QuotaRing), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private double _target = double.NaN;

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Warn { get; set; } = 0.8;
    public bool Active { get; set; }

    public void Show(double fraction, bool animate)
    {
        if (fraction.Equals(_target)) return;
        _target = fraction;
        if (!animate)
        {
            BeginAnimation(ValueProperty, null);
            Value = fraction;
            return;
        }
        BeginAnimation(ValueProperty, new DoubleAnimation(fraction, TimeSpan.FromMilliseconds(700)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size < 8) return;
        double thick = Math.Max(4, size * 0.11), r = (size - thick) / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);

        var track = (Brush)(TryFindResource("LineBrush") ?? Brushes.DimGray);
        dc.DrawEllipse(null, new Pen(track, thick), c, r, r);

        double v = Math.Max(0, Value);
        if (Active && v > 0)
        {
            string key = v >= 1 ? "Accent2Brush" : v >= Warn ? "SunBrush" : "Accent1Brush";
            var pen = new Pen((Brush)(TryFindResource(key) ?? Brushes.Teal), thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            double sweep = Math.Min(v, 0.9999) * 2 * Math.PI;
            var start = new Point(c.X, c.Y - r);
            var end = new Point(c.X + r * Math.Sin(sweep), c.Y - r * Math.Cos(sweep));
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(start, false, false);
                ctx.ArcTo(end, new Size(r, r), 0, sweep > Math.PI, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }

        var text = Active ? (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%" : "—";
        var fg = (Brush)(TryFindResource("TextBrush") ?? Brushes.White);
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface((FontFamily)(TryFindResource("AppFont") ?? new FontFamily("Segoe UI")), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            Math.Max(9, size * 0.24), fg, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
    }
}
