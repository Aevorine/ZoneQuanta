using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ZoneQuanta.UI.Controls;

public sealed class RollCell : Grid
{
    private static readonly IEasingFunction Ease = Freeze(new QuadraticEase { EasingMode = EasingMode.EaseOut });

    private readonly TextBlock _a = Make();
    private readonly TextBlock _b = Make();
    private readonly TranslateTransform _ta = new(), _tb = new();
    private TextBlock _cur, _nxt;
    private TranslateTransform _curT, _nxtT;
    private char _value;
    private double _animHeight;
    private DoubleAnimation? _inY, _outY, _inO, _outO;

    public RollCell(char initial, bool separator)
    {
        ClipToBounds = true;
        var ghost = Make();
        ghost.Text = separator ? initial.ToString() : "0";
        ghost.Opacity = 0;
        Children.Add(ghost);

        _a.RenderTransform = _ta;
        _b.RenderTransform = _tb;
        _b.Opacity = 0;
        Children.Add(_a);
        Children.Add(_b);

        _cur = _a; _curT = _ta; _nxt = _b; _nxtT = _tb;
        _value = initial;
        _a.Text = initial.ToString();
        if (separator)
        {
            ghost.Padding = new Thickness(0);
            _a.HorizontalAlignment = HorizontalAlignment.Center;
        }
    }

    public void Set(char c, bool animate)
    {
        if (c == _value) return;
        _value = c;

        if (!animate || ActualHeight <= 0)
        {
            ClearAnimations();
            _cur.Text = c.ToString();
            _cur.Opacity = 1;
            _nxt.Opacity = 0;
            return;
        }

        EnsureAnimations(ActualHeight * 0.55);
        _nxt.Text = c.ToString();
        _nxtT.BeginAnimation(TranslateTransform.YProperty, _inY);
        _nxt.BeginAnimation(OpacityProperty, _inO);
        _curT.BeginAnimation(TranslateTransform.YProperty, _outY);
        _cur.BeginAnimation(OpacityProperty, _outO);

        (_cur, _nxt) = (_nxt, _cur);
        (_curT, _nxtT) = (_nxtT, _curT);
    }

    private void EnsureAnimations(double h)
    {
        if (_inY is not null && Math.Abs(_animHeight - h) < 0.5) return;
        _animHeight = h;
        var d = TimeSpan.FromMilliseconds(190);
        _inY = Slow(new DoubleAnimation(h, 0, d) { EasingFunction = Ease });
        _outY = Slow(new DoubleAnimation(0, -h, d) { EasingFunction = Ease });
        _inO = Slow(new DoubleAnimation(0, 1, d));
        _outO = Slow(new DoubleAnimation(1, 0, d));
    }

    private void ClearAnimations()
    {
        foreach (var t in new[] { _ta, _tb }) t.BeginAnimation(TranslateTransform.YProperty, null);
        foreach (var t in new[] { _a, _b }) t.BeginAnimation(OpacityProperty, null);
        _ta.Y = 0; _tb.Y = 0;
    }

    private static TextBlock Make()
    {
        var t = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Style = null };
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    private static DoubleAnimation Slow(DoubleAnimation a)
    {
        Timeline.SetDesiredFrameRate(a, 30);
        a.Freeze();
        return a;
    }

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}

public sealed class RollingText : StackPanel
{
    private readonly List<RollCell> _cells = new();
    private string _text = string.Empty;

    public RollingText()
    {
        Orientation = Orientation.Horizontal;
    }

    public void SetText(string text, bool animate)
    {
        if (text == _text) return;

        if (text.Length != _text.Length)
        {
            Children.Clear();
            _cells.Clear();
            foreach (char ch in text)
            {
                var cell = new RollCell(ch, !char.IsDigit(ch));
                _cells.Add(cell);
                Children.Add(cell);
            }
        }
        else
        {
            for (int i = 0; i < text.Length; i++) _cells[i].Set(text[i], animate);
        }
        _text = text;
    }
}
