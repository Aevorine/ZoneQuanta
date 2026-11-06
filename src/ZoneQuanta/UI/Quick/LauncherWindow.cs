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

// Borderless topmost pop-up shown on the monitor under the cursor; hides on Esc or when it loses focus.
public abstract class LauncherWindow : Window
{
    private readonly ScaleTransform _zoom = new(1, 1);
    private readonly TranslateTransform _shift = new();
    private FrameworkElement? _root;
    private bool _placed;

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
            MaximizeFit.Attach(HwndSource.FromHwnd(hwnd));
        };
        Deactivated += (_, _) => { if (!KeepOpen && IsVisible) Hide(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled) return;
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox box && box != MainInput) return;
            if (!OnEscape()) Hide();
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

    protected virtual void OnPresented() { }

    protected virtual bool OnEscape() => false;

    public void Prewarm()
    {
        new WindowInteropHelper(this).EnsureHandle();
        _root?.Measure(new Size(800, 600));
    }

    public void Toggle()
    {
        if (IsVisible && IsActive) Hide();
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
