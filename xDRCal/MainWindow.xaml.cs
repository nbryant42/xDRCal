using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using WinRT.Interop;
using xDRCal.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace xDRCal;

public sealed partial class MainWindow : Window
{
    private AppWindow appWindow;

    public MainWindow()
    {
        this.InitializeComponent();

        var hwnd = WindowNative.GetWindowHandle(this);
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
            CalibrationView.HdrMode = true;
            SliderA.Maximum = 1023;
            SliderA.DisplayMode = SliderDisplayMode.Nits;
            SliderB.Maximum = 1023;
            SliderB.Value = 1023;
            SliderB.DisplayMode = SliderDisplayMode.Nits;
        }
        else
        {
            CalibrationView.HdrMode = false;
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
        var frac = SizeSlider.Value / 100.0;
        var windowWidth = (int)RootGrid.ActualWidth;
        var windowHeight = (int)RootGrid.ActualHeight;
        var availableWidth = (int)BoundingGrid.ActualWidth;
        var availableHeight = (int)BoundingGrid.ActualHeight;
        var shortEdge = Math.Min(availableWidth, availableHeight);
        var totalArea = windowWidth * windowHeight;
        var targetArea = totalArea * frac;
        int width, height;

        // Calculate the edge length if the display were square. Constrain to be a multiple of 8 to keep the pixel
        // dimensions of the grid stable during resize, and reduce flicker
        var square = (int)Math.Sqrt(targetArea) / 8 * 8;

        // Either the square fits inside the shorter edge, or we scale one dimension to match the desired area.
        if (square <= shortEdge)
        {
            width = height = square;
        }
        else if (availableWidth > availableHeight)
        {
            width = Math.Min((int)(targetArea / availableHeight), availableWidth) / 8 * 8;
            height = availableHeight;
        }
        else
        {
            width = windowWidth;
            height = Math.Min((int)(targetArea / availableWidth), availableHeight) / 8 * 8;
        }

        if (width > 0 && height > 0)
        {
            CalibrationView.Width = width;
            CalibrationView.Height = height;
        }
    }

    private void ValueSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        CalibrationView.LuminosityA = (short)SliderA.Value;
        CalibrationView.LuminosityB = (short)SliderB.Value;
    }
}
