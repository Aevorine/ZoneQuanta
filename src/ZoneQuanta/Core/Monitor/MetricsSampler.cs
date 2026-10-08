using System;
using System.Diagnostics;
using System.Threading;

namespace ZoneQuanta.Core.Monitor;

// Samples network / CPU / memory on its own thread so the (comparatively slow) adapter queries never
// delay the clock on the UI thread, and so the taskbar band can refresh faster than once a second.
public sealed class MetricsSampler : IDisposable
{
    private readonly SystemMetrics _metrics;
    private readonly ManualResetEventSlim _wake = new(false);
    private Thread? _thread;
    private volatile bool _stop;
    private volatile int _intervalMs;

    public event Action<Metrics>? Sampled;

    public MetricsSampler(SystemMetrics metrics, int intervalMs)
    {
        _metrics = metrics;
        _intervalMs = Clamp(intervalMs);
    }

    public int IntervalMs
    {
        get => _intervalMs;
        set
        {
            _intervalMs = Clamp(value);
            _wake.Set();
        }
    }

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "ZoneQuanta.Metrics" };
        _thread.Start();
    }

    private void Loop()
    {
        var clock = Stopwatch.StartNew();
        long next = 0;
        while (!_stop)
        {
            try { Sampled?.Invoke(_metrics.Sample()); }
            catch (Exception ex) when (ex is System.Net.NetworkInformation.NetworkInformationException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Log.Error("sample", ex);
            }

            long now = clock.ElapsedMilliseconds;
            next += _intervalMs;
            if (next < now) next = now + _intervalMs;
            int wait = (int)Math.Max(1, next - now);
            if (_wake.Wait(wait))
            {
                _wake.Reset();
                next = clock.ElapsedMilliseconds + _intervalMs;
            }
        }
    }

    private static int Clamp(int ms) => Math.Clamp(ms, 100, 5000);

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        if (_thread is { } t && t != Thread.CurrentThread) t.Join(300);
    }
}
