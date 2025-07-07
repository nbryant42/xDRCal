using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace xDRCal.Controls
{
    public enum SliderDisplayMode
    {
        Hex,
        Percent,
        Nits
    }

    public sealed partial class SliderWithValueBox : UserControl
    {
        public SliderWithValueBox()
        {
            InitializeComponent();
            UpdateTextBox();
        }

        public static readonly DependencyProperty DisplayModeProperty =
            DependencyProperty.Register(
                nameof(DisplayMode),
                typeof(SliderDisplayMode),
                typeof(SliderWithValueBox),
                new PropertyMetadata(SliderDisplayMode.Hex, OnDisplayModeChanged));

        public SliderDisplayMode DisplayMode
        {
            get => (SliderDisplayMode)GetValue(DisplayModeProperty);
            set => SetValue(DisplayModeProperty, value);
        }

        private static void OnDisplayModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SliderWithValueBox control)
            {
                control.UpdateTextBox();
            }
        }

        public double Minimum
        {
            get => Slider.Minimum;
            set
            {
                Slider.Minimum = value;
                Slider.Value = Math.Max(Slider.Value, value);
            }
        }

        public double Maximum
        {
            get => Slider.Maximum;
            set
            {
                Slider.Maximum = value;
                Slider.Value = Math.Min(Slider.Value, value);
            }
        }

        public double Value
        {
            get => Slider.Value;
            set => Slider.Value = value;
        }

        public event RangeBaseValueChangedEventHandler? ValueChanged;

        private void Slider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            UpdateTextBox();
            ValueChanged?.Invoke(this, e);
        }

        private void UpdateTextBox()
        {
            int value = (int)Slider.Value;

            switch (DisplayMode)
            {
                case SliderDisplayMode.Hex:
                    ValueBox.Text = $"0x{value:X2}";
                    break;

                case SliderDisplayMode.Percent:
                    // Clamp to [0–100] and convert to percent
                    double percent = Math.Round(Slider.Value / Slider.Maximum * 100);
                    ValueBox.Text = $"{percent:0}%";
                    break;

                case SliderDisplayMode.Nits:
                    double nits = Util.PQCodeToNits(value);
                    ValueBox.Text = $"{nits:G4} nits";
                    break;
            }
        }
    }
}
