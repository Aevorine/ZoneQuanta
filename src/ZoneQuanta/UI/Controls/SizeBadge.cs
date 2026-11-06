using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace ZoneQuanta.UI.Controls;

// Live pixel size in the middle of a window while one of its edges is dragged; fades out after release.
public sealed class SizeBadge : Border
{
    private readonly TextBlock _text = new() { FontSize = 20, FontWeight = FontWeights.SemiBold };

    public SizeBadge()
    {
        _text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        System.Windows.Documents.Typography.SetNumeralAlignment(_text, FontNumeralAlignment.Tabular);
        Child = _text;
        CornerRadius = new CornerRadius(12);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(20, 10, 20, 11);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        Opacity = 0;
        SetResourceReference(BackgroundProperty, "RaisedBrush");
        SetResourceReference(BorderBrushProperty, "LineBrush");
    }

    // A short result shown the same way ("导入 148"), fading out after a moment.
    public void Flash(string text)
    {
        _text.Text = text;
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(1600) });
    }

    public void Attach(WindowMemory memory)
    {
        memory.Resizing += px =>
        {
            _text.Text = $"{px.Width} × {px.Height}";
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
        };
        memory.ResizeEnded += () => BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)) { BeginTime = TimeSpan.FromMilliseconds(650) });
    }
}
