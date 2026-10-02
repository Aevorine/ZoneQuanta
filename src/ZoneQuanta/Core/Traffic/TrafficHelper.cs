using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using ZoneQuanta.Core.Monitor;

namespace ZoneQuanta.Core.Traffic;

public static class TrafficHelper
{
    public const string StopFileName = "traffic.stop";
    public const string AppsFileName = "apps.tsv";
    private static readonly Guid KernelNetwork = new("7DD42A49-5329-4832-8DFD-43D979153A88");
    private static readonly HashSet<int> SendIds = new() { 10, 26, 42, 58 };
    private static readonly HashSet<int> RecvIds = new() { 11, 27, 43, 59 };

    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static int Run()
    {
        using var mutex = new Mutex(true, "ZoneQuanta.TrafficHelper.7F3A", out bool first);
        if (!first || !IsElevated()) return 1;

        string dir = TrafficFile.DefaultDir;
        string stopFile = Path.Combine(dir, StopFileName);
        string path = Path.Combine(dir, AppsFileName);
        Directory.CreateDirectory(dir);

        var file = TrafficFile.Load(path);
        file.Compact();
        var names = new Dictionary<int, string>();
        var pending = new Dictionary<string, (long Up, long Down)>();
        object gate = new();

        using var session = new TraceEventSession("ZoneQuantaNet");
        session.EnableProvider(KernelNetwork, TraceEventLevel.Informational, 0x30);

        session.Source.Dynamic.All += data =>
        {
            if (data.ProviderGuid != KernelNetwork) return;
            int id = (int)data.ID;
            bool send = SendIds.Contains(id), recv = RecvIds.Contains(id);
            if (!send && !recv) return;

            try
            {
                int pid = Convert.ToInt32(data.PayloadByName("PID"));
                long size = Convert.ToInt64(data.PayloadByName("size"));
                if (IsLoopback(data)) return;
                string app = NameOf(pid, names);
                lock (gate)
                {
                    pending.TryGetValue(app, out var cur);
                    pending[app] = send ? (cur.Up + size, cur.Down) : (cur.Up, cur.Down + size);
                }
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or NullReferenceException) { }
        };

        int ticks = 0;
        var timer = new Timer(_ =>
        {
            if (File.Exists(stopFile))
            {
                session.Stop();
                return;
            }
            if (++ticks % 30 == 0) Flush(file, pending, gate, path);
        }, null, 2000, 2000);

        session.Source.Process();
        timer.Dispose();
        Flush(file, pending, gate, path);
        return 0;
    }

    private static void Flush(TrafficFile file, Dictionary<string, (long Up, long Down)> pending, object gate, string path)
    {
        Dictionary<string, (long Up, long Down)> snapshot;
        lock (gate)
        {
            if (pending.Count == 0) return;
            snapshot = new Dictionary<string, (long Up, long Down)>(pending);
            pending.Clear();
        }
        lock (file)
        {
            foreach (var kv in snapshot) file.AddNow(kv.Key, kv.Value.Up, kv.Value.Down);
            file.Save(path);
        }
    }

    private static bool IsLoopback(TraceEvent data)
    {
        try
        {
            object? d = data.PayloadByName("daddr");
            if (d is uint v4) return (v4 & 0xFF) == 127;
            if (d is int i4) return (i4 & 0xFF) == 127;
        }
        catch (Exception ex) when (ex is InvalidCastException or ArgumentException) { }
        return false;
    }

    private static string NameOf(int pid, Dictionary<int, string> cache)
    {
        if (cache.TryGetValue(pid, out var n)) return n;
        try
        {
            n = pid == 4 ? "System" : Process.GetProcessById(pid).ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            n = "已退出的进程";
        }
        if (cache.Count > 4096) cache.Clear();
        cache[pid] = n;
        return n;
    }
}
