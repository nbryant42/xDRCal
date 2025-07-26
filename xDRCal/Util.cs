using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Devices.Display.Core;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using xDRCal.Win32;

namespace xDRCal;

public partial class Util
{
    /// <summary>
    /// Due to legacy Windows design issues, this may fail to figure out which monitor the HWND is on, in which case
    /// it returns null. Otherwise, returns true or false.
    /// </summary>
    /// <param name="hwnd">HWND of a window on the monitor to check for HDR enablement</param>
    /// <returns>true, false, or null</returns>
    public static bool? IsHdrEnabled(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }
        var dip = FindDeviceInterfacePath((HWND)hwnd);

        return dip == null ? null : IsHdrEnabled(dip);
    }

    public static bool? IsHdrEnabled(string dip)
    {
        if (!FindDisplayConfigIdForDevice(dip, out var adapterId, out var targetId))
        {
            Debug.WriteLine($"FindDisplayConfigIdForDevice failed: {dip}");
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

    /// <summary>
    /// Due to legacy Windows design issues, this may fail to figure out which monitor the HWND is on,
    /// (or the monitor does not support HDR, etc) in which case it returns null.
    /// 
    /// The return value is based on the monitor EDID's MaxLuminanceInNits, which is often pretty far off the mark.
    /// 
    /// E.g. for my LG C9, MaxLuminanceInNits = 1499, MaxAverageFullFrameLuminanceInNits = 799, and the latter is a
    /// better guide to where clipping occurs.
    /// 
    /// But the situation is reversed on my Samsung G65B; 603.698 and 351.276 respectively, with the former being a
    /// a better match.
    /// 
    /// These numbers are sort of a rough guide (and not a very good one) to the usable signal level,
    /// not the actual brightness, because of ABL.
    /// 
    /// TL;DR: game developers must present a calibration screen, because monitors lie, even in HGiG mode.
    /// </summary>
    /// <param name="hwnd">HWND of a window on the monitor</param>
    /// <returns>peak luminance for the monitor or null</returns>
    public static float? GetPeakNits(string deviceInterfacePath)
    {
        if (deviceInterfacePath == null)
        {
            return null;
        }

        var manager = DisplayManager.Create(DisplayManagerOptions.None);
        var targets = manager.GetCurrentTargets();

        foreach (var target in targets)
        {
            if (!target.IsStale && target.DeviceInterfacePath == deviceInterfacePath)
            {
                var monitor = target.TryGetMonitor();

                if (monitor != null)
                {
                    return monitor.MaxLuminanceInNits;
                }
            }
        }

        return null;
    }

    public static int GetPeakLuminanceCode(IntPtr hwnd, EOTF eotf)
    {
        var dip = FindDeviceInterfacePath((HWND)hwnd);

        if (dip != null && IsHdrEnabled(dip) == true)
        {
            var nits = GetPeakNits(dip);

            return nits == null ? 1023 : (int)MathF.Round(eotf.ToCode((float)nits));
        }
        else
        {
            return (int)MathF.Round(eotf.ToCode(80.0f));
        }
    }

    private static string? FindDeviceInterfacePath(HWND hwnd)
    {
        // MonitorFromWindow seems to be a bit of a legacy function and possibly not the right path on modern Windows.
        // The issue is that each HMONITOR can *sometimes* -- not always, and not very predictably -- be associated with
        // multiple physical monitors, as documented at
        // https://learn.microsoft.com/en-us/windows/win32/monitor/using-the-high-level-monitor-configuration-functions
        //
        // For now, we'll attempt to kludge through this as best we can, but the long-term solution might involve
        // obtaining the desktop geometry (how?) and resolving manually based on more recent APIs.
        var hMonitor = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);

        //if (PInvoke.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var pdwNumberOfPhysicalMonitors) &&
        //    pdwNumberOfPhysicalMonitors > 1)
        //{
        //    Debug.WriteLine($"pdwNumberOfPhysicalMonitors = {pdwNumberOfPhysicalMonitors}");
        //}

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
        }

        return dip;
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
        var list = new List<string>();

        foreach (var adapter in EnumDisplayDevices())
        {
            if (deviceName == adapter.DeviceName.ToString())
            {
                // Here's where it gets tricky. Looks like the HMONITOR really corresponds to an adapter, but there can
                // sometimes be more than one monitor per adapter (e.g. user recently had Extend mode enabled...)
                //
                // Maybe we can at least filter based on state flags.
                foreach (var monitor in EnumDisplayDevices(adapter.DeviceName.ToString(), PInvoke.EDD_GET_DEVICE_INTERFACE_NAME))
                {
                    //Debug.WriteLine($"All monitor fields for {deviceName}:");
                    //Debug.WriteLine($"DeviceName: {monitor.DeviceName}");
                    //Debug.WriteLine($"DeviceString: {monitor.DeviceString}");
                    //Debug.WriteLine($"DeviceID: {monitor.DeviceID}");
                    //Debug.WriteLine($"DeviceKey: {monitor.DeviceKey}");
                    //Debug.WriteLine($"StateFlags: {monitor.StateFlags}");

                    if ((monitor.StateFlags & PInvoke.DISPLAY_DEVICE_ACTIVE) != 0)
                    {
                        list.Add(monitor.DeviceID.ToString());
                    }
                }
            }
        }
        return list.Count == 1 ? list[0] : null;
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
            //Debug.WriteLine($"{adapterName.monitorDevicePath} != {deviceName}");
        }
        return false;
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
