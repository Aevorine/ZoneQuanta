using System;
using System.Diagnostics;
using System.Runtime;

namespace ZoneQuanta.Platform;

internal static class MemoryTrim
{
    public static void Run()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            using var self = Process.GetCurrentProcess();
            Native.SetProcessWorkingSetSize(self.Handle, -1, -1);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}
