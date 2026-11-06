using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ZoneQuanta.Platform;

// Contacts of precision touchpads and touchscreens straight from their HID reports (Raw Input). Windows keeps its own
// three- and four-finger gestures and never passes them to apps; the raw reports still arrive. Each device's report
// layout (finger collections, X / Y ranges, contact count) is read once from its preparsed data. Parallel and hybrid
// reporting are both handled: a frame is complete once the contact count of its first report has been read.
internal sealed class RawTouch : IDisposable
{
    public const int WM_INPUT = 0x00FF;
    private const ushort PageDigitizer = 0x0D, PageDesktop = 0x01;
    private const ushort UsageTouchScreen = 0x04, UsageTouchPad = 0x05;
    private const ushort UsageX = 0x30, UsageY = 0x31, UsageTip = 0x42, UsageContactId = 0x51, UsageContactCount = 0x54;
    private const uint RIDEV_REMOVE = 0x1, RIDEV_INPUTSINK = 0x100, RID_INPUT = 0x10000003, RIM_TYPEHID = 2, RIDI_PREPARSEDDATA = 0x20000005;
    private const int HIDP_STATUS_SUCCESS = 0x00110000;

    public readonly record struct Frame(bool TouchPad, int Down, double X, double Y, double Aspect);

    private readonly Dictionary<IntPtr, Device?> _devices = new();
    private IntPtr _buffer;
    private int _bufferSize;
    private bool _registered, _probed;

    public bool Register(IntPtr hwnd)
    {
        if (_registered) return true;
        var devices = new[]
        {
            new RAWINPUTDEVICE { UsagePage = PageDigitizer, Usage = UsageTouchPad, Flags = RIDEV_INPUTSINK, Target = hwnd },
            new RAWINPUTDEVICE { UsagePage = PageDigitizer, Usage = UsageTouchScreen, Flags = RIDEV_INPUTSINK, Target = hwnd },
        };
        _registered = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        if (_registered && !_probed) Probe();
        return _registered;
    }

    // Reads every digitizer's layout up front (logged once each), so the first touch needs no setup.
    private void Probe()
    {
        _probed = true;
        uint n = 0, size = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        if (GetRawInputDeviceList(null, ref n, size) != 0 || n == 0) return;
        var list = new RAWINPUTDEVICELIST[n];
        int got = (int)GetRawInputDeviceList(list, ref n, size);
        for (int i = 0; i < got; i++)
            if (list[i].Type == RIM_TYPEHID && !_devices.ContainsKey(list[i].Device) && Device.Open(list[i].Device) is { } d)
                _devices[list[i].Device] = d;
    }

    public void Unregister()
    {
        if (!_registered) return;
        _registered = false;
        var devices = new[]
        {
            new RAWINPUTDEVICE { UsagePage = PageDigitizer, Usage = UsageTouchPad, Flags = RIDEV_REMOVE },
            new RAWINPUTDEVICE { UsagePage = PageDigitizer, Usage = UsageTouchScreen, Flags = RIDEV_REMOVE },
        };
        RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    // Reads one WM_INPUT; completed frames go to `frame`. Never throws: an unreadable device is skipped from then on.
    public void Read(IntPtr lParam, Action<Frame> frame)
    {
        uint size = 0, header = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        if (GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, header) != 0 || size == 0) return;
        if (size > _bufferSize)
        {
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _bufferSize = (int)Math.Max(size, 256);
            _buffer = Marshal.AllocHGlobal(_bufferSize);
        }
        if (GetRawInputData(lParam, RID_INPUT, _buffer, ref size, header) != size) return;
        var h = Marshal.PtrToStructure<RAWINPUTHEADER>(_buffer);
        if (h.Type != RIM_TYPEHID) return;
        if (!_devices.TryGetValue(h.Device, out var dev))
        {
            dev = Device.Open(h.Device);
            _devices[h.Device] = dev;
        }
        if (dev is null) return;
        IntPtr hid = _buffer + (int)header;
        int sizeHid = Marshal.ReadInt32(hid), count = Marshal.ReadInt32(hid, 4);
        for (int i = 0; i < count; i++)
            if (dev.Report(hid + 8 + i * sizeHid, (uint)sizeHid) is { } f) frame(f);
    }

