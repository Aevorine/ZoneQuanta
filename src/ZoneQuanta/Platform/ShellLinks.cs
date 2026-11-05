using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using ZoneQuanta.Core.Quick;

namespace ZoneQuanta.Platform;

// Shortcut target and program description, used to group quick items the name alone does not explain.
internal static class ShellLinks
{
    // Any thread: the shell link object is free-threaded.
    public static TargetHint? Describe(string path)
    {
        try
        {
            string target = path, args = "";
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                (target, args) = Resolve(path);
            if (target.Length == 0) return null;
            string desc = "", product = "", company = "";
            if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
            {
                var v = FileVersionInfo.GetVersionInfo(target);
                desc = v.FileDescription?.Trim() ?? "";
                product = v.ProductName?.Trim() ?? "";
                company = v.CompanyName?.Trim() ?? "";
            }
            return new TargetHint(target, args, desc, product, company);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or InvalidCastException)
        {
            return null;
        }
    }

    private static (string Target, string Args) Resolve(string lnk)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(lnk, 0);
            var path = new StringBuilder(1024);
            link.GetPath(path, path.Capacity, IntPtr.Zero, 0);
            var args = new StringBuilder(2048);
            link.GetArguments(args, args.Capacity);
            return (Environment.ExpandEnvironmentVariables(path.ToString()), args.ToString());
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    // Only the methods up to GetArguments are called; the vtable order must still match IShellLinkW.
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
    }
}
