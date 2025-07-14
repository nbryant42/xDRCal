using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using Vortice.Mathematics;
using Windows.Win32;
using Windows.Win32.Foundation;
using WinRT.Interop;
using xDRCal.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace xDRCal;

public sealed partial class MainWindow : Window
{
    private readonly AppWindow appWindow;
    private readonly CalibrationDisplay calibrationView;

    public MainWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        calibrationView = new CalibrationDisplay(hwnd);

        this.InitializeComponent();

        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        appWindow = AppWindow.GetFromWindowId(windowId);

        RootGrid.SizeChanged += (_, _) => UpdateCalibrationScale();
    }

    private void FullscreenAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        FullscreenToggle.IsOn = !FullscreenToggle.IsOn;
        args.Handled = true;
    }

    private void HdrAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        HdrToggle.IsOn = !HdrToggle.IsOn;
        args.Handled = true;
    }

    private void FullscreenToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (FullscreenToggle.IsOn)
            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        else
            appWindow.SetPresenter(AppWindowPresenterKind.Default);
    }

    private void HdrToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (HdrToggle.IsOn)
        {
            calibrationView.HdrMode = true;
            SliderA.Maximum = 1023;
            SliderA.DisplayMode = SliderDisplayMode.Nits;
            SliderB.Maximum = 1023;
            SliderB.Value = 1023;
            SliderB.DisplayMode = SliderDisplayMode.Nits;
        }
        else
        {
            calibrationView.HdrMode = false;
            SliderA.Maximum = 255;
            SliderA.DisplayMode = SliderDisplayMode.Hex;
            SliderB.Maximum = 255;
            SliderB.Value = 255;
            SliderB.DisplayMode = SliderDisplayMode.Hex;
        }
    }

    private void Window_SizeChanged(object sender, WindowSizeChangedEventArgs e)
    {
        UpdateCalibrationScale();
    }

    private void SizeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateCalibrationScale();
    }

    private void UpdateCalibrationScale()
    {
        // intermediate calculations in float32
        uint dpi = PInvoke.GetDpiForWindow((HWND)WindowNative.GetWindowHandle(this));
        float scale = dpi / 96.0f;

        float frac = (float)SizeSlider.Value / 100.0f;
        float windowWidth = Phys(scale, RootGrid.ActualWidth);
        float windowHeight = Phys(scale, RootGrid.ActualHeight);
        float availableWidth = Phys(scale, BoundingGrid.ActualWidth);
        float availableHeight = Phys(scale, BoundingGrid.ActualHeight);
        float shortEdge = Math.Min(availableWidth, availableHeight);
        float totalArea = windowWidth * windowHeight;
        float targetArea = totalArea * frac;

        // final outputs as integers
        int width, height;

        // Calculate the edge length if the display were square.
        int square = Round(MathF.Sqrt(targetArea));

        // Either the square fits inside the shorter edge, or we scale one dimension to match the desired area.
        if (square <= shortEdge)
        {
            width = height = square;
        }
        else if (availableWidth > availableHeight)
        {
            width = (int)Math.Min(Round(targetArea / availableHeight), availableWidth);
            height = (int)availableHeight;
        }
        else
        {
            width = (int)windowWidth;
            height = (int)Math.Min(Round(targetArea / availableWidth), availableHeight);
        }

        if (width > 0 && height > 0)
        {
            width = width / 2 * 2; // constrain to even width - keep center centered to avoid flicker
            height = height / 2 * 2;

            RectI pos = new(((int)availableWidth - width) / 2, ((int)availableHeight - height) / 2,
                width, height);

            calibrationView.Reposition(pos);
        }
    }

    private static int Round(float f)
    {
        return (int)MathF.Round(f);
    }

    private static float Phys(float scale, double dip)
    {
        return MathF.Round((float)dip * scale);
    }

    private void ValueSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        calibrationView.LuminosityA = (short)SliderA.Value;
        calibrationView.LuminosityB = (short)SliderB.Value;
    }
}
