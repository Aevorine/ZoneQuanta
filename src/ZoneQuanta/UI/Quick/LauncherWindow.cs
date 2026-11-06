using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using ZoneQuanta.Platform;
using Forms = System.Windows.Forms;

namespace ZoneQuanta.UI.Quick;

// Borderless topmost pop-up shown on the monitor under the cursor; hides on Esc, when it loses focus, or on a
// three-finger swipe down (which first closes whatever it shows on top: a menu, then OnBack).
public abstract class LauncherWindow : Window
{
    private readonly ScaleTransform _zoom = new(1, 1);
    private readonly TranslateTransform _shift = new();
    private readonly Controls.TouchSwipe _swipe;
    // Focus lost while three fingers are down: the system's own gesture (show desktop) took it; decided once it ends.
    private readonly System.Windows.Threading.DispatcherTimer _lostFocus = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private FrameworkElement? _root;
    private bool _placed;
    private WindowState _shownState = WindowState.Normal;

    protected LauncherWindow(string title)
    {
        Title = title;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.CanResize;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        SetResourceReference(BackgroundProperty, "SurfaceBrush");
        SetResourceReference(FontFamilyProperty, "AppFont");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.RoundCorners(hwnd);
            Native.RemoveMinimizeBox(hwnd);
            MaximizeFit.Attach(HwndSource.FromHwnd(hwnd));
        };
        _swipe = Controls.TouchSwipe.Attach(this, OnSwipe);
        _lostFocus.Tick += (_, _) =>
        {
            _lostFocus.Stop();
            if (IsVisible && !IsActive && !KeepOpen) Dismiss("focus lost during a swipe");
        };
        Deactivated += (_, _) =>
        {
            if (KeepOpen || !IsVisible) return;
            if (_swipe.Busy)
            {
                _lostFocus.Stop();
                _lostFocus.Start();
                return;
            }
            Dismiss("focus lost");
        };
        // Never minimized: if the system does it anyway, back to how it was shown (and hidden, unless a swipe decides).
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
            {
                _shownState = WindowState;
                return;
            }
            WindowState = _shownState;
            if (!_swipe.Busy && !KeepOpen) Dismiss("minimized by the system");
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled) return;
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox box && box != MainInput) return;
            if (!OnEscape()) Dismiss("esc");
            e.Handled = true;
        };
        Closing += (s, e) => { if (!App.Exiting) { e.Cancel = true; Hide(); } };
    }

    // Set while a dialog of the pop-up is open: losing activation to it must not hide the pop-up.
    public bool KeepOpen { get; set; }
    protected System.Windows.Controls.TextBox? MainInput { get; set; }

    protected void SetRoot(FrameworkElement root)
    {
        _root = root;
        root.RenderTransformOrigin = new Point(0.5, 0.3);
        var group = new TransformGroup();
        group.Children.Add(_zoom);
        group.Children.Add(_shift);
        root.RenderTransform = group;
        Content = root;
    }

    // DIP rectangle for this pop-up inside the given work area (DIPs).
    protected abstract Rect Place(Rect work, bool firstTime);

    protected virtual void PlaceOn(Rect work, bool firstTime)
    {
        var r = Place(work, firstTime);
        Left = r.Left;
        Top = r.Top;
        Width = r.Width;
        if (!double.IsNaN(r.Height)) Height = r.Height;
    }

    // Hides, saying why in the log (with the class of the window that took the focus, when it was taken).
    private void Dismiss(string why)
    {
        if (why.StartsWith("focus", StringComparison.Ordinal))
        {
            var cls = new System.Text.StringBuilder(64);
            Native.GetClassName(Native.GetForegroundWindow(), cls, cls.Capacity);
            why += $" to {cls}";
        }
        Core.Log.Write($"{Title} hidden: {why}");
        Hide();
    }

    protected virtual void OnPresented() { }

    protected virtual bool OnEscape() => false;

    // One level back (a three-finger swipe down): true when something inside closed and the pop-up stays.
    protected virtual bool OnBack() => false;

    private void OnSwipe(Core.Input.SwipeDirection d)
    {
        if (d != Core.Input.SwipeDirection.Down || !IsVisible) return;
        // A menu or flyout of this window holds the mouse capture; releasing it closes it.
        bool popup = Mouse.Captured is Visual v && PresentationSource.FromVisual(v) is { } src && src != PresentationSource.FromVisual(this);
        if (popup) Mouse.Capture(null);
        if (popup || OnBack())
        {
            if (!IsActive && !Native.ForceForeground(new WindowInteropHelper(this).Handle))
            {
                // The focus did not come back: rather hidden than left open without it (it would never close).
                _lostFocus.Stop();
                Dismiss("swipe (focus did not return)");
                return;
            }
            Activate();
            return;
        }
        _lostFocus.Stop();
        Dismiss("swipe");
    }

    public void Prewarm()
    {
        new WindowInteropHelper(this).EnsureHandle();
        _root?.Measure(new Size(800, 600));
    }

    public void Toggle()
    {
        if (IsVisible && IsActive) Dismiss("toggle");
        else Present();
    }

    public void Present()
    {
        var work = CursorWorkArea();
        if (!IsVisible)
        {
            PlaceOn(work, !_placed);
            _placed = true;
        }
        Show();
        Activate();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (Native.GetForegroundWindow() != hwnd) Native.SetForegroundWindow(hwnd);
        OnPresented();

        if (_root is null) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var d = TimeSpan.FromMilliseconds(170);
        _zoom.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, d) { EasingFunction = ease });
        _zoom.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, d) { EasingFunction = ease });
        _root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)));
    }

    // Damped horizontal shake: x(t) = A·e^(−t/τ)·sin(ωt), the "no" gesture of macOS login fields.
    protected void Shake()
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(420) };
        for (int i = 0; i <= 12; i++)
        {
            double t = i / 12.0;
            a.KeyFrames.Add(new LinearDoubleKeyFrame(10 * Math.Exp(-t * 4) * Math.Sin(t * Math.PI * 6), KeyTime.FromPercent(t)));
        }
        _shift.BeginAnimation(TranslateTransform.XProperty, a);
    }

    private Rect CursorWorkArea()
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0) scale = 1;
        var px = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
        return new Rect(px.Left / scale, px.Top / scale, px.Width / scale, px.Height / scale);
    }
}
