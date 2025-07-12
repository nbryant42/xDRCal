using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SharpGen.Runtime;
using System;
using System.Diagnostics;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace xDRCal.Controls;

public sealed partial class CalibrationDisplay : SwapChainPanel
{
    private ID3D11Device? _d3dDevice;
    private IDXGISwapChain1? _swapChain;

    private ID2D1Factory1? _d2dFactory;
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _d2dContext;
    private ID2D1SolidColorBrush? _brush;

    private ID2D1Bitmap1? _d2dTargetBitmap;
    private bool hdrMode;
    private short luminosityA, luminosityB;

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
            ResizeRenderTarget();
        }
    }

    private DispatcherQueueTimer? _renderTimer;
    private bool _isUnloading = false;

    public CalibrationDisplay()
    {
        this.Loaded += OnLoaded;
        this.SizeChanged += OnSizeChanged;
        this.Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_swapChain == null && ActualWidth > 0 && ActualHeight > 0)
        {
            InitializeDirectX();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isUnloading = true;
        _renderTimer?.Stop();
        _renderTimer = null;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ActualWidth > 0 && ActualHeight > 0)
        {
            if (_swapChain == null)
            {
                InitializeDirectX();
            }
            else
            {
                ResizeRenderTarget();
            }
        }
    }

    // We need to keep calling Present() on the swap chain fairly frequently, otherwise
    // there are issues with composition clamping the brightness to SDR white. I haven't
    // seen any hints of this in Windows Photo Viewer etc (e.g, the NVidia Overlay popup
    // doesn't appear in other apps, providing a hint that they're not using constantly-
    // refreshed swap chains), so it seems likely to be a WinUI 3-specific bug.
    private void StartRenderLoop()
    {
        _renderTimer = DispatcherQueue.CreateTimer();
        _renderTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / 30.0);
        _renderTimer.Tick += (_, _) => Render();
        _renderTimer.Start();
    }

    private static IDXGIFactory2 getFactory()
    {
        IDXGIFactory2? dxgiFactory;
        var result = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false, out dxgiFactory);

        if (dxgiFactory == null || result.Failure)
        {
            throw new InvalidOperationException("Failed to create D3D11 device");
        }
        return dxgiFactory;
    }


    private Vortice.WinUI.ISwapChainPanelNative GetPanelNative()
    {
        using ComObject comObject = new ComObject(this);
        return comObject.QueryInterface<Vortice.WinUI.ISwapChainPanelNative>();
    }

    private Format GetPixelFormat()
    {
        return HdrMode ? Format.R16G16B16A16_Float : Format.B8G8R8A8_UNorm;
    }

    private void InitializeDirectX()
    {
        try
        {
            Vortice.Direct3D.FeatureLevel[] featureLevels = new[]
            {
                    Vortice.Direct3D.FeatureLevel.Level_11_1,
                    Vortice.Direct3D.FeatureLevel.Level_11_0,
                    Vortice.Direct3D.FeatureLevel.Level_10_1
                };

            ID3D11DeviceContext _d3dContext;

#if DEBUG
            var flags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.Debug;
#else
                var flags = DeviceCreationFlags.BgraSupport;
#endif

            // D3D11 device
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, featureLevels, out _d3dDevice, out _d3dContext);

            // DXGI swapchain
            using var dxgiFactory = getFactory();

            var swapDesc = new SwapChainDescription1
            {
                Format = GetPixelFormat(),
                Width = (uint)Math.Max(ActualWidth, 8),
                Height = (uint)Math.Max(ActualHeight, 8),
                BufferCount = 2,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                SwapEffect = SwapEffect.FlipSequential,
                Scaling = Scaling.Stretch,
                AlphaMode = Vortice.DXGI.AlphaMode.Ignore
            };

            _swapChain = dxgiFactory.CreateSwapChainForComposition(_d3dDevice, swapDesc);

            GetPanelNative().SetSwapChain(_swapChain);

            _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
            using IDXGIDevice dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
            _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
            _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

            ResizeRenderTarget();
            StartRenderLoop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
        }
    }

    private ID3D11Texture2D? GetBuffer()
    {
        if (_swapChain == null)
        {
            throw new InvalidOperationException("Missing swap chain");
        }

        _swapChain.GetBuffer<ID3D11Texture2D>(0, out var backBuffer);
        return backBuffer;
    }

    private void ResizeRenderTarget()
    {
        try
        {
            if (_swapChain == null)
            {
                throw new InvalidOperationException("Missing swap chain");
            }

            if (_d2dContext == null)
            {
                throw new InvalidOperationException("Missing D2D context");
            }

            // Release old target before resizing
            _d2dContext.Target?.Dispose();
            _d2dContext.Target = null;

            _d2dTargetBitmap?.Dispose();
            _d2dTargetBitmap = null;

            _brush?.Dispose();
            _brush = null;

            var format = GetPixelFormat();

            var result = _swapChain.ResizeBuffers(
                2,
                (uint)Math.Max(ActualWidth, 8),
                (uint)Math.Max(ActualHeight, 8),
                format,
                SwapChainFlags.None);

            if (result.Failure)
            {
                return;
            }

            using var backBuffer = GetBuffer();
            using var dxgiSurface = backBuffer?.QueryInterface<IDXGISurface>();

            var props = new BitmapProperties1(
                new PixelFormat(format, Vortice.DCommon.AlphaMode.Ignore),
                96, 96,
                BitmapOptions.Target | BitmapOptions.CannotDraw);

            _d2dTargetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(dxgiSurface, props);
            _d2dContext.Target = _d2dTargetBitmap;
            _brush = _d2dContext.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
        }

        Render();
    }

    private void Render()
    {
        try
        {
            if (_isUnloading || _d2dContext == null || _swapChain == null || _brush == null)
                return;

            _d2dContext.BeginDraw();
            _d2dContext.Clear(new Color4(0, 0, 0, 1));

            float width = (float)ActualWidth;
            float height = (float)ActualHeight;

            // ActualWidth and ActualHeight can be 0 early in the app lifecycle.
            if (width < 1 || height < 1)
                return; // Skip frame

            float cellWidth = width * 0.125f;
            float cellHeight = height * 0.125f;
            float lumaA, lumaB;

            if (HdrMode)
            {
                lumaA = Util.PQCodeToNits(LuminosityA) * 0.0125f; // 1/80 nits
                lumaB = Util.PQCodeToNits(LuminosityB) * 0.0125f;
            }
            else
            {
                lumaA = LuminosityA / 255.0f;
                lumaB = LuminosityB / 255.0f;
            }

            for (int row = 0; row < 8; row++)
            {
                for (int col = 0; col < 8; col++)
                {
                    bool isA = (row + col) % 2 == 0;
                    float luma = isA ? lumaA : lumaB;
                    _brush.Color = new Color4(luma, luma, luma, 1);

                    var rect = new Rect(
                        col * cellWidth,
                        row * cellHeight,
                        (col + 1) * cellWidth - 1,
                        (row + 1) * cellHeight - 1);

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
