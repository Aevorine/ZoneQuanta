using System;
using System.Threading;
using System.Windows.Threading;
using ZoneQuanta.Platform;

namespace ZoneQuanta.Core.Time;

// A dedicated thread sleeps on a high-resolution waitable timer and wakes a few milliseconds
// *before* each second boundary, then posts the upcoming second to the UI thread at Send priority.
// The text is therefore already updated when the compositor presents the frame that contains the
// boundary, instead of lagging 15–60 ms behind it as a DispatcherTimer (15.6 ms granularity,
// Normal priority) would.
public sealed class ClockTicker : IDisposable
{
    private const long TicksPerMs = TimeSpan.TicksPerMillisecond;
    private const long Lead = 12 * TicksPerMs;
    private const long Early = 4 * TicksPerMs;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private Thread? _thread;
    private IntPtr _timer;
    private volatile bool _stop;
    private long _pending;
    private int _posted;
    private int _ticks;

    public event Action<DateTimeOffset>? Tick;
    public event Action? Hourly;

    public void Start()
    {
        if (_thread is not null) return;
        _timer = Native.CreateWaitableTimerExW(IntPtr.Zero, null, Native.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, Native.TIMER_ALL_ACCESS);
        _thread = new Thread(Loop) { IsBackground = true, Name = "ZoneQuanta.Clock", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private void Loop()
    {
        long delivered = 0;
        while (!_stop)
        {
            long now = Native.PreciseUtcTicks();
            long boundary = now - now % TimeSpan.TicksPerSecond + TimeSpan.TicksPerSecond;
            if (boundary <= delivered) boundary = delivered + TimeSpan.TicksPerSecond;
            long wake = boundary - Lead;

            if (wake > now) Sleep(wake - now);
            if (_stop) break;

            long woke = Native.PreciseUtcTicks();
            // Woke early (clock stepped back or timer fired short): wait again for the same second.
            if (woke < wake - Early && woke < boundary) continue;
            // Woke far too late (resume from sleep, clock stepped forward): show the current second.
            if (woke >= boundary + TimeSpan.TicksPerSecond) boundary = woke - woke % TimeSpan.TicksPerSecond;

            delivered = boundary;
            Interlocked.Exchange(ref _pending, boundary);
            if (Interlocked.Exchange(ref _posted, 1) == 0)
                _dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(Deliver));
        }
    }

    private void Sleep(long ticks)
    {
        if (_timer != IntPtr.Zero)
        {
            long due = -ticks;
            if (Native.SetWaitableTimer(_timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                Native.WaitForSingleObject(_timer, Native.INFINITE);
                return;
            }
        }
        // Fallback without a high-resolution timer: sleep coarsely, then spin out the remainder.
        long end = Native.PreciseUtcTicks() + ticks;
        while (!_stop)
        {
            long left = end - Native.PreciseUtcTicks();
            if (left <= 0) return;
            if (left > 20 * TicksPerMs) Thread.Sleep((int)(left / TicksPerMs) - 16);
            else Thread.SpinWait(200);
        }
    }

    private void Deliver()
    {
        Volatile.Write(ref _posted, 0);
        long ticks = Interlocked.Exchange(ref _pending, 0);
        if (ticks == 0 || _stop) return;
        var at = new DateTimeOffset(ticks, TimeSpan.Zero);
        if (++_ticks % 300 == 0)
        {
            TimeZoneInfo.ClearCachedData();
            Hourly?.Invoke();
        }
        Tick?.Invoke(at);
    }

    public void Dispose()
    {
        _stop = true;
        if (_timer != IntPtr.Zero)
        {
            long due = -1;
            Native.SetWaitableTimer(_timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false);
        }
        if (_thread is { } t && t != Thread.CurrentThread) t.Join(300);
        if (_timer != IntPtr.Zero)
        {
            Native.CloseHandle(_timer);
            _timer = IntPtr.Zero;
        }
    }
}
