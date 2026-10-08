using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ZoneQuanta.Core.Monitor;
using ZoneQuanta.Core.Settings;
using ZoneQuanta.Core.Traffic;
using ZoneQuanta.Core.Time;
using ZoneQuanta.Core.Update;
using ZoneQuanta.Platform;
using ZoneQuanta.Shell;
using ZoneQuanta.UI.Band;
using ZoneQuanta.UI.Panel;
using ZoneQuanta.UI.Theme;
using ZoneQuanta.UI.Widget;

namespace ZoneQuanta;

public sealed class AppController : IPanelHost, IDisposable
{
    private readonly SettingsStore _store = new();
    private readonly TimeEngine _engine = new();
    private readonly ClockTicker _ticker = new();
    private readonly HotkeyService _hotkey = new();
    private readonly ForegroundWatcher _foreground = new();
    private readonly UpdateCoordinator _updates;
    private readonly SystemMetrics _metrics = new();
    private readonly MetricsSampler _sampler;
    private readonly TotalsRecorder _totals = new();
    private readonly object _gate = new();
    private Dispatcher _dispatcher = null!;
    private Metrics _pending;
    private bool _hasPending, _posted, _envQueued, _fullscreen;
    private BandWindow _band = null!;
    private BandDetailWindow _detail = null!;
    private WidgetWindow _widget = null!;
    private PanelWindow? _panel;
    private TrayService _tray = null!;
    private int _lastMinute = -1;
    private int _topmostTicks;
    private int _trimIn = 20;
    private int _trimTicks;
    private bool _panelWasVisible;

    public AppController()
    {
        _updates = new UpdateCoordinator(_store.Current);
        _sampler = new MetricsSampler(_metrics, _store.Current.SampleIntervalMs);
    }

    public AppSettings Settings => _store.Current;
    public TimeEngine Engine => _engine;
    public UpdateCoordinator Updates => _updates;
    public Metrics Latest { get; private set; }
    public TrafficFile Totals => _totals.File;
    public event Action<DateTimeOffset>? Tick;

    public (double X, double Y) WidgetPosition => (
        double.IsNaN(_widget.Left) ? 0 : _widget.Left,
        double.IsNaN(_widget.Top) ? 0 : _widget.Top);

