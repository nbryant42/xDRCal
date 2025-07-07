using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using WinRT.Interop;
using xDRCal.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace xDRCal
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private AppWindow appWindow;

        public MainWindow()
        {
            this.InitializeComponent();

            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            appWindow = AppWindow.GetFromWindowId(windowId);
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
            var actualWidth = RootGrid.ActualWidth;
            var actualHeight = RootGrid.ActualHeight;
            var shortEdge = Math.Min(actualWidth, actualHeight);
            var frac = SizeSlider.Value / 100.0;
            var totalArea = actualWidth * actualHeight;
            var cutoff = (shortEdge * shortEdge) / totalArea;

            if (frac <= cutoff)
            {
                // Generate a square with area totalArea * frac
                var side = Math.Sqrt(totalArea * frac);
                CalibrationView.Width = side;
                CalibrationView.Height = side;
            }
            // otherwise, scale long edge only
            else if (actualWidth > actualHeight)
            {
                CalibrationView.Width = actualWidth * frac;
                CalibrationView.Height = actualHeight;
            }
            else
            {
                CalibrationView.Width = actualWidth;
                CalibrationView.Height = actualHeight * frac;
            }
        }

        private void ValueSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            CalibrationView.LuminosityA = (short)SliderA.Value;
            CalibrationView.LuminosityB = (short)SliderB.Value;
        }
    }
}
