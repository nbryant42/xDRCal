using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Devices.Display.Core;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using xDRCal.Win32;

namespace xDRCal;

public partial class Util
{
    public static bool? IsHdrEnabled(IntPtr hwnd)
    {
        var hMonitor = PInvoke.MonitorFromWindow((HWND)hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(hMonitor, ref mi))
        {
            // can happen on shutdown
            return null;
        }

        var dip = FindDeviceInterfacePath(mi.szDevice);

        if (dip == null)
        {
            Debug.WriteLine("FindDeviceInterfacePath failed");
            return null;
        }

        if (!FindDisplayConfigIdForDevice(dip, out var adapterId, out var targetId))
        {
            Debug.WriteLine("FindDisplayConfigIdForDevice failed");
            return null;
        }

        var hr = GetAdvancedColorInfo2(adapterId, targetId, out var info2);
        if (hr != 0)
        {
            Debug.WriteLine($"GetAdvancedColorInfo2 failed: {hr}");
            return null;
        }

        return info2.HDRUserEnabled;
    }

    private static IEnumerable<DISPLAY_DEVICEW> EnumDisplayDevices(string? adapterName = null, uint flags = 0)
    {
        uint devNum = 0;
        DISPLAY_DEVICEW dev = new() { cb = (uint)Marshal.SizeOf(typeof(DISPLAY_DEVICEW)) };
        while (PInvoke.EnumDisplayDevices(adapterName, devNum, ref dev, flags))
        {
            yield return dev;
            devNum++;
        }
    }

    private static string? FindDeviceInterfacePath(string deviceName)
    {
        foreach (var adapter in EnumDisplayDevices())
        {
            if (deviceName == adapter.DeviceName.ToString())
            {
                DISPLAY_DEVICEW monitor = new() { cb = (uint)Marshal.SizeOf(typeof(DISPLAY_DEVICEW)) };
                if (PInvoke.EnumDisplayDevices(adapter.DeviceName.ToString(), 0, ref monitor, PInvoke.EDD_GET_DEVICE_INTERFACE_NAME))
                {
                    return monitor.DeviceID.ToString();
                }
            }
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    // return true if found
    public static bool FindDisplayConfigIdForDevice(string deviceName, out LUID adapterId, out uint targetId)
    {
        adapterId = new LUID();
        targetId = 0;

        var err = PInvoke.GetDisplayConfigBufferSizes(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
            out var numPathArrayElements, out var numModeInfoArrayElements);
        if (err != 0)
            throw new Win32Exception((int)err);

        var pathInfoArray = new DISPLAYCONFIG_PATH_INFO[numPathArrayElements];
        var modeInfoArray = new DISPLAYCONFIG_MODE_INFO[numModeInfoArrayElements];
        var hr = QueryDisplayConfig(QDC.ONLY_ACTIVE_PATHS,
            ref numPathArrayElements, pathInfoArray,
            ref numModeInfoArrayElements, modeInfoArray, nint.Zero);

        if (hr != 0)
        {
            Debug.WriteLine($"FindDisplayConfigIdForDevice: QueryDisplayConfig failed: {hr}. Buffer sizes too small?");
            return false;
        }

        for (int i = 0; i < numPathArrayElements; ++i)
        {
            var targetInfo = pathInfoArray[i].targetInfo;

            // Fill in target name request
            var adapterName = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
            adapterName.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
            adapterName.header.adapterId = targetInfo.adapterId;
            adapterName.header.id = targetInfo.id;
            adapterName.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_TARGET_NAME;

            // Compare deviceName (e.g. "\\.\DISPLAY1")
            var hrl = DisplayConfigGetDeviceInfo(ref adapterName);

            if (hrl != 0)
            {
                Debug.WriteLine($"DisplayConfigGetDeviceInfo failed: {hrl}");
                return false;
            }

            if (adapterName.monitorDevicePath.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
            {
                adapterId = targetInfo.adapterId;
                targetId = targetInfo.id;
                return true;
            }
        }
        return false;
    }

    public static float PQCodeToNits(int N)
    {
        float Nn_pow = MathF.Pow(N / 1023.0f, 32.0f / 2523.0f);

        float numerator = Math.Max(Nn_pow - 107.0f / 128.0f, 0.0f);
        float denominator = 2413.0f / 128.0f - 2392.0f / 128.0f * Nn_pow;

        return MathF.Pow(numerator / denominator, 8192.0f / 1305.0f) * 10000.0f;
    }
    public static float SrgbToLinear(float value)
    {
        if (value <= 0.04045f)
            return value / 12.92f;
        else
            return MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
    }

    public static long GetAdvancedColorInfo2(LUID adapterId, uint targetId,
        out DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 info2)
    {
        info2 = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2();
        info2.header.type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_ADVANCED_COLOR_INFO_2;
        info2.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2>();
        info2.header.adapterId = adapterId;
        info2.header.id = targetId;

        return DisplayConfigGetDeviceInfo(ref info2);
    }

    // return true if found
    public static bool GetFirstActiveDisplayId(out LUID adapterId, out uint targetId)
    {
        adapterId = new LUID();
        targetId = 0;

        var err = PInvoke.GetDisplayConfigBufferSizes(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
            out var numPathArrayElements, out var numModeInfoArrayElements);
        if (err != 0)
            throw new Win32Exception((int)err);

        var pathInfoArray = new DISPLAYCONFIG_PATH_INFO[numPathArrayElements];
        var modeInfoArray = new DISPLAYCONFIG_MODE_INFO[numModeInfoArrayElements];

        int hr = QueryDisplayConfig(QDC.ONLY_ACTIVE_PATHS,
            ref numPathArrayElements, pathInfoArray,
            ref numModeInfoArrayElements, modeInfoArray, nint.Zero);

        if (hr != 0 || numPathArrayElements == 0)
        {
            Debug.WriteLine($"GetFirstActiveDisplayId: QueryDisplayConfig failed: {hr}");
            return false;
        }

        // Use the first target (primary display, or iterate for all displays)
        var info = pathInfoArray[0].targetInfo;
        adapterId = info.adapterId;
        targetId = info.id;
        return true;
    }

    // CsWin32's stub for this lacks the latest struct defs.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern long DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 requestPacket);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern long DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    // CsWin32's stub for this uses pointers.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int QueryDisplayConfig(
        QDC flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        nint currentTopologyId);


    // CsWin32 stub does not allow MONITORINFOEX
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
}
