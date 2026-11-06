using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;
using ZoneQuanta.Core;

namespace ZoneQuanta.Platform;

// Task Scheduler through its COM interface: states as numbers instead of schtasks' translated text, and the
// running instance of a task this user registered can be ended without elevation.
public static class ScheduledTask
{
    // Process ids of the task's running instances; empty when it has none or cannot be read.
    public static List<int> RunningPids(string name)
    {
        var pids = new List<int>();
        Use(name, task =>
        {
            foreach (dynamic run in task.GetInstances(0))
                pids.Add(Convert.ToInt32((object)run.EnginePID, CultureInfo.InvariantCulture));
        });
        return pids;
    }

    public static bool Stop(string name) => Use(name, task => task.Stop(0));

    private static bool Use(string name, Action<dynamic> work)
    {
        try
        {
            Type? type = Type.GetTypeFromProgID("Schedule.Service");
            if (type is null) return false;
            dynamic service = Activator.CreateInstance(type)!;
            service.Connect();
            work(service.GetFolder("\\").GetTask(name));
            return true;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException
                                       or RuntimeBinderException or InvalidCastException)
        {
            Log.Error("task-scheduler", ex);
            return false;
        }
    }
}
