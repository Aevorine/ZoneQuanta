using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using ZoneQuanta.Platform;

namespace ZoneQuanta.Core.Monitor;

public readonly record struct Metrics(double UpBps, double DownBps, double Cpu, double Mem, long UpBytes, long DownBytes, long MemUsed = 0, long MemTotal = 0);

public sealed class SystemMetrics
{
    private static readonly string[] VirtualHints = { "tun", "tap", "vpn", "virtual", "vmware", "vethernet", "hyper-v", "wintun", "loopback", "pseudo", "bluetooth" };

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private List<NetworkInterface> _nics = new();
    private long _nicRefreshAt = -1;
    private long _lastTicks;
    private long _lastUp, _lastDown;
    private bool _haveNet;
    private long _idle, _kernel, _user;
    private bool _haveCpu;

    public Metrics Sample()
    {
        long now = _clock.ElapsedMilliseconds;
        if (_nicRefreshAt < 0 || now - _nicRefreshAt > 30000)
        {
            _nics = SelectInterfaces();
            _nicRefreshAt = now;
            _haveNet = false;
        }

        long up = 0, down = 0;
        foreach (var nic in _nics)
        {
            try
            {
                var s = nic.GetIPStatistics();
                up += s.BytesSent;
                down += s.BytesReceived;
            }
            catch (NetworkInformationException) { }
        }

        long dUp = 0, dDown = 0;
        double upBps = 0, downBps = 0;
        if (_haveNet)
        {
            double sec = Math.Max(0.2, (now - _lastTicks) / 1000.0);
            dUp = Math.Max(0, up - _lastUp);
            dDown = Math.Max(0, down - _lastDown);
            upBps = dUp / sec;
            downBps = dDown / sec;
        }
        _lastUp = up; _lastDown = down; _lastTicks = now; _haveNet = true;

        var (memPercent, memUsed, memTotal) = SampleMem();
        return new Metrics(upBps, downBps, SampleCpu(), memPercent, dUp, dDown, memUsed, memTotal);
    }

    private double SampleCpu()
    {
        if (!Native.GetSystemTimes(out long idle, out long kernel, out long user)) return 0;
        double result = 0;
        if (_haveCpu)
        {
            long dIdle = idle - _idle, dTotal = (kernel - _kernel) + (user - _user);
            if (dTotal > 0) result = Math.Clamp(100.0 * (1.0 - (double)dIdle / dTotal), 0, 100);
        }
        _idle = idle; _kernel = kernel; _user = user; _haveCpu = true;
        return result;
    }

    private static (double Percent, long Used, long Total) SampleMem()
    {
        var m = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        if (!Native.GlobalMemoryStatusEx(ref m)) return (0, 0, 0);
        return (m.dwMemoryLoad, (long)(m.ullTotalPhys - m.ullAvailPhys), (long)m.ullTotalPhys);
    }

    public string AdapterSummary()
    {
        NetworkInterface? best = null;
        long speed = -1;
        foreach (var n in _nics)
        {
            try
            {
                if (n.Speed > speed) { speed = n.Speed; best = n; }
            }
            catch (NetworkInformationException) { }
        }
        if (best is null) return string.Empty;
        string rate = speed >= 1_000_000_000 ? $"{speed / 1_000_000_000.0:0.#} Gbps" : $"{speed / 1_000_000.0:0} Mbps";
        return $"{best.Name} · {rate}";
    }

    private static List<NetworkInterface> SelectInterfaces()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                            && !VirtualHints.Any(h => n.Name.Contains(h, StringComparison.OrdinalIgnoreCase)
                                                      || n.Description.Contains(h, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return new List<NetworkInterface>();
        }
    }
}
