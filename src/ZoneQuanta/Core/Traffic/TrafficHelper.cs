using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using ZoneQuanta.Core.Monitor;
using ZoneQuanta.Platform;

namespace ZoneQuanta.Core.Traffic;

public static class TrafficHelper
{
    public const string StopFileName = "traffic.stop";
    public const string AppsFileName = "apps.tsv";
    // name \t full path \t AppUserModelID, so the panel can show the real icon of programs that have exited.
    public const string PathsFileName = "apps-paths.tsv";
    // name 	 path of programs this user ran lately (from Windows' records, not watched by the helper): a hint
    // the panel ranks below what it can see itself.
    public const string SeenFileName = "apps-seen.tsv";
    public const string MutexName = "ZoneQuanta.TrafficHelper.7F3A";
    public const string ExitedName = "已退出的进程";
    private static readonly Guid KernelNetwork = new("7DD42A49-5329-4832-8DFD-43D979153A88");
    private static readonly Guid KernelProcess = new("22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716");
    private static readonly HashSet<int> SendIds = new() { 10, 26, 42, 58 };
    private static readonly HashSet<int> RecvIds = new() { 11, 27, 43, 59 };

    private sealed class Proc
    {
        public string Name = "";
        public string Path = "";
        public string Aumid = "";
        public long StoppedAt; // ms tick; kept a little longer for packets still in flight
    }

    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static int Run()
    {
        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first || !IsElevated()) return 1;

        string dir = TrafficFile.DefaultDir;
        string stopFile = Path.Combine(dir, StopFileName);
        string path = Path.Combine(dir, AppsFileName);
        string pathsFile = Path.Combine(dir, PathsFileName);
        Directory.CreateDirectory(dir);
        if (!SafeToWrite(path)) return 1; // nothing is written, not even the log
        Log.CanWrite = SafeToWrite;

        var file = TrafficFile.Load(path);
        file.Compact();
        var procs = new Dictionary<int, Proc>();
        var known = LoadPaths(pathsFile);
        bool knownDirty = false;
        SaveRecentlyRun(Path.Combine(dir, SeenFileName));
        var pending = new Dictionary<string, (long Up, long Down)>();
        object gate = new();

        using var session = new TraceEventSession("ZoneQuantaNet");
        session.EnableProvider(KernelNetwork, TraceEventLevel.Informational, 0x30);
        // Process start / stop: names of short-lived programs, and no mix-up when Windows reuses a process id.
        session.EnableProvider(KernelProcess, TraceEventLevel.Informational, 0x10);

