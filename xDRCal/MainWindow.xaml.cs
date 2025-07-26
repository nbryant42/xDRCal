using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using System.Linq;
using Windows.Win32;
using Windows.Win32.Foundation;
using WinRT.Interop;
using xDRCal.Controls;

namespace xDRCal;

public sealed partial class MainWindow : Window
{
    private readonly AppWindow appWindow;

    public MainWindow()
    {
        InitializeComponent();
        SliderA.EOTFComboBox = EOTFComboBox;
        SliderB.EOTFComboBox = EOTFComboBox;
        ComboBoxItem_PQ.Tag = EOTF.pq;
        ComboBoxItem_sRGB.Tag = EOTF.sRGB;
        ComboBoxItem_Gamma22.Tag = EOTF.gamma22;
        ComboBoxItem_Gamma24.Tag = EOTF.gamma24;

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
        UpdateHDRSettings(null);
    }

    private void EOTF_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateHDRSettings((EOTF?)((ComboBoxItem?)e.RemovedItems.SingleOrDefault())?.Tag);
    }

    private void UpdateHDRSettings(EOTF? previousEOTF)
    {
        EOTFComboBox.IsEnabled = HdrToggle.IsOn;
        TestPattern.Eotf = (EOTF)((ComboBoxItem)EOTFComboBox.SelectedValue).Tag;

        if (HdrToggle.IsOn)
        {
            TestPattern.HdrMode = true;

            if (SliderA != null)
            {
                SliderA.Maximum = 1023;
                SliderA.DisplayMode = SliderDisplayMode.Nits;
                if (previousEOTF != null)
                    Recalc(SliderA, previousEOTF);
            }

            if (SliderB != null)
            {
                var eotf = (EOTF)((ComboBoxItem)EOTFComboBox.SelectedItem).Tag;
                SliderB.Maximum = 1023;
                SliderB.DisplayMode = SliderDisplayMode.Nits;
                if (previousEOTF == null) // EOTF did not change, so HDR mode was toggled on.
                    SliderB.Value = Util.GetPeakLuminanceCode(WindowNative.GetWindowHandle(this), eotf);
                else
                    Recalc(SliderB, previousEOTF);
            }
        }
        else
        {
            TestPattern.HdrMode = false;

            if (SliderA != null)
            {
                SliderA.Maximum = 255;
                SliderA.DisplayMode = SliderDisplayMode.Hex;
            }

            if (SliderB != null)
            {
                SliderB.Maximum = 255;
                SliderB.DisplayMode = SliderDisplayMode.Hex;
            }
        }
        TestPattern.Render();
    }

    private void Recalc(SliderWithValueBox slider, EOTF previousEOTF)
    {
        var eotf = (EOTF)((ComboBoxItem)EOTFComboBox.SelectedItem).Tag;

        slider.Value = MathF.Round(eotf.ToCode(previousEOTF.ToNits((float)slider.Value)));
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
        // These are device-independent pixels, so all calculations must be floating point.
        float availableWidth = (float)BoundingGrid.ActualWidth;
        float availableHeight = (float)BoundingGrid.ActualHeight;
        float targetArea = (float)(RootGrid.ActualWidth * RootGrid.ActualHeight * SizeSlider.Value / 100.0);
        float width, height;

        // Calculate the edge length if the display were square.
        float square = MathF.Sqrt(targetArea);

        // Either the square fits inside the shorter edge, or we scale one dimension to match the desired area.
        if (square <= Math.Min(availableWidth, availableHeight))
        {
            width = height = square;
        }
        else if (availableWidth > availableHeight)
        {
            width = Math.Min(targetArea / availableHeight, availableWidth);
            height = availableHeight;
        }
        else
        {
            width = (int)availableWidth;
            height = (int)Math.Min(targetArea / availableWidth, availableHeight);
        }

        if (width > 0 && height > 0)
        {
            TestPattern.Width = width;
            TestPattern.Height = height;
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
        TestPattern.LuminosityA = (short)SliderA.Value;
        TestPattern.LuminosityB = (short)SliderB.Value;
        TestPattern.Render();
    }

    private void LeftButton_Clicked(object sender, RoutedEventArgs e)
    {
        TestPattern.Page = Math.Max(TestPattern.Page - 1, 0);
        LeftButton.Visibility = TestPattern.Page == 0 ? Visibility.Collapsed : Visibility.Visible;
        RightButton.Visibility = TestPattern.Page == TestPattern.MaxPage ?
            Visibility.Collapsed : Visibility.Visible;
        TestPattern.Render();
    }

    private void RightButton_Clicked(object sender, RoutedEventArgs e)
    {
        TestPattern.Page = Math.Min(TestPattern.Page + 1, TestPattern.MaxPage);
        LeftButton.Visibility = TestPattern.Page == 0 ? Visibility.Collapsed : Visibility.Visible;
        RightButton.Visibility = TestPattern.Page == TestPattern.MaxPage ?
            Visibility.Collapsed : Visibility.Visible;
        TestPattern.Render();
    }
}
