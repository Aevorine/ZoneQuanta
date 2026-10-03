using System;
using System.Runtime.InteropServices;
using ZoneQuanta.Platform;

namespace ZoneQuanta.Core.Monitor;

public readonly record struct SystemDetail(
    int Processes, int Threads, int Handles, int Cores,
    long CommitUsed, long CommitLimit, long Available, TimeSpan Uptime)
{
    public static SystemDetail Read()
    {
        var info = new Native.PERFORMANCE_INFORMATION { cb = (uint)Marshal.SizeOf<Native.PERFORMANCE_INFORMATION>() };
        if (!Native.GetPerformanceInfo(out info, info.cb))
            return new SystemDetail(0, 0, 0, Environment.ProcessorCount, 0, 0, 0, TimeSpan.FromMilliseconds(Environment.TickCount64));

        long page = (long)info.PageSize;
        return new SystemDetail(
            (int)info.ProcessCount, (int)info.ThreadCount, (int)info.HandleCount, Environment.ProcessorCount,
            (long)info.CommitTotal * page, (long)info.CommitLimit * page,
            (long)info.PhysicalAvailable * page, TimeSpan.FromMilliseconds(Environment.TickCount64));
    }
}
