using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace xDRCal.Visuals;

public interface ISurfaceHost
{
    bool IsUnloading { get; }
    IDCompositionDesktopDevice? DcompDevice { get; }
    IDXGIFactory2? DxgiFactory { get; }
    ID2D1DeviceContext? D2dContext { get; }
    ID3D11Device? D3dDevice { get; }
    ID2D1Factory1? D2dFactory { get; }
    IDWriteFactory? DwriteFactory { get; }

    IntPtr Hwnd { get; }
}

/// <summary>
/// Ties together the minimum set of API references we need to hold for each classic DComp surface we create, and
/// gives us a standard place to implement the render logic.
/// </summary>
public abstract partial class Surface : IDisposable
{
    // device-DEPENDENT pixel size (assuming square surface) below which Windows clamps HDR swap-chains to SDR
    public const int MIN_SIZE = 274;

    // shared refs across surfaces: (we are not responsible for Dispose)
    protected readonly ISurfaceHost host;
    private readonly ID3D11Device? _d3dDevice;
    private readonly IDXGIFactory2? _dxgiFactory;
    protected readonly ID2D1Factory1? _d2dFactory;
    private readonly IDCompositionDesktopDevice? _dcompDevice;

    // shared ref, but each surface must reset its Target on each draw:
    protected readonly ID2D1DeviceContext? _d2dContext;

    // per-surface props (these affect visible output)
    protected RectI pos; // device-dependent coordinates!
    private bool hdrMode;
    protected bool HasAlpha { get; set; }

    // per-surface obj refs (must dispose)
    protected IDXGISwapChain1? _swapChain;
    private IDCompositionVisual2? _visual;
    protected ID2D1SolidColorBrush? _brush;
    protected ID2D1Bitmap1? _d2dTargetBitmap;

    public Surface(IDCompositionVisual2? _parentVisual, ISurfaceHost host, bool insertAbove = true,
        Surface? referenceSurface = null)
    {
        this.host = host;
        _dcompDevice = host.DcompDevice;
        _dxgiFactory = host.DxgiFactory;
        _d2dContext = host.D2dContext;
        _d2dFactory = host.D2dFactory;
        _d3dDevice = host.D3dDevice;
        if (_dcompDevice == null || _parentVisual == null)
        {
            throw new InvalidOperationException("missing reference");
        }
        _visual = _dcompDevice.CreateVisual();
        _parentVisual.AddVisual(_visual, insertAbove, referenceSurface?._visual);
    }

    public void Dispose()
    {
        _swapChain?.Dispose();
        _swapChain = null;
        _visual?.Dispose();
        _visual = null;
        _brush?.Dispose();
        _brush = null;
        _d2dTargetBitmap?.Dispose();
        _d2dTargetBitmap = null;
        GC.SuppressFinalize(this);
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

    // Currently, only the main test-pattern surface ever needs to be RE-sized, but this method also performs the
    // initial allocation too. Must call this method after construction.
    public async Task ResizeRenderTarget(bool commit = true)
    {
        if (_dcompDevice == null || _d2dContext == null || _visual == null || _dxgiFactory == null ||
            pos.Height == 0 || pos.Width == 0)
        {
            return;
        }

        // We'll release old target etc after resizing.
        var oldTarget = _d2dContext.Target;
        var oldTargetBitmap = _d2dTargetBitmap;
        var oldBrush = _brush;
        var oldSwapChain = _swapChain;

        uint width = (uint)pos.Width;
        uint height = (uint)pos.Height;

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
            // atomically* .Commit() updates to the visual tree if it's pointing to a *new swap-chain*.
            //
            // * (There are still some caveats and unavoidable races, leading to black-frame flicker, but this mostly
            //    works.)
            _swapChain = _dxgiFactory.CreateSwapChainForComposition(_d3dDevice, swapDesc);

            if (HdrMode)
            {
                var swapChain3 = _swapChain.QueryInterfaceOrNull<IDXGISwapChain3>();
                if (swapChain3 is not null)
                {
                    swapChain3.SetColorSpace1(ColorSpaceType.RgbFullG10NoneP709);
                }
            }

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

        // defer the DComp commit a bit more to give the Present time to do its work (async)
        await Task.Yield();

        try
        {
            // caller must adjust requested offset to account for our size workaround (if applicable)
            _visual.SetOffsetX(pos.X);
            _visual.SetOffsetY(pos.Y);
            _visual.SetContent(_swapChain);

            if (commit)
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

    public bool HitTest(int x, int y)
    {
        return pos.Contains(x, y);
    }

    /// <summary>
    /// Default implementation does nothing.
    /// </summary>
    public virtual void Clicked()
    {
    }
}