    public void Dispose()
    {
        Unregister();
        foreach (var d in _devices.Values) d?.Dispose();
        _devices.Clear();
        if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
        _buffer = IntPtr.Zero;
        _bufferSize = 0;
    }

    internal sealed class Device : IDisposable
    {
        private readonly IntPtr _pre;
        private readonly bool _pad;
        private readonly ushort _countLink;
        private readonly bool _hasCount;
        private readonly (ushort Link, int MinX, int MaxX, int MinY, int MaxY)[] _fingers;
        private readonly double _aspect;
        private readonly ushort[] _usages = new ushort[16];
        private readonly Dictionary<uint, (bool Tip, double X, double Y)> _contacts = new();
        private int _expected, _received;

        private Device(IntPtr pre, bool pad, ushort countLink, bool hasCount, (ushort, int, int, int, int)[] fingers, double aspect)
        {
            (_pre, _pad, _countLink, _hasCount, _fingers, _aspect) = (pre, pad, countLink, hasCount, fingers, aspect);
        }

        public static Device? Open(IntPtr handle)
        {
            uint size = 0;
            if (GetRawInputDeviceInfo(handle, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size) != 0 || size == 0) return null;
            IntPtr pre = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputDeviceInfo(handle, RIDI_PREPARSEDDATA, pre, ref size) == unchecked((uint)-1)) throw new InvalidOperationException();
                if (HidP_GetCaps(pre, out var caps) != HIDP_STATUS_SUCCESS || caps.UsagePage != PageDigitizer
                    || (caps.Usage != UsageTouchPad && caps.Usage != UsageTouchScreen) || caps.NumberInputValueCaps == 0) throw new InvalidOperationException();
                ushort n = caps.NumberInputValueCaps;
                var vals = new HIDP_VALUE_CAPS[n];
                if (HidP_GetValueCaps(0, vals, ref n, pre) != HIDP_STATUS_SUCCESS) throw new InvalidOperationException();
                var xs = new Dictionary<ushort, (int Min, int Max, int PMin, int PMax)>();
                var ys = new Dictionary<ushort, (int Min, int Max, int PMin, int PMax)>();
                ushort countLink = 0;
                bool hasCount = false;
                for (int i = 0; i < n; i++)
                {
                    var v = vals[i];
                    bool Has(ushort page, ushort usage) => v.UsagePage == page && (v.IsRange != 0 ? usage >= v.UsageMin && usage <= v.UsageMax : v.UsageMin == usage);
                    if (Has(PageDesktop, UsageX)) xs[v.LinkCollection] = (v.LogicalMin, v.LogicalMax, v.PhysicalMin, v.PhysicalMax);
                    else if (Has(PageDesktop, UsageY)) ys[v.LinkCollection] = (v.LogicalMin, v.LogicalMax, v.PhysicalMin, v.PhysicalMax);
                    else if (Has(PageDigitizer, UsageContactCount)) (countLink, hasCount) = (v.LinkCollection, true);
                }
                var fingers = new List<(ushort, int, int, int, int)>();
                double aspect = 1;
                foreach (var (link, x) in xs)
                {
                    if (!ys.TryGetValue(link, out var y) || x.Max <= x.Min || y.Max <= y.Min) continue;
                    fingers.Add((link, x.Min, x.Max, y.Min, y.Max));
                    double pw = x.PMax - x.PMin, ph = y.PMax - y.PMin;
                    aspect = pw > 0 && ph > 0 ? pw / ph : (double)(x.Max - x.Min) / (y.Max - y.Min);
                }
                if (fingers.Count == 0) throw new InvalidOperationException();
                fingers.Sort((a, b) => a.Item1.CompareTo(b.Item1));
                Core.Log.Write($"touch device: {(caps.Usage == UsageTouchPad ? "touchpad" : "touchscreen")}, {fingers.Count} contact slot(s), aspect {aspect:0.00}");
                return new Device(pre, caps.Usage == UsageTouchPad, countLink, hasCount, fingers.ToArray(), aspect);
            }
            catch (Exception)
            {
                Marshal.FreeHGlobal(pre);
                return null;
            }
        }

        // One input report; returns the frame it completes, if any.
        public Frame? Report(IntPtr report, uint length)
        {
            if (_hasCount && HidP_GetUsageValue(0, PageDigitizer, _countLink, UsageContactCount, out uint count, _pre, report, length) == HIDP_STATUS_SUCCESS && count > 0)
            {
                _expected = (int)count;
                _received = 0;
                _contacts.Clear();
            }
            else if (!_hasCount && _received >= _expected)
            {
                _expected = _fingers.Length;
                _received = 0;
                _contacts.Clear();
            }
            for (int i = 0; i < _fingers.Length && _received < _expected; i++)
            {
                var f = _fingers[i];
                if (HidP_GetUsageValue(0, PageDesktop, f.Link, UsageX, out uint rx, _pre, report, length) != HIDP_STATUS_SUCCESS
                    || HidP_GetUsageValue(0, PageDesktop, f.Link, UsageY, out uint ry, _pre, report, length) != HIDP_STATUS_SUCCESS) continue;
                uint id = HidP_GetUsageValue(0, PageDigitizer, f.Link, UsageContactId, out uint cid, _pre, report, length) == HIDP_STATUS_SUCCESS ? cid : (uint)i;
                uint len = (uint)_usages.Length;
                bool tip = false;
                if (HidP_GetUsages(0, PageDigitizer, f.Link, _usages, ref len, _pre, report, length) == HIDP_STATUS_SUCCESS)
                    for (int k = 0; k < len; k++)
                        if (_usages[k] == UsageTip) tip = true;
                _contacts[id] = (tip, Math.Clamp(((int)rx - f.MinX) / (double)(f.MaxX - f.MinX), 0, 1), Math.Clamp(((int)ry - f.MinY) / (double)(f.MaxY - f.MinY), 0, 1));
                _received++;
            }
            if (_received < _expected) return null;
            int down = 0;
            double sx = 0, sy = 0;
            foreach (var c in _contacts.Values)
            {
                if (!c.Tip) continue;
                down++;
                sx += c.X;
                sy += c.Y;
            }
            return new Frame(_pad, down, down > 0 ? sx / down : 0, down > 0 ? sy / down : 0, _aspect);
        }

        public void Dispose() => Marshal.FreeHGlobal(_pre);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICELIST
    {
        public IntPtr Device;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    // 72 bytes; the union's first two USAGEs are Usage (or UsageMin) and UsageMax.
    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_VALUE_CAPS
    {
        public ushort UsagePage;
        public byte ReportID, IsAlias;
        public ushort BitField, LinkCollection, LinkUsage, LinkUsagePage;
        public byte IsRange, IsStringRange, IsDesignatorRange, IsAbsolute, HasNull, Reserved;
        public ushort BitSize, ReportCount;
        public ushort R0, R1, R2, R3, R4;
        public uint UnitsExp, Units;
        public int LogicalMin, LogicalMax, PhysicalMin, PhysicalMax;
        public ushort UsageMin, UsageMax;
        public ushort S0, S1, D0, D1, I0, I1;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[]? list, ref uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW")]
    private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);

    [DllImport("hid.dll")]
    private static extern int HidP_GetValueCaps(int reportType, [Out] HIDP_VALUE_CAPS[] caps, ref ushort length, IntPtr preparsed);

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection, ushort usage, out uint value, IntPtr preparsed, IntPtr report, uint reportLength);

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, [Out] ushort[] usages, ref uint length, IntPtr preparsed, IntPtr report, uint reportLength);
}