    public void Start(bool showPanel)
    {
        UpdateService.CleanupLeftovers();

        _dispatcher = Application.Current.Dispatcher;
        var s = Settings;
        s.AutoStart = AutoStart.IsEnabled();
        ThemeManager.Apply(s.Theme);

        _widget = new WidgetWindow(s, _engine);
        _band = new BandWindow(s);
        _detail = new BandDetailWindow(s, _totals.File, _metrics, () => _band.BoundsPx);
        _band.HoverChanged += _detail.SetHover;
        _band.PaintingChanged += _ => ApplySampleRate();
        _band.SettingsRequested += page =>
        {
            _panel ??= new PanelWindow(this);
            _panel.SelectPage(page);
            _panel.ShowPanel();
        };
        _tray = new TrayService(s, TogglePanel, ResetWidget, () => _ = _updates.CheckAsync(), ExitApp);

        ShowPendingUpdateFailure();
        _widget.ApplyInitial(CurrentSecond());
        _band.Sync();
        if (s.TrackApps) _ = StartTrackingHelperAsync();
        s.PropertyChanged += OnSettingChanged;
        _hotkey.Pressed += TogglePanel;
        RegisterHotkey();

        _updates.UpdateFound += info => _tray.Notify("发现新版本", $"ZoneQuanta v{info.Version} 可用，在面板「更新」页安装");
        _updates.PreparingToApply += () => ((App)Application.Current).PrepareExit();

        _ticker.Tick += OnTick;
        _ticker.Hourly += _engine.Refresh;
        _ticker.Start();
        _sampler.Sampled += OnSampled;
        _sampler.Start();
        _foreground.Changed += QueueEnvironment;
        _foreground.Start();
        OnTick(CurrentSecond());

        if (showPanel) ShowPanel();
        _dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Prewarm));
        if (s.AutoCheckUpdate)
        {
            var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            delay.Tick += async (_, _) =>
            {
                delay.Stop();
                await _updates.CheckAsync();
            };
            delay.Start();
        }
    }

    // Window creation is the slow part of the first hover and the first panel open: do it while idle.
    private void Prewarm()
    {
        _detail.Prewarm();
        if (_panel is null)
        {
            _panel = new PanelWindow(this);
            _trimIn = 6;
        }
    }

    private async Task StartTrackingHelperAsync()
    {
        if (await HelperLauncher.StartIfInstalledAsync() != HelperState.Missing) return;
        // The scheduled task is gone (removed by hand or by a cleanup tool): say so instead of
        // leaving the switch on while nothing is being counted.
        Settings.TrackApps = false;
        _tray.Notify("应用流量统计已关闭", "后台助手已不存在，可在「流量」页重新开启");
    }

    public void ShowPanel()
    {
        _panel ??= new PanelWindow(this);
        _panel.ShowPanel();
    }

    public void TogglePanel()
    {
        _panel ??= new PanelWindow(this);
        _panel.TogglePanel();
    }

    public void MoveWidget(double x, double y) => _widget.MoveTo(x, y);
    public void AnchorWidget(string where) => _widget.Anchor(where);

    public void ResetWidget()
    {
        _widget.Anchor("TR");
    }

    public async System.Threading.Tasks.Task<bool> SetTrackingAsync(bool on)
    {
        if (on)
        {
            bool ok = await HelperLauncher.EnableAsync();
            Settings.TrackApps = ok;
            return ok;
        }
        await HelperLauncher.DisableAsync();
        Settings.TrackApps = false;
        return true;
    }

    public void OpenSettingsFolder()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZoneQuanta");
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    private void ShowPendingUpdateFailure()
    {
        string marker = UpdateService.FailureMarker;
        try
        {
            if (!File.Exists(marker)) return;
            string text = File.ReadAllText(marker).Trim();
            File.Delete(marker);
            _tray.Notify("更新失败", text.Length > 0 ? text : "请稍后重试");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void ExitApp()
    {
        ((App)Application.Current).ExitApp();
    }

    public void Dispose()
    {
        _ticker.Dispose();
        _sampler.Dispose();
        _foreground.Dispose();
        _store.Flush();
        _totals.Flush();
        _hotkey.Dispose();
        _tray.Dispose();
        _detail.Close();
        _band.Close();
        _widget.Close();
        _panel?.Close();
    }

    // Fast sampling only pays off while something on screen shows the numbers. Otherwise a one-second
    // pass still keeps the daily totals exact, at a fraction of the wake-ups.
    private void ApplySampleRate()
    {
        int wanted = Settings.SampleIntervalMs;
        if (!_band.IsPainting && _panel?.IsVisible != true) wanted = Math.Max(wanted, 1000);
        if (_sampler.IntervalMs != wanted) _sampler.IntervalMs = wanted;
    }

    private static DateTimeOffset CurrentSecond()
    {
        long ticks = Native.PreciseUtcTicks();
        return new DateTimeOffset(ticks - ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
    }

    // Runs on the UI thread at the top of every second. The clock is drawn first: nothing after it
    // (environment checks, panel refresh, memory trim) may delay the new second.
    private void OnTick(DateTimeOffset now)
    {
        if (!_fullscreen) _widget.Tick(now);
        Tick?.Invoke(now);

        EvaluateEnvironment();
        _widget.Revive();
        _band.Revive();
        _panel?.Revive();
        if (!_fullscreen && ++_topmostTicks >= 3)
        {
            _topmostTicks = 0;
            _widget.ReassertTopmost();
        }

        bool panelVisible = _panel?.IsVisible == true;
        if (_panelWasVisible && !panelVisible) _trimIn = 4;
        if (panelVisible != _panelWasVisible) ApplySampleRate();
        _panelWasVisible = panelVisible;
        if (++_trimTicks % 600 == 0 && _trimIn == 0) _trimIn = 1;
        if (_trimIn > 0 && --_trimIn == 0 && !panelVisible) MemoryTrim.Run();

        int minute = now.Minute;
        if (minute != _lastMinute)
        {
            _lastMinute = minute;
            _tray.SetTooltip(BuildTooltip(now));
        }
    }

    // Fullscreen hiding and the desktop lift. Besides the per-second pass this also runs the moment the
    // foreground window changes or resizes, so the widget gets out of a game's way (and back) at once.
    private void EvaluateEnvironment()
    {
        _envQueued = false;
        bool fullscreen = Settings.HideOnFullscreen && FullscreenWatcher.IsFullscreenActive();
        if (fullscreen != _fullscreen)
        {
            _fullscreen = fullscreen;
            if (!fullscreen) _widget.Refresh(CurrentSecond()); // reappear on the current second, not the one it hid at
            _widget.SetSuppressed(fullscreen);
            _band.SetSuppressed(fullscreen);
        }
        _widget.SetDesktopLift(FullscreenWatcher.IsDesktopForeground());
    }

    private void QueueEnvironment()
    {
        if (_envQueued) return;
        _envQueued = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(EvaluateEnvironment));
    }

    // Sampler thread. Rates are replaced by the newest sample, while byte counts are summed so that a
    // busy UI thread coalescing two samples never loses traffic from the daily total.
    private void OnSampled(Metrics m)
    {
        lock (_gate)
        {
            _pending = _hasPending ? m with { UpBytes = m.UpBytes + _pending.UpBytes, DownBytes = m.DownBytes + _pending.DownBytes } : m;
            _hasPending = true;
            if (_posted) return;
            _posted = true;
        }
        _dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(ApplyMetrics));
    }

    private void ApplyMetrics()
    {
        Metrics m;
        lock (_gate)
        {
            m = _pending;
            _hasPending = false;
            _posted = false;
        }
        Latest = m;
        _totals.Add(m);
        _band.Update(m, _totals.File.TodayTotal());
        _detail.Update(m);
    }

    private string BuildTooltip(DateTimeOffset now)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (var z in Settings.Zones)
        {
            var snap = _engine.Compute(z, now);
            parts.Add($"{snap.Label} {snap.Time:HH:mm}");
        }
        return string.Join(" · ", parts);
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.AutoStart):
                AutoStart.Set(Settings.AutoStart);
                break;
            case nameof(AppSettings.Theme):
                ThemeManager.Apply(Settings.Theme);
                break;
            case nameof(AppSettings.HideOnFullscreen):
                EvaluateEnvironment();
                break;
            case nameof(AppSettings.SampleIntervalMs):
                ApplySampleRate();
                break;
            case nameof(AppSettings.HotkeyEnabled):
            case nameof(AppSettings.HotkeyModifiers):
            case nameof(AppSettings.HotkeyKey):
                RegisterHotkey();
                break;
        }
    }

    private void RegisterHotkey()
    {
        var s = Settings;
        if (!s.HotkeyEnabled)
        {
            _hotkey.Unregister();
            return;
        }
        if (!_hotkey.Register(s.HotkeyModifiers, s.HotkeyKey))
            _tray.Notify("快捷键不可用", "该组合已被其他程序占用，请在「系统」页更换");
    }
}
