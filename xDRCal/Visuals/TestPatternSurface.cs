using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
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
        Pages = [DrawChessboard, DrawGammaRamp, DrawBandingTest];
    }

    public int Page { get; set; }
    public List<Action<int, int, float>> Pages { get; }

    public override void Render()
    {
        try
        {
            var host = (CalibrationDisplay)this.host;
            uint dpi = PInvoke.GetDpiForWindow((HWND)host.Hwnd);

            if (host.IsUnloading || _d2dContext == null || _swapChain == null || _brush == null ||
                pos.Width <= 0 || pos.Height <= 0 || dpi <= 0 || host.EOTFComboBox == null)
                return;

            float scale = dpi / 96.0f;

            _d2dContext.Target = _d2dTargetBitmap;
            _d2dContext.BeginDraw();

            var grey = 23.0f / 255.0f; // = #171717 to match our XAML background
            float lumaA, lumaB;
            var eotf = (EOTF)((ComboBoxItem)host.EOTFComboBox.SelectedItem).Tag;

            if (HdrMode)
            {
                lumaA = eotf.ToScRGB(host.LuminosityA);
                lumaB = eotf.ToScRGB(host.LuminosityB);
                grey = EOTF.sRGB.ToScRGB(grey * 255.0f);
            }
            else
            {
                lumaA = host.LuminosityA / 255.0f;
                lumaB = host.LuminosityB / 255.0f;
            }

            _d2dContext.Clear(new Color4(grey, grey, grey));
            _d2dContext.Transform = Matrix3x2.CreateTranslation(border, border);

            Pages[Page].Invoke(pos.Width - border * 2, pos.Height - border * 2, scale);

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

    private void DrawChessboard(int width, int height, float scale)
    {
        var host = (CalibrationDisplay)this.host;

        if (_d2dContext == null || _brush == null || host.EOTFComboBox == null)
        {
            throw new InvalidOperationException("missing reference");
        }

        float lumaA, lumaB;
        var eotf = (EOTF)((ComboBoxItem)host.EOTFComboBox.SelectedItem).Tag;

        if (HdrMode)
        {
            lumaA = eotf.ToScRGB(host.LuminosityA);
            lumaB = eotf.ToScRGB(host.LuminosityB);
        }
        else
        {
            lumaA = host.LuminosityA / 255.0f;
            lumaB = host.LuminosityB / 255.0f;
        }

        float cellWidth = width * 0.125f; // 1/8
        float cellHeight = height * 0.125f;

        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                bool isA = (row + col) % 2 == 0;
                float luma = isA ? lumaA : lumaB;
                _brush.Color = new Color4(luma, luma, luma);

                // The + 1's are a little bit of overlap to account for rounding error; we don't constrain the grid
                // size to be an even multiple of 8.
                var rect = new Rect(col * cellWidth, row * cellHeight, cellWidth + 1, cellHeight + 1);

                _d2dContext.FillRectangle(rect, _brush);
            }
        }
    }

    private void DrawGammaRamp(int width, int height, float scale)
    {
        var host = (CalibrationDisplay)this.host;

        if (_d2dContext == null || _brush == null)
        {
            throw new InvalidOperationException("missing reference");
        }

        GammaSetup(scale, host, out var caption, out var gammaFunc, out var labelFunc,
            out var textFormat, out var textBrush);

        // Draw the ramp
        float cellWidth = width * 0.0625f; // 1/16
        for (int col = 0; col < 16; col++)
        {
            float point = host.LuminosityA + (host.LuminosityB - host.LuminosityA) * col / 15.0f;
            float luma = gammaFunc(point);
            _brush.Color = new Color4(luma, luma, luma);

            // The + 1 is a little bit of overlap to account for rounding error; we don't constrain the image
            // width to be an even multiple of 16.
            var rect = new Rect(col * cellWidth, 0, cellWidth + 1, height);

            _d2dContext.FillRectangle(rect, _brush);

            // label each bar with a SDR color code or HDR nits value
            DrawTextBottom(labelFunc(point), textFormat, textBrush, rect);
        }

        // main gamma curve caption
        DrawTextTop(caption, textFormat, textBrush, new Rect(0, 0, width, height));
    }

    private void DrawBandingTest(int width, int height, float scale)
    {
        var host = (CalibrationDisplay)this.host;

        if (_d2dContext == null || _brush == null)
        {
            throw new InvalidOperationException("missing reference");
        }

        GammaSetup(scale, host, out var caption, out var gammaFunc, out var labelFunc,
            out var textFormat, out var textBrush);

        // D2D gradient fills don't support HDR, so roll our own.
        for (int col = 0; col < width; col++)
        {
            float point = host.LuminosityA + (host.LuminosityB - host.LuminosityA) * col / (width - 1);
            float luma = gammaFunc(point);
            _brush.Color = new Color4(luma, luma, luma);

            // precise device-dependent single-pixel width
            var rect = new Rect(col, 0, 1, height);

            _d2dContext.FillRectangle(rect, _brush);
        }

        // main gamma curve caption
        DrawTextTop(caption, textFormat, textBrush, new Rect(0, 0, width, height));
    }

    // common setup shared by both DrawGammaRamp and DrawBandingTest
    private void GammaSetup(float scale, CalibrationDisplay host, out string caption, out Func<float, float> gammaFunc,
        out Func<float, string> labelFunc, out IDWriteTextFormat textFormat, out ID2D1SolidColorBrush textBrush)
    {
        if (host.DwriteFactory == null || _d2dContext == null || host.EOTFComboBox == null)
        {
            throw new InvalidOperationException("missing reference");
        }

        var desktopIsHDR = Util.IsHdrEnabled(host.Hwnd).GetValueOrDefault(HdrMode);
        if (HdrMode)
        {
            // `enc` is the raw slider value selected by user, [0..1023]
            // use the floating point representation and don't round here; give 12-bit monitors a chance to shine in
            // `DrawBandingTest`
            var eotf = (EOTF)((ComboBoxItem)host.EOTFComboBox.SelectedItem).Tag;
            gammaFunc = eotf.ToScRGB;
            labelFunc = enc => $"{eotf.ToNits(enc):G4}";
            var name = ((ComboBoxItem)host.EOTFComboBox.SelectedItem).Content;
            caption = desktopIsHDR ? $"{name} EOTF" : $"{name} EOTF (mapped onto monitor native gamma)";
        }
        else
        {
            // `enc` is [0..255]
            gammaFunc = enc => enc / 255.0f;
            labelFunc = enc => $"{(int)enc:X2}";
            caption = desktopIsHDR ? "sRGB gamma" : "Monitor native gamma";
        }
        textFormat = host.DwriteFactory.CreateTextFormat(
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
        textBrush = _d2dContext.CreateSolidColorBrush(new Color4(1, 1, 1, 0.70f));
        // white, semi-transparent
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

        _brush.Color = new Color4(0, 0, 0, 1.0f);
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

        _brush.Color = new Color4(0, 0, 0, 1.0f);
        _d2dContext.FillRectangle(textRect, _brush);
        _d2dContext.DrawText(text, textFormat, textRect, textBrush);
    }
}