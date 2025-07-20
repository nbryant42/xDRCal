using System;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;

namespace xDRCal.Win32;

public enum DISPLAYCONFIG_DEVICE_INFO_TYPE : uint
{
    GET_SOURCE_NAME = 1,
    GET_TARGET_NAME = 2,
    GET_TARGET_PREFERRED_MODE = 3,
    GET_ADAPTER_NAME = 4,
    SET_TARGET_PERSISTENCE = 5,
    GET_TARGET_BASE_TYPE = 6,
    GET_SUPPORT_VIRTUAL_RESOLUTION = 7,
    SET_SUPPORT_VIRTUAL_RESOLUTION = 8,
    GET_ADVANCED_COLOR_INFO = 9,
    SET_ADVANCED_COLOR_STATE = 10,
    GET_SDR_WHITE_LEVEL = 11,
    GET_MONITOR_SPECIALIZATION = 12,
    SET_MONITOR_SPECIALIZATION = 13,
    SET_RESERVED1 = 14,
    GET_ADVANCED_COLOR_INFO_2 = 15,
    SET_HDR_STATE = 16,
    SET_WCG_STATE = 17,
    FORCE_UINT32 = 0xFFFFFFFF
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct DISPLAYCONFIG_ADAPTER_NAME
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string adapterDevicePath;
}

public enum DISPLAYCONFIG_MODE_INFO_TYPE : uint
{
    SOURCE = 1,
    TARGET = 2,
    DESKTOP_IMAGE = 3,
    FORCE_UINT32 = 0xFFFFFFFF
}

[StructLayout(LayoutKind.Explicit)]
internal struct DISPLAYCONFIG_MODE_INFO
{
    [FieldOffset(0)]
    public DISPLAYCONFIG_MODE_INFO_TYPE infoType;
    [FieldOffset(4)]
    public uint id;
    [FieldOffset(8)]
    public LUID adapterId;

