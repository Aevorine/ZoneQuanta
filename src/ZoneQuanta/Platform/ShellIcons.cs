using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZoneQuanta.Core;

namespace ZoneQuanta.Platform;

// System icons and image thumbnails, extracted on one background STA thread and cached.
internal static class ShellIcons
{
    private static readonly ConcurrentDictionary<string, BitmapSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly BlockingCollection<(string Path, int Px, Action<BitmapSource?> Done)> Queue = new();
    private static readonly object Gate = new();
    private static Thread? _worker;

    private static string Key(string path, int px) => px + "|" + path;

    public static BitmapSource? Cached(string path, int px) => Cache.TryGetValue(Key(path, px), out var b) ? b : null;

    public static void Forget(string path)
    {
        foreach (var k in Cache.Keys)
            if (k.EndsWith("|" + path, StringComparison.OrdinalIgnoreCase)) Cache.TryRemove(k, out _);
    }

    // Done runs on the worker thread; the bitmap is frozen, so callers just marshal to their dispatcher.
    public static void Request(string path, int px, Action<BitmapSource?> done)
    {
        if (Cache.TryGetValue(Key(path, px), out var cached))
        {
            done(cached);
            return;
        }
        EnsureWorker();
        Queue.Add((path, px, done));
    }

    private static void EnsureWorker()
    {
        if (_worker is not null) return;
        lock (Gate)
        {
            if (_worker is not null) return;
            _worker = new Thread(Run) { IsBackground = true, Name = "ShellIcons", Priority = ThreadPriority.BelowNormal };
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start();
        }
    }

    private static void Run()
    {
        foreach (var job in Queue.GetConsumingEnumerable())
        {
            string key = Key(job.Path, job.Px);
            if (!Cache.TryGetValue(key, out var bmp))
            {
                try { bmp = Load(job.Path, job.Px); }
                catch (Exception ex) { Log.Error("icon", ex); bmp = null; }
                Cache[key] = bmp;
            }
            try { job.Done(bmp); }
            catch (Exception ex) { Log.Error("icon-done", ex); }
        }
    }

    private static BitmapSource? Load(string path, int px)
    {
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory) == 0 && factory is not null)
        {
            try
            {
                int flags = SIIGBF_BIGGERSIZEOK | (IsImage(path) ? 0 : SIIGBF_ICONONLY);
                if (factory.GetImage(new SIZE { cx = px, cy = px }, flags, out IntPtr hbmp) == 0 && hbmp != IntPtr.Zero)
                {
                    try { if (FromHBitmap(hbmp) is { } b) return b; }
                    finally { DeleteObject(hbmp); }
                }
            }
            finally { Marshal.ReleaseComObject(factory); }
        }
        return FromFileInfo(path);
    }

    private static bool IsImage(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".heic" or ".tif" or ".tiff";

    // GetDIBits with a negative height always yields top-down rows, whatever the source orientation.
    private static BitmapSource? FromHBitmap(IntPtr hbmp)
    {
        if (GetObject(hbmp, Marshal.SizeOf<BITMAP>(), out BITMAP bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight == 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        var info = new BITMAPINFO { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var pixels = new byte[w * h * 4];
        IntPtr dc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, hbmp, 0, (uint)h, pixels, ref info, 0) == 0) return null;
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }

        bool alpha = false;
        for (int i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 0) { alpha = true; break; }
        if (!alpha)
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
        src.Freeze();
        return src;
    }

    private static BitmapSource? FromFileInfo(string path)
    {
        bool exists = File.Exists(path) || Directory.Exists(path);
        var fi = new SHFILEINFO();
        uint flags = SHGFI_ICON | SHGFI_LARGEICON | (exists ? 0 : SHGFI_USEFILEATTRIBUTES);
        uint attrs = Directory.Exists(path) ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        SHGetFileInfo(path, attrs, ref fi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (fi.hIcon == IntPtr.Zero) return null;
        try
        {
            var b = Imaging.CreateBitmapSourceFromHIcon(fi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            b.Freeze();
            return b;
        }
        finally { DestroyIcon(fi.hIcon); }
    }

    private const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;
    private const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0, SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
        public uint c0, c1, c2, c3;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr h, int size, out BITMAP bm);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr hbmp, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFO info, uint usage);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
}
