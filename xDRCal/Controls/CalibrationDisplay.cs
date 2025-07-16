using Microsoft.UI.Dispatching;
using SharpGen.Runtime;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace xDRCal.Controls;

public sealed partial class CalibrationDisplay : IDisposable
{
    private ID3D11Device? _d3dDevice;
    private IDXGIFactory2? _dxgiFactory;
    private IDXGISwapChain1? _swapChain;

    private ID2D1Factory1? _d2dFactory;
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _d2dContext;
    private ID2D1SolidColorBrush? _brush;

    private ID2D1Bitmap1? _d2dTargetBitmap;
    private bool hdrMode;
    private short luminosityA, luminosityB;

    private IDCompositionDesktopDevice? _dcompDevice;
    private IDCompositionVisual2? _visual;

    public short LuminosityA
    {
        get => luminosityA;
        set
        {
            luminosityA = value;
            Render();
        }
    }
    public short LuminosityB
    {
        get => luminosityB;
        set
        {
            luminosityB = value;
            Render();
        }
    }

    public bool HdrMode
    {
        get => hdrMode;
        set
        {
            hdrMode = value;
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            // fire-and-forget should be fine here.
            ResizeRenderTarget();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }
    }

    private DispatcherQueueTimer? _renderTimer;
    private bool _isUnloading = false;

    private RectI pos;

    public CalibrationDisplay(IntPtr hwnd)
    {
        this.hwnd = hwnd;
        InitializeDirectX();
    }

    public void Dispose()
    {
        _isUnloading = true;

        _renderTimer?.Stop();
        _renderTimer = null;

        _d3dDevice?.Dispose();
        _d3dDevice = null;
        _swapChain?.Dispose();
        _swapChain = null;
        _d2dFactory?.Dispose();
        _d2dFactory = null;
        _d2dDevice?.Dispose();
        _d2dDevice = null;
        _d2dContext?.Dispose();
        _d2dContext = null;
        _brush?.Dispose();
        _brush = null;
        _d2dTargetBitmap?.Dispose();
        _d2dTargetBitmap = null;
        _dcompDevice?.Dispose();
        _dcompDevice = null;
        _visual?.Dispose();
        _visual = null;
        _dxgiFactory?.Dispose();
        _dxgiFactory = null;
    }

    public void Reposition(RectI _pos)
    {
        if (_pos.Width > 0 && _pos.Height > 0)
        {
            pos = _pos;

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            // fire-and-forget should be fine here.
            ResizeRenderTarget();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }
    }

