# xDRCal

This is a small .NET application to help check the black or white luminosity clipping points of an SDR or HDR monitor.

![Screenshot](https://github.com/nbryant42/xDRCal/blob/main/screenshot.png?raw=true)

I mainly built this as a demo project to test out Direct2D HDR composition in a WinUI 3 project.

## Installation

I've created a self-contained EXE file, built for Windows 11 24H4 x64 at a minimum.

Download the ZIP from the [release](https://github.com/nbryant42/xDRCal/releases), extract to any directory, and that's it.

## Building

You'll need to install Visual Studio and the relevant workloads.

See here: https://learn.microsoft.com/en-us/windows/apps/get-started/start-here?tabs=vs-2022-17-10

(I believe this document may neglect to mention that you also need the .NET workload, and you will definitely need the Windows 11 SDK 10.0.26100.0 component as well.)

## Usage

Drag one slider to full black, and the other to almost black to see where it's possible to see detail. Do the opposite for the whites.

The bottom slider scales the test pattern by screen area percentage. Most HDR monitors get brighter when they only tasked to show bright whites on a smaller percentage of the screen. Note, however, that the "nits" readout in the app is only based on the signal we are sending to the monitor, and may not match the monitor's true capabilities, especially at 100% of the display area.

## Known issue

HDR test pattern UI controls are composited into a single HWND using the WinUI XAML layer via a SwapChainPanel. This leads to occasional
glitches where the HDR output overwrites the control panel. The fix appears to involve splitting SDR and HDR into child HWNDs. Until I find
the time for that, bang on F11 or F12 a few times, hover the mouse over where you think the controls are, or just hit Alt-F4.

## See also

OLED panel users may notice that their display follows a pure Gamma 2.2 curve when Windows is in SDR mode, leading to black crush, but that SDR programs (such as this one, when the SDR toggle is turned off) are mapped to higher sRGB brightness levels in the blacks from 0x00 through 0x04 or so. This tends not to be the case on LCD panels (at least, not my Samsung G65B), on which the low SDR signal levels are definitely quick visible.

Opinions differ on what's best, and there is no One True Way. Some content was mastered to 2.2, other content to sRGB, and it's anyone's guess which is which. To change your Windows behavior in SDR mode, see https://github.com/dylanraga/win11hdr-srgb-to-gamma2.2-icm