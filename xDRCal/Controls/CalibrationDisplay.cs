using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SharpGen.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Windows.UI;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using xDRCal.Visuals;

namespace xDRCal.Controls;

public sealed partial class CalibrationDisplay : Panel, IDisposable, ISurfaceHost
{
    // general naming convention is _ prefix on IDisposable's
    private ID3D11Device? _d3dDevice;
    private IDXGIFactory2? _dxgiFactory;
    private IDXGISwapChain1? _swapChain;

    private ID2D1Factory1? _d2dFactory;
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _d2dContext;
    private ID2D1SolidColorBrush? _brush;

    private IDCompositionDesktopDevice? _dcompDevice;
    private IDCompositionVisual2? _rootVisual;
    private TestPatternSurface? _testPatternSurface;
    private NavBtnSurface? _leftBtnSurface, _rightBtnSurface;
    private List<Surface> _surfaces = [];
    private DispatcherQueueTimer? _renderTimer;
    private IDWriteFactory? _dwriteFactory;

    public bool IsUnloading { get; set; }

    private nint prevWndProc;
    private WndProcDelegate? wndProcDelegate;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private short luminosityA;
    private short luminosityB;

    public IntPtr Hwnd { get; private set; }

    public CalibrationDisplay()
    {
        Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        IsHitTestVisible = true;
    }

    /// <summary>
    /// (1) Subclass the parent window's WndProc in order to intercept WM_SETCURSOR, which is sent when the
    /// mouse is over one of our DComp overlays. If we don't do this, the system chooses the wrong pointer.
    /// (2) Setup DirectX etc.
    /// MainWindow is responsible for calling this on initialization.
    /// </summary>
    /// <param name="parentHwnd">Native Win32 HWND for the root app window.</param>
    public void Initialize(IntPtr parentHwnd)
    {
        Hwnd = FindXamlChild((HWND)parentHwnd);
        wndProcDelegate = CustomWndProc;
        prevWndProc = PInvoke.SetWindowLongPtr((HWND)Hwnd, WINDOW_LONG_PTR_INDEX.GWL_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(wndProcDelegate));

        InitializeDirectX();
    }