    // Union: Only one of the following is valid, based on infoType
    [FieldOffset(16)]
    public DISPLAYCONFIG_TARGET_MODE targetMode;
    [FieldOffset(16)]
    public DISPLAYCONFIG_SOURCE_MODE sourceMode;
    [FieldOffset(16)]
    public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO
{
    public POINTL PathSourceSize;
    public RECT DesktopImageRegion;
    public RECT DesktopImageClip;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_SOURCE_MODE
{
    public uint width;
    public uint height;
    public DISPLAYCONFIG_PIXELFORMAT pixelFormat;
    public POINTL position;
}

public enum DISPLAYCONFIG_PIXELFORMAT : uint
{
    DISPLAYCONFIG_PIXELFORMAT_8BPP = 1,
    DISPLAYCONFIG_PIXELFORMAT_16BPP = 2,
    DISPLAYCONFIG_PIXELFORMAT_24BPP = 3,
    DISPLAYCONFIG_PIXELFORMAT_32BPP = 4,
    DISPLAYCONFIG_PIXELFORMAT_NONGDI = 5,
    DISPLAYCONFIG_PIXELFORMAT_FORCE_UINT32 = 0xFFFFFFFF
}


[StructLayout(LayoutKind.Sequential)]
public struct POINTL
{
    public int x;
    public int y;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_TARGET_MODE
{
    public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
{
    public ulong pixelRate;
    public DISPLAYCONFIG_RATIONAL hSyncFreq;
    public DISPLAYCONFIG_RATIONAL vSyncFreq;
    public DISPLAYCONFIG_2DREGION activeSize;
    public DISPLAYCONFIG_2DREGION totalSize;

    // Union of bitfields and direct uint: use as a single field, provide helpers for bitfields
    public uint videoStandard;

    public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;

    // Helpers for bitfields
    public ushort VideoStandard => (ushort)(videoStandard & 0xFFFF);
    public byte VSyncFreqDivider => (byte)((videoStandard >> 16) & 0x3F);
    public ushort Reserved => (ushort)((videoStandard >> 22) & 0x3FF);
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_2DREGION
{
    public uint cx;
    public uint cy;
}


[StructLayout(LayoutKind.Sequential)]
public struct LUID
{
    public uint LowPart;
    public int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_DEVICE_INFO_HEADER
{
    public DISPLAYCONFIG_DEVICE_INFO_TYPE type;
    public uint size;
    public LUID adapterId;
    public uint id;
}

public enum DISPLAYCONFIG_COLOR_ENCODING : uint
{
    RGB = 0,
    YCbCr444 = 1,
    YCbCr422 = 2,
    YCbCr420 = 3,
    Intensity = 4,
    FORCE_UINT32 = 0xFFFFFFFF
}

public enum DISPLAYCONFIG_ADVANCED_COLOR_MODE : uint
{
    SDR = 0, // RGB888 composition, display-referred color/luminance
    WCG = 1, // Advanced color (FP16 scRGB), scene-referred color, display-referred luminance
    HDR = 2  // Advanced color (FP16 scRGB), scene-referred color/luminance
}


[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    public uint value;
    public DISPLAYCONFIG_COLOR_ENCODING colorEncoding;
    public uint bitsPerColorChannel;
    public DISPLAYCONFIG_ADVANCED_COLOR_MODE activeColorMode;

    public bool AdvancedColorSupported => (value & 1 << 0) != 0;
    public bool AdvancedColorActive => (value & 1 << 1) != 0;
    public bool AdvancedColorLimitedByPolicy => (value & 1 << 3) != 0;
    public bool HDRSupported => (value & 1 << 4) != 0;
    public bool HDRUserEnabled => (value & 1 << 5) != 0;
    public bool WCgSupported => (value & 1 << 6) != 0;
    public bool WCgUserEnabled => (value & 1 << 7) != 0;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_PATH_INFO
{
    public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
    public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
    public uint flags;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_PATH_TARGET_INFO
{
    public LUID adapterId;
    public uint id;
    public uint modeInfoIdx; // Union: can use as a single uint

    public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
    public DISPLAYCONFIG_ROTATION rotation;
    public DISPLAYCONFIG_SCALING scaling;
    public DISPLAYCONFIG_RATIONAL refreshRate;
    public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
    [MarshalAs(UnmanagedType.Bool)]
    public bool targetAvailable;
    public uint statusFlags;

    public ushort DesktopModeInfoIdx => (ushort)(modeInfoIdx & 0xFFFF);
    public ushort TargetModeInfoIdx => (ushort)(modeInfoIdx >> 16 & 0xFFFF);
}

public enum DISPLAYCONFIG_SCANLINE_ORDERING : uint
{
    UNSPECIFIED = 0,
    PROGRESSIVE = 1,
    INTERLACED = 2,
    INTERLACED_UPPERFIELDFIRST = 2, // Alias for INTERLACED
    INTERLACED_LOWERFIELDFIRST = 3,
    FORCE_UINT32 = 0xFFFFFFFF
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_RATIONAL
{
    public uint Numerator;
    public uint Denominator;
}

public enum DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY : uint
{
    OTHER = 0xFFFFFFFF,                // -1 in unsigned, mapped to 0xFFFFFFFF for P/Invoke compatibility
    HD15 = 0,
    SVIDEO = 1,
    COMPOSITE_VIDEO = 2,
    COMPONENT_VIDEO = 3,
    DVI = 4,
    HDMI = 5,
    LVDS = 6,
    D_JPN = 8,
    SDI = 9,
    DISPLAYPORT_EXTERNAL = 10,
    DISPLAYPORT_EMBEDDED = 11,
    UDI_EXTERNAL = 12,
    UDI_EMBEDDED = 13,
    SDTVDONGLE = 14,
    MIRACAST = 15,
    INDIRECT_WIRED = 16,
    INDIRECT_VIRTUAL = 17,
    DISPLAYPORT_USB_TUNNEL = 18,
    INTERNAL = 0x80000000,
    FORCE_UINT32 = 0xFFFFFFFF
}

public enum DISPLAYCONFIG_ROTATION : uint
{
    IDENTITY = 1,
    ROTATE90 = 2,
    ROTATE180 = 3,
    ROTATE270 = 4,
    FORCE_UINT32 = 0xFFFFFFFF
}

public enum DISPLAYCONFIG_SCALING : uint
{
    IDENTITY = 1,
    CENTERED = 2,
    STRETCHED = 3,
    ASPECTRATIOCENTEREDMAX = 4,
    CUSTOM = 5,
    PREFERRED = 128,
    FORCE_UINT32 = 0xFFFFFFFF
}


[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_PATH_SOURCE_INFO
{
    public LUID adapterId;
    public uint id;
    public uint modeInfoIdx; // Union: use as a single uint for modeInfoIdx

    public uint statusFlags;
}

[Flags]
public enum QDC : uint
{
    ALL_PATHS = 0x00000001,
    ONLY_ACTIVE_PATHS = 0x00000002,
    DATABASE_CURRENT = 0x00000004,
    VIRTUAL_MODE_AWARE = 0x00000010,
    INCLUDE_HMD = 0x00000020,
    VIRTUAL_REFRESH_RATE_AWARE = 0x00000040
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string viewGdiDeviceName;
}

[StructLayout(LayoutKind.Sequential)]
public struct DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS
{
    public uint value;

    // Helper properties to access the bitfields
    public bool FriendlyNameFromEdid
    {
        get => (value & 0x1) != 0;
        set
        {
            if (value)
                this.value |= 0x1;
            else
                this.value &= ~0x1u;
        }
    }

    public bool FriendlyNameForced
    {
        get => (value & 0x2) != 0;
        set
        {
            if (value)
                this.value |= 0x2;
            else
                this.value &= ~0x2u;
        }
    }

    public bool EdidIdsValid
    {
        get => (value & 0x4) != 0;
        set
        {
            if (value)
                this.value |= 0x4;
            else
                this.value &= ~0x4u;
        }
    }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
    public DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS flags;
    public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
    public ushort edidManufactureId;
    public ushort edidProductCodeId;
    public uint connectorInstance;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string monitorFriendlyDeviceName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string monitorDevicePath;
}