        Proc Resolve(int pid)
        {
            // Also when it stopped moments ago: its last packets still belong to it.
            if (procs.TryGetValue(pid, out var p)) return p;
            p = new Proc();
            if (pid == 4) p.Name = "System";
            else if (ProcessImages.Query(pid) is { } q)
            {
                p.Path = q.Path;
                p.Aumid = q.Aumid;
                p.Name = ProcessImages.NameOf(q.Path);
            }
            else
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    p.Name = ProcessImages.NameOf(proc.ProcessName);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { p.Name = ExitedName; }
            }
            procs[pid] = p;
            Remember(p);
            return p;
        }

        void Remember(Proc p)
        {
            if (p.Path.Length == 0 || p.Name == ExitedName) return;
            if (known.TryGetValue(p.Name, out var k) && k.Path == p.Path && k.Aumid == p.Aumid) return;
            known[p.Name] = (p.Path, p.Aumid);
            knownDirty = true;
        }

        session.Source.Dynamic.All += data =>
        {
            try
            {
                if (data.ProviderGuid == KernelProcess)
                {
                    int ev = (int)data.ID;
                    if (ev != 1 && ev != 2) return;
                    int pid = Convert.ToInt32(data.PayloadByName("ProcessID"), CultureInfo.InvariantCulture);
                    lock (gate)
                    {
                        if (ev == 1)
                        {
                            string image = ProcessImages.ToDosPath(data.PayloadByName("ImageName") as string ?? "");
                            string aumid = ProcessImages.AumidOf(data.PayloadByName("PackageFullName") as string ?? "",
                                data.PayloadByName("PackageRelativeAppId") as string ?? "");
                            var p = new Proc { Path = image, Aumid = aumid, Name = image.Length > 0 ? ProcessImages.NameOf(image) : "" };
                            if (p.Name.Length == 0) procs.Remove(pid);
                            else procs[pid] = p;
                        }
                        else if (procs.TryGetValue(pid, out var p)) p.StoppedAt = Environment.TickCount64;
                    }
                    return;
                }
                if (data.ProviderGuid != KernelNetwork) return;
                int id = (int)data.ID;
                bool send = SendIds.Contains(id), recv = RecvIds.Contains(id);
                if (!send && !recv) return;

                int npid = Convert.ToInt32(data.PayloadByName("PID"), CultureInfo.InvariantCulture);
                long size = Convert.ToInt64(data.PayloadByName("size"), CultureInfo.InvariantCulture);
                if (IsLoopback(data)) return;
                lock (gate)
                {
                    var p = Resolve(npid);
                    if (p.Path.Length > 0) Remember(p);
                    pending.TryGetValue(p.Name, out var cur);
                    pending[p.Name] = send ? (cur.Up + size, cur.Down) : (cur.Up, cur.Down + size);
                }
            }
            // This runs inside ETW's native callback: any exception leaving it ends the whole process,
            // past every try/catch, so one malformed event (seen while the session stops) must not escape.
            catch (Exception) { }
        };

        int ticks = 0;
        var timer = new Timer(_ =>
        {
            try
            {
                if (File.Exists(stopFile))
                {
                    session.Stop();
                    return;
                }
                if (++ticks % 30 != 0) return;
                Flush(file, pending, gate, path);
                Dictionary<string, (string Path, string Aumid)>? snapshot = null;
                lock (gate)
                {
                    long now = Environment.TickCount64;
                    foreach (int pid in procs.Where(kv => kv.Value.StoppedAt != 0 && now - kv.Value.StoppedAt > 10_000).Select(kv => kv.Key).ToList())
                        procs.Remove(pid);
                    if (knownDirty)
                    {
                        snapshot = new(known, StringComparer.OrdinalIgnoreCase);
                        knownDirty = false;
                    }
                }
                if (snapshot is not null) SavePaths(pathsFile, snapshot);
            }
            catch (Exception ex) { Log.Error("traffic-helper-timer", ex); } // a timer exception would end the process
        }, null, 2000, 2000);

        session.Source.Process();
        timer.Dispose();
        Flush(file, pending, gate, path);
        lock (gate) SavePaths(pathsFile, known);
        return 0;
    }

    private static void SaveRecentlyRun(string file)
    {
        if (!SafeToWrite(file)) return;
        try
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var (name, path) in Platform.ExecutableIndex.RecentlyRun().OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (path.Length <= 3 || path[1] != ':') continue;
                sb.Append(name).Append('\t').Append(path).Append('\n');
                n++;
            }
            File.WriteAllText(file + ".tmp", sb.ToString(), Encoding.UTF8);
            File.Move(file + ".tmp", file, overwrite: true);
            Log.Write($"helper recent runs {n}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error("apps-seen-save", ex); }
    }

    public static Dictionary<string, (string Path, string Aumid)> LoadPaths(string file)
    {
        var d = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(file)) return d;
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var p = line.Split('\t');
                if (p.Length >= 2 && p[0].Length > 0 && p[1].Length > 0) d[p[0]] = (p[1], p.Length > 2 ? p[2] : "");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error("apps-paths-read", ex); }
        return d;
    }

    private static void SavePaths(string file, Dictionary<string, (string Path, string Aumid)> known)
    {
        if (!SafeToWrite(file)) return;
        try
        {
            var sb = new StringBuilder();
            foreach (var (name, v) in known.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                sb.Append(name).Append('\t').Append(v.Path).Append('\t').Append(v.Aumid).Append('\n');
            File.WriteAllText(file + ".tmp", sb.ToString(), Encoding.UTF8);
            File.Move(file + ".tmp", file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Error("apps-paths-save", ex); }
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
            if (SafeToWrite(path)) file.Save(path);
        }
    }

    // The helper is elevated but writes into the user's own folder: refuse when any part of the path, or the
    // file or its temporary copy, is a link (a junction could send an elevated write onto a protected file).
    internal static bool SafeToWrite(string file)
    {
        try
        {
            for (var d = new DirectoryInfo(Path.GetDirectoryName(file)!); d is not null; d = d.Parent)
                if (d.Exists && d.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
            foreach (string f in new[] { file, file + ".tmp" })
            {
                var info = new FileInfo(f);
                if (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
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
}
