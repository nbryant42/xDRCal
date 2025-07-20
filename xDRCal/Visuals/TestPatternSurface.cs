using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Win32;
using Windows.Win32.Foundation;
using xDRCal.Controls;

namespace xDRCal.Visuals;

public partial class TestPatternSurface : Surface
{
    public TestPatternSurface(IDCompositionVisual2? _parentVisual, CalibrationDisplay host) :
        base(_parentVisual, host)
    {
        Pages = [DrawChessboard, DrawGammaRamp];
    }

    public int Page { get; set; }
    public List<Action> Pages { get; }

    public override void Render()
    {
        try
        {
            var host = (CalibrationDisplay)this.host;

            if (host.IsUnloading || _d2dContext == null || _swapChain == null || _brush == null ||
                pos.Width <= 0 || pos.Height <= 0)
                return;

            _d2dContext.Target = _d2dTargetBitmap;
            _d2dContext.BeginDraw();

            // leave a border to account for our minSize workaround
            // TODO: this is currently broken. Fix.
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

            Pages[Page].Invoke();

            var hr = _d2dContext.EndDraw();

            if (hr.Failure)
            {
                throw new InvalidOperationException($"EndDraw: {hr}");
            }

            hr = _swapChain.Present(1, PresentFlags.None);

            if (hr.Failure)
            {
                throw new InvalidOperationException($"EndDraw: {hr}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
        }
    }

    private void DrawChessboard()
    {
        if (_d2dContext == null || _brush == null)
        {
            throw new InvalidOperationException("missing reference");
        }

        var host = (CalibrationDisplay)this.host;
        float lumaA, lumaB;

        if (HdrMode)
        {
            lumaA = Util.PQCodeToNits(host.LuminosityA) * 0.0125f; // 1/80 nits
            lumaB = Util.PQCodeToNits(host.LuminosityB) * 0.0125f;
        }
        else
        {
            lumaA = host.LuminosityA / 255.0f;
            lumaB = host.LuminosityB / 255.0f;
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

    private void DrawGammaRamp()
    {
        var host = (CalibrationDisplay)this.host;

        if (_d2dContext == null || _brush == null || host.DwriteFactory == null)
        {
            throw new InvalidOperationException("missing reference");
        }

        uint dpi = PInvoke.GetDpiForWindow((HWND)host.Hwnd);
        if (dpi <= 0)
        {
            return; // the app is shutting down.
        }

        // a little paranoia to ensure we don't draw text outside the surface
        _d2dContext.PushAxisAlignedClip(new RawRectF(0, 0, pos.Width, pos.Height), AntialiasMode.Aliased);
        try
        {
            var desktopIsHDR = Util.IsHdrEnabled(host.Hwnd).GetValueOrDefault(HdrMode);
            string caption;
            Func<float, float> gammaFunc;
            Func<float, string> labelFunc;

            if (HdrMode)
            {
                gammaFunc = (float enc) => Util.PQCodeToNits((int)MathF.Round(enc)) * 0.0125f; // 1/80 nits
                labelFunc = (float enc) => $"{Util.PQCodeToNits((int)MathF.Round(enc)):G4}";
                caption = desktopIsHDR ? "PQ EOTF" : "PQ EOTF (mapped onto monitor native gamma)";
            }
            else
            {
                gammaFunc = (float enc) => enc / 255.0f;
                labelFunc = (float enc) => $"{(int)enc:X2}";
                caption = desktopIsHDR ? "sRGB gamma" : "Monitor native gamma";
            }

            float cellWidth = pos.Width * 0.0625f; // 1/16
            float scale = dpi / 96.0f;

            using var textFormat = host.DwriteFactory.CreateTextFormat(
                "Segoe UI",  // font family
                null, // collection
                fontWeight: FontWeight.Normal,
                fontStyle: FontStyle.Normal,
                fontStretch: FontStretch.Normal,
                fontSize: 13.0f * scale,
                localeName: "en-us"
            );
            textFormat.TextAlignment = TextAlignment.Center;
            textFormat.ParagraphAlignment = ParagraphAlignment.Near;
            using var textBrush = _d2dContext.CreateSolidColorBrush(new Color4(1, 1, 1, 0.70f)); // white, semi-transparent

            // Draw the ramp
            for (int col = 0; col < 16; col++)
            {
                float point = host.LuminosityA + (host.LuminosityB - host.LuminosityA) * col / 15.0f;
                float luma = gammaFunc(point);
                _brush.Color = new Color4(luma, luma, luma);

                var rect = new Rect(col * cellWidth, 0, cellWidth + 1, pos.Height + 1);

                _d2dContext.FillRectangle(rect, _brush);

                // label each bar with a SDR color code or HDR nits value
                DrawTextBottom(labelFunc(point), textFormat, textBrush, rect);
            }

            // main gamma curve caption
            DrawTextTop(caption, textFormat, textBrush, new Rect(0, 0, pos.Width, pos.Height));
        }
        finally
        {
            _d2dContext.PopAxisAlignedClip();
        }
    }

    // draw text top-center within a bounding rect, and surrounded by a darker box for visibility
    private void DrawTextTop(string text, IDWriteTextFormat textFormat, ID2D1SolidColorBrush textBrush,
        Rect bounds)
    {
        if (_d2dContext == null || host.DwriteFactory == null || _brush == null)
        {
            throw new InvalidOperationException("missing reference");
        }

        using var textLayout = host.DwriteFactory.CreateTextLayout(text, textFormat, bounds.Width, bounds.Height);
        var textHeight = textLayout.Metrics.Height;
        var textWidth = textLayout.Metrics.Width;
        var offset = (bounds.Width - textWidth) / 2;
        var x = bounds.X + offset;
        var textRect = new Rect(x, 0, textWidth, textHeight);

        // must be very nearly opaque on top of possibly-very-bright HDR:
        _brush.Color = new Color4(0, 0, 0, 0.999f);
        _d2dContext.FillRectangle(textRect, _brush);
        _d2dContext.DrawText(text, textFormat, textRect, textBrush);
    }
    // draw text bottom-center within a bounding rect, and surrounded by a darker box for visibility
    private void DrawTextBottom(string text, IDWriteTextFormat textFormat, ID2D1SolidColorBrush textBrush,
        Rect bounds)
    {
        if (_d2dContext == null || host.DwriteFactory == null || _brush == null)
        {
            throw new InvalidOperationException("missing reference");
        }

        using var textLayout = host.DwriteFactory.CreateTextLayout(text, textFormat, bounds.Width, bounds.Height);
        var textHeight = textLayout.Metrics.Height;
        var textWidth = textLayout.Metrics.Width;
        var offset = (bounds.Width - textWidth) / 2;
        var x = bounds.X + offset;
        var textRect = new Rect(x, bounds.Height - textHeight, textWidth, textHeight);

        // must be very nearly opaque on top of possibly-very-bright HDR:
        _brush.Color = new Color4(0, 0, 0, 0.999f);
        _d2dContext.FillRectangle(textRect, _brush);
        _d2dContext.DrawText(text, textFormat, textRect, textBrush);
    }
}