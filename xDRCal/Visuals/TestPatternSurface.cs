using System;
using System.Diagnostics;
using System.Numerics;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using xDRCal.Controls;

namespace xDRCal.Visuals;

public partial class TestPatternSurface : Surface
{
    private short luminosityA, luminosityB;

    public TestPatternSurface(IDCompositionVisual2? _parentVisual, CalibrationDisplay host) :
        base(_parentVisual, host)
    {
    }

    public override void Render()
    {
        try
        {
            var host = (CalibrationDisplay)this.host;

            if (host.IsUnloading || _d2dContext == null || _swapChain == null || _brush == null ||
                pos.Width <= 0 || pos.Height <= 0)
                return;

            _d2dContext.BeginDraw();

            // leave a border to account for our minSize workaround
            var topBorder = Math.Max(0, (MIN_SIZE - pos.Width) / 2);
            var leftBorder = Math.Max(0, (MIN_SIZE - pos.Height) / 2);

            var grey = 23.0f / 255.0f; // = #171717 to match our XAML background
            float lumaA, lumaB;

            if (HdrMode)
            {
                lumaA = Util.PQCodeToNits(host.LuminosityA) * 0.0125f; // 1/80 nits
                lumaB = Util.PQCodeToNits(host.LuminosityB) * 0.0125f;
                grey = Util.SrgbToLinear(grey);
            }
            else
            {
                lumaA = host.LuminosityA / 255.0f;
                lumaB = host.LuminosityB / 255.0f;
            }

            _d2dContext.Clear(new Color4(grey, grey, grey));
            _d2dContext.Transform = Matrix3x2.CreateTranslation(leftBorder, topBorder);

            DrawChessboard(lumaA, lumaB);

            _d2dContext.EndDraw();
            _swapChain.Present(1, PresentFlags.None);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
        }
    }

    private void DrawChessboard(float lumaA, float lumaB)
    {
        if (_d2dContext == null || _brush == null)
        {
            throw new InvalidOperationException("missing reference");
        }

        float cellWidth = pos.Width * 0.125f; // 1/8
        float cellHeight = pos.Height * 0.125f;

        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                bool isA = (row + col) % 2 == 0;
                float luma = isA ? lumaA : lumaB;
                _brush.Color = new Color4(luma, luma, luma);

                var rect = new Rect(col * cellWidth, row * cellHeight, cellWidth + 1, cellHeight + 1);

                _d2dContext.FillRectangle(rect, _brush);
            }
        }
    }
}
