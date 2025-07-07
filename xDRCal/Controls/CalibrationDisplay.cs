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

namespace xDRCal.Controls
{
    public sealed partial class CalibrationDisplay : SwapChainPanel
    {
        private ID3D11Device? _d3dDevice;
        private IDXGISwapChain1? _swapChain;

        private ID2D1Factory1? _d2dFactory;
        private ID2D1Device? _d2dDevice;
        private ID2D1DeviceContext? _d2dContext;
        private ID2D1SolidColorBrush? _brush;

        private ID2D1Bitmap1? _d2dTargetBitmap;

        public short LuminosityA { get; set; }
        public short LuminosityB { get; set; }
        public bool HdrMode { get; set; }

        private DispatcherQueueTimer? _renderTimer;

        public CalibrationDisplay()
        {
            this.Loaded += OnLoaded;
            this.SizeChanged += OnSizeChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_swapChain == null && ActualWidth > 0 && ActualHeight > 0)
            {
                InitializeDirectX();
            }
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

        private void StartRenderLoop()
        {
            _renderTimer = DispatcherQueue.CreateTimer();
            _renderTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60 FPS
            _renderTimer.Tick += OnRender;
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


        private Vortice.WinUI.ISwapChainPanelNative CreatePanelNative()
        {
            using ComObject comObject = new ComObject(this);
            return comObject.QueryInterface<Vortice.WinUI.ISwapChainPanelNative>();
        }

        private void InitializeDirectX()
        {
            Vortice.Direct3D.FeatureLevel[] featureLevels = new[]
            {
                Vortice.Direct3D.FeatureLevel.Level_11_1,
                Vortice.Direct3D.FeatureLevel.Level_11_0,
                Vortice.Direct3D.FeatureLevel.Level_10_1
            };

            ID3D11DeviceContext _d3dContext;

            // D3D11 device
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                featureLevels, out _d3dDevice, out _d3dContext);

            // DXGI swapchain
            using var dxgiFactory = getFactory();

            var swapDesc = new SwapChainDescription1
            {
                Format = Format.R16G16B16A16_Float,
                Width = (uint)Math.Max(ActualWidth, 1),
                Height = (uint)Math.Max(ActualHeight, 1),
                BufferCount = 2,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                SwapEffect = SwapEffect.FlipSequential,
                Scaling = Scaling.Stretch,
                AlphaMode = Vortice.DXGI.AlphaMode.Ignore
            };

            var nativePanel = CreatePanelNative();
            var mediaFactory = dxgiFactory.QueryInterfaceOrNull<IDXGIFactoryMedia>();
            if (mediaFactory == null)
            {
                throw new InvalidOperationException("IDXGIFactoryMedia not supported.");
            }

            _swapChain = dxgiFactory.CreateSwapChainForComposition(_d3dDevice, swapDesc);

            nativePanel.SetSwapChain(_swapChain);

            _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
            using IDXGIDevice dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
            _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
            _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

            ResizeRenderTarget();
            StartRenderLoop();
        }

        private void ResizeRenderTarget()
        {
            if (_swapChain == null)
            {
                throw new InvalidOperationException("Missing swap chain");
            }

            _swapChain.ResizeBuffers(
                2,
                (uint)Math.Max(ActualWidth, 1),
                (uint)Math.Max(ActualHeight, 1),
                Format.R16G16B16A16_Float,
                SwapChainFlags.None);

            _swapChain.GetBuffer<ID3D11Texture2D>(0, out var backBuffer);
            using var dxgiSurface = backBuffer?.QueryInterface<IDXGISurface>();

            var props = new BitmapProperties1(
                new PixelFormat(Format.R16G16B16A16_Float, Vortice.DCommon.AlphaMode.Ignore),
                96, 96,
                BitmapOptions.Target | BitmapOptions.CannotDraw);

            if (_d2dContext == null)
            {
                throw new InvalidOperationException("Missing D2D context");
            }
            _d2dTargetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(dxgiSurface, props);
            _d2dContext.Target = _d2dTargetBitmap;
            _brush = _d2dContext.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        }

        private void OnRender(object sender, object e)
        {
            if (_d2dContext == null)
            {
                throw new InvalidOperationException("Missing D2D context");
            }

            if (_brush == null)
            {
                throw new InvalidOperationException("Missing Brush");
            }

            if (_swapChain == null)
            {
                throw new InvalidOperationException("Missing Swapchain");
            }

            _d2dContext.BeginDraw();
            _d2dContext.Clear(new Color4(0, 0, 0, 1));

            float width = (float)ActualWidth;
            float height = (float)ActualHeight;

            // ActualWidth and ActualHeight can be 0 early in the app lifecycle.
            if (width < 1 || height < 1)
                return; // Skip frame

            float cellWidth = width * 0.125f;
            float cellHeight = height * 0.125f;

            float nitsA = Util.PQCodeToNits(LuminosityA) * 0.0125f;
            float nitsB = Util.PQCodeToNits(LuminosityB) * 0.0125f;

            for (int row = 0; row < 8; row++)
            {
                for (int col = 0; col < 8; col++)
                {
                    bool isA = (row + col) % 2 == 0;
                    float luma = isA ? nitsA : nitsB;
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
    }
}
