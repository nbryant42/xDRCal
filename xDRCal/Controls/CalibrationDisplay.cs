using Microsoft.UI.Dispatching;
using SharpGen.Runtime;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace xDRCal.Controls;

public sealed class CalibrationDisplay(IntPtr _hwnd) : IDisposable
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
            ResizeRenderTarget();
        }
    }

    private DispatcherQueueTimer? _renderTimer;
    private bool _isUnloading = false;

    private RectI pos;
    private readonly IntPtr hwnd = _hwnd;

    public void Init()
    {
        if (_swapChain == null && pos.Width > 0 && pos.Height > 0)
        {
            InitializeDirectX();
        }
    }

    public void Dispose()
    {
        _isUnloading = true;
        // TODO release refs
        _renderTimer?.Stop();
        _renderTimer = null;
    }

    public void Reposition(RectI _pos)
    {
        if (_pos.Width > 0 && _pos.Height > 0)
        {
            pos = _pos;

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

    // Must call Render() at least 24Hz to prevent DComp from downgrading the output to SDR.
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

    private unsafe static Result DCompositionCreateDevice3(IUnknown renderingDevice, Guid iid, out nint dcompositionDevice)
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

    [DllImport("dcomp.dll", CallingConvention = CallingConvention.StdCall, EntryPoint = "DCompositionCreateDevice3")]
    private unsafe static extern int DCompositionCreateDevice3_(void* _renderingDevice, void* _iid,
        void* _dcompositionDevice);

    private void InitializeDirectX()
    {
        try
        {
            _d3dDevice = CreateD3DDevice();

            using var dxgiFactory = GetFactory();

            var swapDesc = new SwapChainDescription1
            {
                Format = GetPixelFormat(),
                Width = (uint)Math.Max(pos.Width, 8),
                Height = (uint)Math.Max(pos.Height, 8),
                BufferCount = 2,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                SwapEffect = SwapEffect.FlipDiscard,
                Scaling = Scaling.Stretch,
                AlphaMode = Vortice.DXGI.AlphaMode.Ignore
            };

            _swapChain = dxgiFactory.CreateSwapChainForComposition(_d3dDevice, swapDesc);

            _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
            using IDXGIDevice dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
            _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
            _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

            // dcomp setup
            _dcompDevice = DCompositionCreateDevice3<IDCompositionDesktopDevice>(dxgiDevice);
            var dcompTarget = _dcompDevice.CreateSurfaceFromHwnd(hwnd, true);

            _visual = _dcompDevice.CreateVisual();
            _visual.SetContent(_swapChain);

            dcompTarget.SetRoot(_visual);

            ResizeRenderTarget();
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

    private void ResizeRenderTarget()
    {
        if (_swapChain == null || _dcompDevice == null || _d2dContext == null || _visual == null)
        {
            return;
        }

        try
        {
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
                (uint)Math.Max(pos.Width, 8),
                (uint)Math.Max(pos.Height, 8),
                format,
                SwapChainFlags.None);

            if (result.Failure)
            {
                return;
            }

            using var backBuffer = GetBuffer();
            using var dxgiSurface = backBuffer.QueryInterface<IDXGISurface>();

            var props = new BitmapProperties1(
                new PixelFormat(format, Vortice.DCommon.AlphaMode.Ignore),
                96, 96,
                BitmapOptions.Target | BitmapOptions.CannotDraw);

            _d2dTargetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(dxgiSurface, props);
            _d2dContext.Target = _d2dTargetBitmap;
            _brush = _d2dContext.CreateSolidColorBrush(new Color4(1, 1, 1, 1));

            _visual.SetOffsetX(pos.X);
            _visual.SetOffsetY(pos.Y);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
        }

        Render();
        _dcompDevice.Commit();
    }

    private void Render()
    {
        try
        {
            if (_isUnloading || _d2dContext == null || _swapChain == null || _brush == null ||
                pos.Width <= 0 || pos.Height <= 0)
                return;

            _d2dContext.BeginDraw();
            _d2dContext.Clear(new Color4(0, 0, 0, 1));

            float cellWidth = pos.Width * 0.125f; // 1/8
            float cellHeight = pos.Height * 0.125f;
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
                        (col + 1) * cellWidth,
                        (row + 1) * cellHeight);

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
