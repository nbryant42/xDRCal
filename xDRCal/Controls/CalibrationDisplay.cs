using Microsoft.UI.Xaml.Controls;

namespace xDRCal.Controls
{
    public sealed partial class CalibrationDisplay : SwapChainPanel
    {
        public bool HdrMode;

        public CalibrationDisplay()
        {
            //this.InitializeComponent();
            // Custom DX init goes here (e.g., Vortice or SharpDX or D3D wrapper)
        }

        public short LuminosityA { get; internal set; }
        public short LuminosityB { get; internal set; }

        // Setup Direct3D device, swapchain, render loop, etc.
    }

}
