using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using ZoneQuanta.Platform;

namespace ZoneQuanta.Core.Monitor;

public readonly record struct Metrics(double UpBps, double DownBps, double Cpu, double Mem, long UpBytes, long DownBytes, long MemUsed = 0, long MemTotal = 0);

// Not thread-safe: Sample() belongs to the sampler thread. Only AdapterSummary() may be read elsewhere.
public sealed class SystemMetrics
{
    private static readonly string[] VirtualHints = { "tun", "tap", "vpn", "virtual", "vmware", "vethernet", "hyper-v", "wintun", "loopback", "pseudo", "bluetooth" };

    private sealed class Nic
    {
        public NetworkInterface Interface = null!;
        public long Sent, Received;
        public bool HaveBaseline;
    }

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, Nic> _nics = new();
    private long _nicRefreshAt = -1, _lastAt = -1;
    private long _idle, _kernel, _user;
    private bool _haveCpu;
    private volatile string _adapter = string.Empty;

    public Metrics Sample()
    {
        long now = _clock.ElapsedMilliseconds;
        if (_nicRefreshAt < 0 || now - _nicRefreshAt > 30000)
        {
            RefreshInterfaces();
            _nicRefreshAt = now;
        }

        // Deltas are kept per adapter, so a refreshed adapter list (or one adapter appearing / going
        // away) never resets the others: no phantom zero-speed second, and no lost bytes in the totals.
        long dUp = 0, dDown = 0;
        foreach (var nic in _nics.Values)
        {
            try
            {
                var s = nic.Interface.GetIPStatistics();
                long sent = s.BytesSent, received = s.BytesReceived;
                if (nic.HaveBaseline)
                {
                    dUp += Math.Max(0, sent - nic.Sent);
                    dDown += Math.Max(0, received - nic.Received);
                }
                nic.Sent = sent;
                nic.Received = received;
                nic.HaveBaseline = true;
            }
            catch (NetworkInformationException) { nic.HaveBaseline = false; }
        }

        double upBps = 0, downBps = 0;
        if (_lastAt >= 0)
        {
            double sec = Math.Max(0.05, (now - _lastAt) / 1000.0);
            upBps = dUp / sec;
            downBps = dDown / sec;
        }
        _lastAt = now;

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

    public string AdapterSummary() => _adapter;

    private void RefreshInterfaces()
    {
        var fresh = SelectInterfaces();
        var keep = new HashSet<string>();
        NetworkInterface? best = null;
        long speed = -1;
        foreach (var n in fresh)
        {
            keep.Add(n.Id);
            if (_nics.TryGetValue(n.Id, out var existing)) existing.Interface = n;
            else _nics[n.Id] = new Nic { Interface = n };
            try
            {
                if (n.Speed > speed) { speed = n.Speed; best = n; }
            }
            catch (NetworkInformationException) { }
        }
        foreach (string id in _nics.Keys.Where(id => !keep.Contains(id)).ToList()) _nics.Remove(id);

        if (best is null) { _adapter = string.Empty; return; }
        string rate = speed >= 1_000_000_000 ? $"{speed / 1_000_000_000.0:0.#} Gbps" : $"{speed / 1_000_000.0:0} Mbps";
        _adapter = $"{best.Name} · {rate}";
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
