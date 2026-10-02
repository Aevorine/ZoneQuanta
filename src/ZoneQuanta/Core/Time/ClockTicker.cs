using System;
using System.Windows.Threading;

namespace ZoneQuanta.Core.Time;

public sealed class ClockTicker
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal);
    private int _ticks;

    public event Action<DateTimeOffset>? Tick;
    public event Action? Hourly;

    public ClockTicker()
    {
        _timer.Tick += OnTick;
    }

    public void Start() => Schedule();

    private void Schedule()
    {
        int wait = 1000 - DateTimeOffset.UtcNow.Millisecond + 4;
        _timer.Interval = TimeSpan.FromMilliseconds(wait);
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        var now = DateTimeOffset.UtcNow.AddMilliseconds(60);
        now = new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
        if (++_ticks % 300 == 0)
        {
            TimeZoneInfo.ClearCachedData();
            Hourly?.Invoke();
        }
        Tick?.Invoke(now);
        Schedule();
    }
}
