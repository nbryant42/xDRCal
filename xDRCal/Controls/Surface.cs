using Microsoft.UI.Xaml.Controls;
using SharpGen.Runtime;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace xDRCal.Controls;

/// <summary>
/// Ties together the minimum set of API references we need to hold for each D2D SwapChainPanel we create, and
/// gives us a standard place to implement the render logic.
/// </summary>
public abstract partial class Surface : SwapChainPanel, IDisposable
{
    // general naming convention is _ prefix on IDisposable's
    private ID3D11Device? _d3dDevice;
    private IDXGIFactory2? _dxgiFactory;

    protected ID2D1Factory1? _d2dFactory;
    private ID2D1Device? _d2dDevice;
    protected ID2D1DeviceContext? _d2dContext;
    protected IDWriteFactory? _dwriteFactory;
    private Vortice.WinUI.ISwapChainPanelNative? _panelNative;
    protected ID2D1SolidColorBrush? _brush;
    protected IDXGISwapChain1? _swapChain;
    protected ID2D1Bitmap1? _d2dTargetBitmap;

    private bool hdrMode;
    protected bool HasAlpha { get; set; }

    public Surface()
    {
        Loaded += (_, _) => InitializeDirectX();
        SizeChanged += (_, _) => InitializeDirectX();
        CompositionScaleChanged += (_, _) => InitializeDirectX();
        Unloaded += (_, _) => Dispose();
    }

    public void Dispose()
    {
        _swapChain?.Dispose();
        _swapChain = null;
        _brush?.Dispose();
        _brush = null;
        _d2dTargetBitmap?.Dispose();
        _d2dTargetBitmap = null;
        _d2dContext?.Dispose();
        _d2dContext = null;
        _d2dFactory?.Dispose();
        _d2dFactory = null;
        _d3dDevice?.Dispose();
        _d3dDevice = null;
        _dwriteFactory?.Dispose();
        _dwriteFactory = null;
        _dxgiFactory?.Dispose();
        _dxgiFactory = null;
        _panelNative?.Dispose();
        _panelNative = null;
        GC.SuppressFinalize(this);
    }

    public bool HdrMode
    {
        get => hdrMode;
        set
        {
            if (hdrMode != value)
            {
                hdrMode = value;
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                // fire-and-forget should be fine here.
                ResizeRenderTarget();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            }
        }
    }

    private async void InitializeDirectX()
    {
        try
        {
            if (_d3dDevice == null)
            {
                _d3dDevice = CreateD3DDevice();
                _dxgiFactory = GetFactory();
                _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded);
                using IDXGIDevice dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
                _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
                _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

                _dwriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();
                _panelNative = GetPanelNative();
            }
            await ResizeRenderTarget();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
        }
    }

    private Vortice.WinUI.ISwapChainPanelNative GetPanelNative()
    {
        using var comObject = new ComObject(this);
        return comObject.QueryInterface<Vortice.WinUI.ISwapChainPanelNative>();
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

    private static IDXGIFactory2 GetFactory()
    {
        var result = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false, out var dxgiFactory);

        if (dxgiFactory == null || result.Failure)
        {
            throw new InvalidOperationException("Failed to create DXGI factory");
        }
        return dxgiFactory;
    }

    // Currently, only the main test-pattern surface ever needs to be RE-sized, but this method also performs the
    // initial allocation too. Must call this method after construction.
    public async Task ResizeRenderTarget()
    {
        if (_d2dContext == null || _dxgiFactory == null || _panelNative == null ||
            ActualHeight == 0 || ActualWidth == 0)
        {
            return;
        }

        // We'll release old target etc after resizing.
        var oldTarget = _d2dTargetBitmap;
        var oldBrush = _brush;
        var oldSwapChain = _swapChain;

        _d2dContext.Target = null;

        // Allocate the swap-chain in device-dependent pixels:
        uint width = (uint)MathF.Round((float)ActualWidth * CompositionScaleX);
        uint height = (uint)MathF.Round((float)ActualHeight * CompositionScaleY);

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
                AlphaMode = HasAlpha ? Vortice.DXGI.AlphaMode.Premultiplied : Vortice.DXGI.AlphaMode.Ignore
            };

            // Swap the, uhh... what are we doing? Swap the swap chain. Yeah, that's the ticket.
            //
            // Seriously, we do it this way for atomicity, to avoid flicker. Swap-chains present directly to the visible
            // surface, and are not synchronized with the batched-updates concept in the DComp visual tree. But we can
            // atomically* .Commit() updates to the visual tree (indirectly; SwapChainPanel does this under the covers)
            // if it's pointing to a *new swap-chain*.
            //
            // * (There are still some caveats and unavoidable races, leading to black-frame flicker, but this mostly
            //    works.)
            _swapChain = _dxgiFactory.CreateSwapChainForComposition(_d3dDevice, swapDesc);

            using var swapChain2 = _swapChain.QueryInterface<IDXGISwapChain2>();
            swapChain2.MatrixTransform = Matrix3x2.CreateScale(1.0f / CompositionScaleX, 1.0f / CompositionScaleY);

            // Do not enable; causes black-frame freezes (probable Windows bug.)
            //if (HdrMode)
            //{
            //    var swapChain3 = _swapChain.QueryInterfaceOrNull<IDXGISwapChain3>();
            //    if (swapChain3 is not null)
            //    {
            //        swapChain3.SetColorSpace1(ColorSpaceType.RgbFullG10NoneP709);
            //    }
            //}

            using var backBuffer = GetBuffer();
            using var dxgiSurface = backBuffer.QueryInterface<IDXGISurface>();

            var bitmapAlpha = HasAlpha ? Vortice.DCommon.AlphaMode.Premultiplied : Vortice.DCommon.AlphaMode.Ignore;
            var props = new BitmapProperties1(
                new PixelFormat(format, bitmapAlpha),
                96, 96,
                BitmapOptions.Target | BitmapOptions.CannotDraw);

            _d2dTargetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(dxgiSurface, props);
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

        _panelNative.SetSwapChain(_swapChain);

        // idk, to me it seems safer to defer this 'till the next UI tick:
        await Task.Yield();
        oldTarget?.Dispose();
        oldBrush?.Dispose();
        oldSwapChain?.Dispose();
    }
    private Format GetPixelFormat()
    {
        return HdrMode ? Format.R16G16B16A16_Float : Format.B8G8R8A8_UNorm;
    }

    private ID3D11Texture2D GetBuffer()
    {
        if (_swapChain == null)
        {
            throw new InvalidOperationException("Missing swap chain");
        }

        return _swapChain.GetBuffer<ID3D11Texture2D>(0);
    }

    public abstract void Render();
}