    // Must call Render() at >=24Hz to prevent DComp from downgrading the output to SDR.
    // This also happens in SwapChainPanel; apparently a well-known quirk of Windows
    // (implemented for power conservation, etc)
    private void StartRenderLoop()
    {
        _renderTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _renderTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / 24.0);
        _renderTimer.Tick += (_, _) => Render();
        _renderTimer.Start();
    }

    private static IDXGIFactory2 GetFactory()
    {
        IDXGIFactory2? dxgiFactory;
        var result = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false, out dxgiFactory);

        if (dxgiFactory == null || result.Failure)
        {
            throw new InvalidOperationException("Failed to create DXGI factory");
        }
        return dxgiFactory;
    }


    private Format GetPixelFormat()
    {
        return HdrMode ? Format.R16G16B16A16_Float : Format.B8G8R8A8_UNorm;
    }

    private static T DCompositionCreateDevice3<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(IUnknown renderingDevice) where T : IDCompositionDevice2
    {
        DCompositionCreateDevice3(renderingDevice, typeof(T).GUID, out var dcompositionDevice).CheckError();
#pragma warning disable CS8603 // Possible null reference return.
        return MarshallingHelpers.FromPointer<T>(dcompositionDevice);
#pragma warning restore CS8603 // Possible null reference return.
    }

    private unsafe static Result DCompositionCreateDevice3(IUnknown renderingDevice, Guid iid,
        out nint dcompositionDevice)
    {
        _ = IntPtr.Zero;
        nint renderingDevice2 = MarshallingHelpers.ToCallbackPtr<IUnknown>(renderingDevice);
        Result result;
        fixed (nint* ptr = &dcompositionDevice)
        {
            void* dcompositionDevice2 = ptr;
            result = DCompositionCreateDevice3_((void*)renderingDevice2, &iid, dcompositionDevice2);
        }

        GC.KeepAlive(renderingDevice);
        return result;
    }

    [LibraryImport("dcomp.dll", EntryPoint = "DCompositionCreateDevice3")]
    [UnmanagedCallConv(CallConvs = new Type[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
    internal static unsafe partial int DCompositionCreateDevice3_(void* _renderingDevice, void* _iid,
        void* _dcompositionDevice);

    private async void InitializeDirectX()
    {
        try
        {
            _d3dDevice = CreateD3DDevice();
            _dxgiFactory = GetFactory();
            _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
            using IDXGIDevice dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
            _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
            _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

            // dcomp setup
            _dcompDevice = DCompositionCreateDevice3<IDCompositionDesktopDevice>(dxgiDevice);
            var dcompTarget = _dcompDevice.CreateSurfaceFromHwnd(hwnd, true);

            _visual = _dcompDevice.CreateVisual();

            dcompTarget.SetRoot(_visual);

            await ResizeRenderTarget();
            StartRenderLoop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
        }
    }

    private static ID3D11Device CreateD3DDevice()
    {
        Vortice.Direct3D.FeatureLevel[] featureLevels =
        [
            Vortice.Direct3D.FeatureLevel.Level_11_1,
            Vortice.Direct3D.FeatureLevel.Level_11_0,
            Vortice.Direct3D.FeatureLevel.Level_10_1
        ];

#if DEBUG
        var flags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.Debug;
#else
                var flags = DeviceCreationFlags.BgraSupport;
#endif

        return D3D11.D3D11CreateDevice(DriverType.Hardware, flags, featureLevels); ;
    }

    private ID3D11Texture2D GetBuffer()
    {
        if (_swapChain == null)
        {
            throw new InvalidOperationException("Missing swap chain");
        }

        return _swapChain.GetBuffer<ID3D11Texture2D>(0);
    }

    // pixel size below which Windows clamps HDR swap-chains to SDR
    private const int minSize = 274;
    private readonly nint hwnd;

    private async Task ResizeRenderTarget()
    {
        if (_dcompDevice == null || _d2dContext == null || _visual == null || _dxgiFactory == null)
        {
            return;
        }

        // We'll release old target etc after resizing.
        var oldTarget = _d2dContext.Target;
        var oldTargetBitmap = _d2dTargetBitmap;
        var oldBrush = _brush;
        var oldSwapChain = _swapChain;

        // Windows will downgrade small swap-chains to SDR. Work around by keeping a
        // minimum size and drawing a dark grey border. (Transparency is unsupported.)
        uint width = (uint)Math.Max(minSize, pos.Width);
        uint height = (uint)Math.Max(minSize, pos.Height);

        try
        {
            var format = GetPixelFormat();

            var swapDesc = new SwapChainDescription1
            {
                Format = format,
                Width = width,
                Height = height,
                BufferCount = 2,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                SwapEffect = SwapEffect.FlipDiscard,
                Scaling = Scaling.Stretch,
                AlphaMode = Vortice.DXGI.AlphaMode.Ignore
            };

            // Swap the, uhh... what are we doing? Swap the swap chain. Yeah, that's the ticket.
            //
            // Seriously, we do it this way for atomicity, to avoid flicker. Swap-chains present directly to the device,
            // and are not synchronized with the DComp visual tree. But we can atomically* .Commit() updates
            // to the visual tree if it's pointing to a *new swap-chain*.
            //
            // * (There are still some caveats and unavoidable races, leading to black-frame flicker, but this mostly
            //    works.)
            oldSwapChain = _swapChain;
            _swapChain = _dxgiFactory.CreateSwapChainForComposition(_d3dDevice, swapDesc);

            using var backBuffer = GetBuffer();
            using var dxgiSurface = backBuffer.QueryInterface<IDXGISurface>();

            var props = new BitmapProperties1(
                new PixelFormat(format, Vortice.DCommon.AlphaMode.Ignore),
                96, 96,
                BitmapOptions.Target | BitmapOptions.CannotDraw);

            _d2dTargetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(dxgiSurface, props);
            _d2dContext.Target = _d2dTargetBitmap;
            _brush = _d2dContext.CreateSolidColorBrush(new Color4(1, 1, 1));
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
            return;
        }

        // get buffer count (Windows may override our request)
        var count = _swapChain.Description1.BufferCount;
        // if 2 buffers, render 3X; third is more likely to block, helping with .Commit() sync.
        for (var i = 0; i <= count; i++)
        {
            Render();
        }

        // defer the DComp commit a bit more to give the Present time to do its work (async)
        await Task.Yield();

        try
        {
            // adjust requested offset to account for our size workaround
            _visual.SetOffsetX(Math.Max(0, pos.X - (width - pos.Width) / 2));
            _visual.SetOffsetY(Math.Max(0, pos.Y - (height - pos.Height) / 2));
            _visual.SetContent(_swapChain);
            _dcompDevice.Commit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
            return;
        }

        // idk, to me it seems safer to defer this 'till the next UI tick:
        await Task.Yield();
        oldTarget?.Dispose();
        oldTargetBitmap?.Dispose();
        oldBrush?.Dispose();
        oldSwapChain?.Dispose();
    }

    private void Render()
    {
        try
        {
            if (_isUnloading || _d2dContext == null || _swapChain == null || _brush == null ||
                pos.Width <= 0 || pos.Height <= 0)
                return;

            _d2dContext.BeginDraw();

            // leave a border to account for our minSize workaround
            var topBorder = Math.Max(0, (minSize - pos.Width) / 2);
            var leftBorder = Math.Max(0, (minSize - pos.Height) / 2);

            var grey = 23.0f / 255.0f; // = #171717 to match our XAML background
            float cellWidth = pos.Width * 0.125f; // 1/8
            float cellHeight = pos.Height * 0.125f;
            float lumaA, lumaB;

            if (HdrMode)
            {
                lumaA = Util.PQCodeToNits(LuminosityA) * 0.0125f; // 1/80 nits
                lumaB = Util.PQCodeToNits(LuminosityB) * 0.0125f;
                grey = Util.SrgbToLinear(grey);
            }
            else
            {
                lumaA = LuminosityA / 255.0f;
                lumaB = LuminosityB / 255.0f;
            }

            _d2dContext.Clear(new Color4(grey, grey, grey));

            for (int row = 0; row < 8; row++)
            {
                for (int col = 0; col < 8; col++)
                {
                    bool isA = (row + col) % 2 == 0;
                    float luma = isA ? lumaA : lumaB;
                    _brush.Color = new Color4(luma, luma, luma);

                    var rect = new Rect(
                        leftBorder + col * cellWidth,
                        topBorder + row * cellHeight,
                        cellWidth + 1,
                        cellHeight + 1);

                    _d2dContext.FillRectangle(rect, _brush);
                }
            }

            _d2dContext.EndDraw();
            _swapChain.Present(1, PresentFlags.None);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
        }
    }
}