    private HWND FindXamlChild(HWND parentHwnd)
    {
        List<HWND> result = [];

        PInvoke.EnumChildWindows(parentHwnd, (hWnd, _) =>
        {
            Span<char> buf = stackalloc char[44];
            PInvoke.GetClassName(hWnd, buf);
            if (buf.ToString() == "Microsoft.UI.Content.DesktopChildSiteBridge\0")
            {
                result.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);

        return result.FirstOrDefault();
    }

    private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        const ushort HTCLIENT = 1;
        const uint WM_SETCURSOR = 0x0020;
        const uint WM_LBUTTONUP = 0x0202;

        var IDC_ARROW = MAKEINTRESOURCE(32512);

        switch (msg)
        {
            case WM_SETCURSOR:
                if (wParam == Hwnd && LOWORD(lParam) == HTCLIENT)
                {
                    // wParam != hwnd (parent) --> XAML child. So it's the DComp overlay.
                    var hCursor = LoadCursor(default, IDC_ARROW);
                    PInvoke.SetCursor((HCURSOR)hCursor);
                    return 1;
                }
                break;

            case WM_LBUTTONUP:
                int x = LOWORD(lParam);
                int y = HIWORD(lParam);

                foreach (var surface in _surfaces)
                {
                    if (surface.HitTest(x, y))
                    {
                        surface.Clicked();
                    }
                }
                return 0;
        }

        // Call previous/original WndProc (possibly owned by WinUI) for everything else.
        return CallWindowProc(prevWndProc, Hwnd, msg, wParam, lParam);
    }

    private static ushort LOWORD(LPARAM dword) => (ushort)(dword & 0xffff);
    private static ushort HIWORD(LPARAM dword) => (ushort)((dword >> 16) & 0xffff);

    [LibraryImport("USER32.dll", EntryPoint = "LoadCursorW"),
        DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [SupportedOSPlatform("windows5.0")]
    private static partial IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

    [LibraryImport("USER32.dll", EntryPoint = "CallWindowProcW"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [SupportedOSPlatform("windows5.0")]
    private static partial IntPtr CallWindowProc(nint lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private static IntPtr MAKEINTRESOURCE(ushort i) => i;

    public void Dispose()
    {
        IsUnloading = true;

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
        _dcompDevice?.Dispose();
        _dcompDevice = null;
        _rootVisual?.Dispose();
        _rootVisual = null;
        _testPatternSurface?.Dispose();
        _testPatternSurface = null;
        _leftBtnSurface?.Dispose();
        _leftBtnSurface = null;
        _rightBtnSurface?.Dispose();
        _rightBtnSurface = null;
        _surfaces.Clear();
        _dxgiFactory?.Dispose();
        _dxgiFactory = null;

        // Restore original wndproc
        if (prevWndProc != IntPtr.Zero)
        {
            PInvoke.SetWindowLongPtr((HWND)Hwnd, WINDOW_LONG_PTR_INDEX.GWL_WNDPROC, prevWndProc);
        }
    }

    // Must call Render() at >=24Hz on any HDR surfaces, to prevent DComp from downgrading the output to SDR.
    // This also happens in SwapChainPanel; apparently a well-known quirk of Windows
    // (implemented for power conservation, etc)
    private void StartRenderLoop()
    {
        _renderTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _renderTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / 24.0);
        _renderTimer.Tick += (_, _) => _testPatternSurface?.Render();
        _renderTimer.Start();
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

    private static T DCompositionCreateDevice3<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors)] T>(IUnknown renderingDevice) where T : IDCompositionDevice2
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
    private static unsafe partial int DCompositionCreateDevice3_(void* _renderingDevice, void* _iid,
        void* _dcompositionDevice);

    private async void InitializeDirectX()
    {
        try
        {
            _d3dDevice = CreateD3DDevice();
            _dxgiFactory = GetFactory();
            _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded);
            using IDXGIDevice dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
            _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
            _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

            // dcomp setup
            _dcompDevice = DCompositionCreateDevice3<IDCompositionDesktopDevice>(dxgiDevice);
            var dcompTarget = _dcompDevice.CreateSurfaceFromHwnd(Hwnd, true);

            _rootVisual = _dcompDevice.CreateVisual();
            _testPatternSurface = new TestPatternSurface(_rootVisual, this);
            _leftBtnSurface = new NavBtnSurface(_rootVisual, this, false, _testPatternSurface);
            _rightBtnSurface = new NavBtnSurface(_rootVisual, this, true, _testPatternSurface);
            _surfaces = [_testPatternSurface, _leftBtnSurface, _rightBtnSurface];

            dcompTarget.SetRoot(_rootVisual);

            _dwriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            // fire-and-forget is fine here; no RenderLoop for these.
            _leftBtnSurface.ResizeRenderTarget(false);
            _rightBtnSurface.ResizeRenderTarget(false);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            await _testPatternSurface.ResizeRenderTarget(true);
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

    internal void FlipLeft()
    {
        if (_testPatternSurface != null)
            _testPatternSurface.Page = Math.Max(0, _testPatternSurface.Page - 1);
    }

    internal void FlipRight()
    {
        if (_testPatternSurface != null)
            _testPatternSurface.Page = Math.Min(_testPatternSurface.Pages.Count - 1, _testPatternSurface.Page + 1);
    }

    public IDCompositionDesktopDevice? DcompDevice { get => _dcompDevice; }

    public IDXGIFactory2? DxgiFactory { get => _dxgiFactory; }

    public ID2D1Factory1? D2dFactory { get => _d2dFactory; }

    public ID2D1DeviceContext? D2dContext { get => _d2dContext; }

    public ID3D11Device? D3dDevice { get => _d3dDevice; }

    public IDWriteFactory? DwriteFactory { get => _dwriteFactory; }

    public TestPatternSurface? OverlaySurface { get => _testPatternSurface; }

    public NavBtnSurface? LeftBtnSurface { get => _leftBtnSurface; }

    public NavBtnSurface? RightBtnSurface { get => _rightBtnSurface; }

    public short LuminosityA
    {
        get => luminosityA;
        set
        {
            luminosityA = value;
            _testPatternSurface?.Render();
        }
    }
    public short LuminosityB
    {
        get => luminosityB;
        set
        {
            luminosityB = value;
            _testPatternSurface?.Render();
        }
    }
}
