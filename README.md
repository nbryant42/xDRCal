# xDRCal

This is a small .NET application to help check the black or white luminosity clipping points of an SDR or HDR monitor.

![Screenshot](https://github.com/nbryant42/xDRCal/blob/main/screenshot.png?raw=true)

I mainly built this as an exploratory project to test out Direct2D HDR composition in a WinUI 3 setting.

## Developer Notes

As of Windows 11 24H2, Windows and WinUI 3 both have a number of platform limitations related to swap chains in general and HDR surfaces in
particular.

- **Don't use [ResizeBuffers](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgiswapchain-resizebuffers)
on a visible swap-chain**:  
This creates resize flicker. DirectComposition helps by batching changes atomically when you call
[Commit](https://learn.microsoft.com/en-us/windows/win32/api/dcomp/nf-dcomp-idcompositiondevice2-commit), but this is
not synchronized with
[Present](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgiswapchain-present), so build a new
resized swap-chain, `Present` it, and give that a bit of time to complete before switching the visual to use it and
calling `Commit`. There are still some unavoidable race conditions in this, but it eliminates most resize flicker.
(There is no public way to know when `Present` has actually completed and ready to `Commit`, which is the core source
of unavoidable flicker.)
- **Consider avoiding [SwapChainPanel](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.swapchainpanel):**  
`SwapChainPanel` can be hard to synchronize with XAML layout, and layering UI over `SwapChainPanel` is unreliable because
it presents directly to the window surface. See also
[#6919](https://github.com/microsoft/microsoft-ui-xaml/issues/6919).
Would the two-swap-chain trick have worked with `SwapChainPanel`? I'm not sure, but it seems better to have direct
control over `Commit`.
- **Use classic DirectComposition (DComp) for HDR surfaces as overlays:**  
Any `SwapChainPanel` issues can be sidestepped by presenting via
[classic DComp](https://learn.microsoft.com/en-us/windows/win32/api/dcomp/nf-dcomp-idcompositiondesktopdevice-createsurfacefromhwnd).
- **Input/event caveats for overlays:**  
If you overlay an `HWND` or DComp visual above your UI, ensure you handle input correctly (e.g., by punching a transparent
hole in the overlay), or input may not reach your controls. Even opaque DComp overlays may require manually setting the
mouse pointer style.
- **Transparency not supported for HDR surfaces:**  
As soon as you set an alpha mode other than Ignore, an HDR surface either returns invalid parameter or clamps to SDR.
So you cannot "punch holes in it" via transparency for controls layered below to show through.
- **Not possible to get layered surfaces via custom SpriteVisual**:  
[ICompositionDrawingSurfaceInterop::BeginDraw](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/win32/microsoft.ui.composition.interop/nf-microsoft-ui-composition-interop-icompositiondrawingsurfaceinterop-begindraw)
is unsupported, unlike UWP (access is blocked), and the
[CreateCompositionSurfaceForSwapChain](https://learn.microsoft.com/en-us/windows/win32/api/windows.ui.composition.interop/nn-windows-ui-composition-interop-icompositorinterop)
API that existed in UWP was removed from WinUI 3.
- **Not possible to get layered surfaces via child `HWND` (without XAML Islands):**  
WinUI 3 renders into a child window, but attempts to layer HDR below this will fail
(it occupies the whole client area, and doesn't use
[WS_EX_LAYERED](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)). That's not the case in
[XAML Islands](https://github.com/microsoft/WindowsAppSDK-Samples/tree/main/Samples/Islands) mode, but that mode has
tradeoffs, at least in terms of packaging, deployment, and boilerplate.
- **HDR swap-chains require a minimum size and refresh rate:**  
DWM will downgrade HDR swap-chains to SDR if their size drops below a certain threshold (e.g., 274 274 px @ 96dpi), or
if you present at less than ~24Hz.

## Installation

I've created a self-contained EXE file, built for Windows 11 24H4 x64 at a minimum.

Download the ZIP from the [release](https://github.com/nbryant42/xDRCal/releases), extract to any directory, and that's
it.

## Building

Install Visual Studio and the relevant workloads.

See here: https://learn.microsoft.com/en-us/windows/apps/get-started/start-here?tabs=vs-2022-17-10

(I believe this document may neglect to mention that you also need the .NET workload, and you will definitely need the
Windows 11 SDK 10.0.26100.0 component as well.)

## Usage

Drag one slider to full black, and the other to almost black to see where it's possible to see detail. Do the opposite
for the whites. Find the points where the checkerboard pattern is barely visible / just becomes invisible.

In SDR mode, you should be able to just barely see the difference between 00 and 01 (hexadecimal), and between
FE and FF; if you can't, it's probably a good idea to change your monitor's brightness setting (for the blacks) or
contrast (for the whites.)

In HDR mode, monitors can't really be calibrated; by design, the HDR signal exceeds the actual capabilities of any
real-world monitor, and monitors are expected to present the image as best they can. But for purposes of game
development or content mastering, it can be helpful to understand the points where displays clip to black or white under
various conditions. In HDR mode, the brightness slider scales from 0..1023 along with raw
[PQ](https://en.wikipedia.org/wiki/Perceptual_quantizer) values, and displays the corresponding brightness in nits.

The bottom slider scales the test pattern by screen area percentage. Most HDR monitors get brighter when they're only
tasked to show bright whites on a smaller percentage of the screen. Note, however, that the "nits" readout in the app is
only based on the signal we are sending to the monitor, and may not match the monitor's true capabilities, especially at
100% of the display area.

## See also

Note that this app can be in SDR mode while the system is in HDR mode, and vice-versa. If Windows is in HDR mode, it
will interpret SDR app output according to the
[piecewise sRGB gamma function](https://en.wikipedia.org/wiki/SRGB#Transfer_function_(%22gamma%22)). This means that
near-blacks will be a bit brighter than most monitors would display in SDR mode.

OLED panel users, in particular, may notice that their display follows a pure Gamma 2.2 curve when Windows is in SDR
mode. This is also true to a lesser degree on LCD panels (at least, my Samsung G65B), on which the low SDR signal levels
are definitely quite visible (if they followed a true Gamma 2.2, signal level 01 would imply a contrast ratio that LCD
panels cannot reproduce, and 01 would be indistinguishable from black). Thus, most LCD displays probably fall
somewhere in between Gamma 2.2 and sRGB, at the low end of the curve.

Opinions differ on whether sRGB or Gamma 2.2 is "best," and there is no One True Way. Some content was mastered to 2.2,
other content to sRGB, and it's anyone's guess which is which. For a workaround to change your Windows behavior in HDR
mode, see https://github.com/dylanraga/win11hdr-srgb-to-gamma2.2-icm