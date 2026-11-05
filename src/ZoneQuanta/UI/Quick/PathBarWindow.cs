using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ZoneQuanta.Core.Quick;
using ZoneQuanta.Platform;
using ZoneQuanta.UI.Controls;
using Shapes = System.Windows.Shapes;

namespace ZoneQuanta.UI.Quick;

// "Go to path" bar: type or paste a path, get completions, Enter opens it.
public sealed class PathBarWindow : LauncherWindow
{
    private const int Limit = 8;
    private readonly QuickActions _actions;
    private readonly TextBox _box = new() { FontSize = 17 };
    private readonly ListBox _list = new();
    private CancellationTokenSource? _cts;
    private bool _navigated;
    private int _iconPx = 20;

    public PathBarWindow(QuickActions actions) : base("ZoneQuanta Path")
    {
        _actions = actions;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        _box.SetResourceReference(StyleProperty, "IconField");
        _box.Tag = FindResource("Icon.Path");
        MainInput = _box;
        _box.MinHeight = 50;
        _list.SetResourceReference(StyleProperty, "PickList");
        _list.Margin = new Thickness(0, 8, 0, 0);
        _list.MaxHeight = 8 * 50;
        _list.Visibility = Visibility.Collapsed;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(_box);
        root.Children.Add(_list);
        SetRoot(root);

        _box.TextChanged += (_, _) => { _navigated = false; Suggest(); };
        _box.PreviewKeyDown += OnKey;
        _list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (Pick(e.OriginalSource) is { } s) OpenSuggestion(s);
        };
        actions.Failed += _ => { if (IsActive) Fail(); };
        actions.Opened += () => { if (IsVisible) Hide(); };
    }

    protected override Rect Place(Rect work, bool firstTime)
    {
        double w = Math.Clamp(work.Width * 0.42, 520, 780);
        return new Rect(work.Left + (work.Width - w) / 2, work.Top + work.Height * 0.22, w, double.NaN);
    }

    protected override void OnPresented()
    {
        _iconPx = (int)Math.Round(20 * Math.Max(1, VisualTreeHelper.GetDpi(this).DpiScaleX));
        _box.Focus();
        _box.SelectAll();
        Suggest();
    }

    protected override bool OnEscape()
    {
        if (_box.Text.Length == 0) return false;
        _box.Clear();
        return true;
    }

    private KeyChord Chord(string name) => KeyChord.Parse(_actions.Settings.LocalKey(name));

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (Chord("Complete").Matches(e))
        {
            if (Selected() is { } s) Complete(s);
            e.Handled = true;
            return;
        }
        if (Chord("Reveal").Matches(e))
        {
            if (Target() is { } reveal && QuickActions.Exists(reveal)) { ShellLauncher.Reveal(reveal); Hide(); }
            else Fail();
            e.Handled = true;
            return;
        }
        if (Chord("AddQuick").Matches(e))
        {
            if (Target() is { } add && _actions.Add(new[] { add }) is { } added && (added.Count > 0 || _actions.Store.Find(add) is not null)) Hide();
            else Fail();
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.Down:
            case Key.Up:
                if (_list.Items.Count == 0) return;
                int n = _list.Items.Count, i = _list.SelectedIndex;
                _list.SelectedIndex = e.Key == Key.Down ? (i + 1) % n : (i <= 0 ? n - 1 : i - 1);
                _list.ScrollIntoView(_list.SelectedItem);
                _navigated = true;
                break;
            case Key.Enter when Keyboard.Modifiers == ModifierKeys.None:
                if (PreferSuggestion && Selected() is { } pick) OpenSuggestion(pick);
                else _actions.OpenText(_box.Text);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private string? Target()
    {
        if (PreferSuggestion && Selected() is { } s) return s.Target;
        var t = PathEngine.Resolve(_box.Text);
        return t.Kind is TargetKind.File or TargetKind.Directory or TargetKind.Shell ? t.Value : null;
    }

    private bool PreferSuggestion => _navigated || !PathEngine.LooksLikePath(_box.Text);

    private void OpenSuggestion(Suggestion s)
    {
        if (s.Item is { } item) _actions.Open(item);
        else _actions.OpenText(s.Target);
    }

    private void Complete(Suggestion s)
    {
        string text = s.Target;
        if (s.IsDirectory && !PathEngine.IsShell(text) && !text.EndsWith('\\')) text += "\\";
        _box.Text = text;
        _box.CaretIndex = text.Length;
    }

    private Suggestion? Selected() => (_list.SelectedItem as FrameworkElement)?.Tag as Suggestion;

    private static Suggestion? Pick(object source)
    {
        for (var d = source as DependencyObject; d is not null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement { Tag: Suggestion s }) return s;
        return null;
    }

    // Suggestions are computed off the UI thread; a newer keystroke cancels the older query.
    private void Suggest()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        string text = _box.Text;
        var items = _actions.Store.Items.ToArray();
        var history = _actions.Store.Data.History.ToArray();
        Task.Run(() => PathEngine.Suggest(text, items, history, Limit, cts.Token), cts.Token)
            .ContinueWith(t =>
            {
                if (cts.IsCancellationRequested || !t.IsCompletedSuccessfully) return;
                Dispatcher.BeginInvoke(new Action(() => { if (!cts.IsCancellationRequested) Show(t.Result); }));
            }, TaskScheduler.Default);
    }

    private void Show(List<Suggestion> list)
    {
        _list.Items.Clear();
        foreach (var s in list) _list.Items.Add(Row(s));
        _list.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (list.Count > 0) _list.SelectedIndex = 0;
    }

    private FrameworkElement Row(Suggestion s)
    {
        var icon = new Grid { Width = 20, Height = 20, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        if (PathEngine.IsUrl(s.Target)) icon.Children.Add(Glyph("Icon.Open"));
        else if (ShellIcons.Cached(s.Target, _iconPx) is { } cached) icon.Children.Add(new Image { Source = cached });
        else
        {
            icon.Children.Add(Glyph(s.IsDirectory ? "Icon.Folder" : "Icon.File"));
            int px = _iconPx;
            ShellIcons.Request(s.Target, px, bmp =>
            {
                if (bmp is null) return;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    icon.Children.Clear();
                    var img = new Image { Source = bmp, Opacity = 0 };
                    icon.Children.Add(img);
                    img.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
                }));
            });
        }

        var title = new TextBlock { Text = s.Title, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
        var detail = new TextBlock { Text = s.Detail, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        if (s.Detail.Length > 0) text.Children.Add(detail);

        var g = new Grid { Tag = s, ToolTip = s.Target };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(icon);
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        return g;
    }

    private Shapes.Path Glyph(string key)
    {
        var p = new Shapes.Path { Data = (Geometry)FindResource(key), Width = 16, Height = 16 };
        p.SetResourceReference(StyleProperty, "IconPath");
        p.SetResourceReference(Shapes.Shape.StrokeProperty, "MutedBrush");
        return p;
    }

    private void Fail()
    {
        Shake();
        _box.ApplyTemplate();
        if (_box.Template.FindName("Bg", _box) is Border bg)
        {
            bg.SetResourceReference(Border.BorderBrushProperty, "Accent2Brush");
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            t.Tick += (_, _) => { t.Stop(); bg.ClearValue(Border.BorderBrushProperty); };
            t.Start();
        }
    }
}
