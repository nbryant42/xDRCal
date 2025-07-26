# xDRCal

This is a small .NET application to generate black/white clipping, gamma, and banding test patterns for SDR/HDR
monitors.

I mainly built this as an exploratory project to test out Direct2D HDR composition in a WinUI 3 setting. The learning
curve can be pretty steep; see [Developer Notes](#developer-notes) for all the gory details of the swap-chain and
composition API fighting that initially caused trouble.

![Screenshot](https://github.com/nbryant42/xDRCal/blob/main/screenshot.png?raw=true)

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

### Checkerboard test pattern (determines clipping points)

Drag one slider to full black, and the other to almost black to see where it's possible to see detail. Do the opposite
for the whites. Find the points where the checkerboard pattern is barely visible / just becomes invisible.

In SDR mode, you should be able to just barely see the difference between 00 and 01 (hexadecimal), and between
FE and FF; if you can't, it's probably a good idea to change your monitor's brightness setting (for the blacks) or
contrast (for the whites.) (This applies to sRGB monitors, but blacks may be crushed on TVs that follow Gamma 2.2.)

In HDR mode, monitors can't really be calibrated; by design, the HDR signal exceeds the actual capabilities of any
real-world monitor, and monitors are expected to present the image as best they can. But for purposes of game
development or content mastering, it can be helpful to understand the points where displays clip to black or white under
various conditions. In HDR mode, the brightness slider scales from 0..1023 along with raw
[PQ](https://en.wikipedia.org/wiki/Perceptual_quantizer) values, and displays the corresponding brightness in nits.

The bottom slider scales the test pattern by screen area percentage. Most HDR monitors get brighter when they're only
tasked to show bright whites on a smaller percentage of the screen. Note, however, that the "nits" readout in the app is
only based on the signal we are sending to the monitor, and may not match the monitor's true capabilities, especially at
100% of the display area.

### Gamma ramp test pattern

Flip to the right for a 16-bar gamma ramp. There are a few different ways this can work:

| Desktop mode | App mode | Displayed Gamma Ramp                                                                 |
| ------------ | -------- | ------------------------------------------------------------------------------------ |
| SDR          | SDR      | Monitor native gamma (typically sRGB-ish for PC monitors, or Gamma 2.2-ish for TV's) |
| SDR          | HDR      | PQ EOTF from 0-80 nits max (as distorted by monitor gamma if it deviates from sRGB)  |
| HDR          | SDR      | sRGB piecewise transfer function (assuming monitor follows PQ accurately)            |
| HDR          | HDR      | PQ EOTF as interpreted by the monitor (often inaccurately)                           |

Try setting SDR-mode sliders to 00-0F and F0-FF, especially for HDMI outputs; if you can't see 16 subtle-but-visible
shades of near-black and near-white, there are a few possibilities:

* Your TV is using Gamma 2.2 rather than sRGB. (Gamma 2.2 will crush the lowest blacks from 00-04 or so; this is
expected for TVs that follow Gamma 2.2, but not typical for PC monitors.)
* Check any "black level" settings in the monitor.
* The Full/Limited dynamic range setting is mismatched between the PC and the display. (This can be set in the Nvidia
control panel, etc.)

### Banding test pattern

This works similarly to the gamma ramp (see above), but renders a continuous gradient. You may find that monitors show
the least banding when operating in 10- or 12-bit WCG (non-HDR) mode; this may be the closest thing to "native" level
control.

### "HDR mode"

This really just selects the pixel format the app uses to present; either 8-bit nonlinear BGR or 16-bit floating point
scRGB.

### EOTF selection

If HDR mode is turned on, it is possible to select an EOTF or gamma function: PQ, sRGB, or Gamma 2.2. This affects the
meaning of the sliders and how the gamma ramp and banding tests are rendered. Note that if Windows is not in HDR
mode, the sRGB function will always match the monitor native gamma (even on TVs that use Gamma 2.2 etc), because it's
simply the inverse of the output translation that Windows performs for scRGB.

PQ EOTF seems to produce gradients with the least banding, including in high-bit-depth SDR modes.

## See also

PC monitors and Windows' SDR-to-HDR translation both tend to follow sRGB, and TVs tend to follow Gamma 2.2 or 2.4
depending on mode; but some PC ports of console games assume a TV output, which can make a visible difference,
particularly on OLED where users expect deep blacks. If this affects you, see
https://github.com/dylanraga/win11hdr-srgb-to-gamma2.2-icm for a manual workaround.

## Developer Notes

As of Windows 11 24H2, Windows and WinUI 3 both have a number of subtle gotchas related to swap chains in general
and HDR surfaces in particular.

- **IDXGISwapChain3.SetColorSpace1 can be unreliable:**  
This call was causing the compositor to get stuck in a black-frame state for some reason after surface resize; probably
a Windows or NVidia bug. Since it was only added on an "explicit is better than implicit" theory, I've removed it.
(Setting `ColorSpaceType.RgbFullG10NoneP709` on a surface that defaults to scRGB is just belt-and-suspenders.)
Implications for other projects are unknown, but if you need colorspace translation, consider shaders.
- **Don't use [ResizeBuffers](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgiswapchain-resizebuffers)
on a visible swap-chain:**  
This creates resize flicker, at least if you're also centering. DirectComposition helps by batching changes atomically
when you call [Commit](https://learn.microsoft.com/en-us/windows/win32/api/dcomp/nf-dcomp-idcompositiondevice2-commit),
but this is not synchronized with
[Present](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgiswapchain-present), so build a new
resized swap-chain, `Present` it, and give that a bit of time to complete before switching the visual to use it and
calling `Commit`. There are still some unavoidable race conditions in this, but it eliminates most resize flicker.
(There is no public way to know when `Present` has actually completed and ready to `Commit`, which is the core source
of unavoidable flicker.)
- **Consider calling `Present` multiple times on resize:**  
In this app, calling my `Render()` function 3 times (for a 2-buffer swap-chain) reduces resize flicker to almost zero.
The 3rd `Present` is more likely to block, helping to guarantee the swapchain is ready to pass to `Commit`.
- **[SwapChainPanel](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.swapchainpanel)
setup is sequence/timing dependent and prone to race conditions:**    
Do not call [ISwapChainPanelNative.SetSwapChain](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/win32/microsoft.ui.xaml.media.dxinterop/nf-microsoft-ui-xaml-media-dxinterop-iswapchainpanelnative-setswapchain)
until your swap-chain is fully presented. (May require multiple Present calls, as discussed above. `SwapChainPanel` is
committing to a DComp surface under the covers.) It might work fine for SDR surfaces, but HDR surfaces will behave very
strangely and misleadingly. If you see HDR surfaces that appear to "clamp" to SDR, or find that you need to force things
with a game-style render loop, this is probably the cause.