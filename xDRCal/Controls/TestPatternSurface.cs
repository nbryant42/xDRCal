using System;
using System.Collections.Generic;
using System.Diagnostics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace xDRCal.Controls;

public partial class TestPatternSurface : Surface
{
    public TestPatternSurface()
    {
        Pages = [DrawChessboard, DrawGammaRamp, DrawBandingTest];
    }

    public int Page { get; set; }
    public int MaxPage { get => Pages.Count - 1; }

    public List<Action<int, int, float>> Pages { get; }
    public EOTF Eotf { get; set; } = EOTF.pq;
    public int Hwnd { get; set; }
    public short LuminosityA { get; set; }
    public short LuminosityB { get; set; } = 255;

    public override void Render()
    {
        try
        {
            // If there is no LayoutTransform or ViewBox, it should be safe to assume this is only being used for DPI,
            // so X=Y. We are not simply pushing a D2D transform here, because we want precise control over
            // device-dependent pixels in the banding test.
            var scale = CompositionScaleX;
            int width = (int)MathF.Round((float)ActualWidth * scale);
            int height = (int)MathF.Round((float)ActualHeight * scale);

            if (_d2dContext == null || _swapChain == null || _brush == null || width <= 0 || height <= 0)
                return;

            _d2dContext.Target = _d2dTargetBitmap;
            _d2dContext.BeginDraw();

            var grey = 23.0f / 255.0f; // = #171717 to match our XAML background
            float lumaA, lumaB;

            if (HdrMode)
            {
                lumaA = Eotf.ToScRGB(LuminosityA);
                lumaB = Eotf.ToScRGB(LuminosityB);
                grey = EOTF.sRGB.ToScRGB(grey * 255.0f);
            }
            else
            {
                lumaA = LuminosityA / 255.0f;
                lumaB = LuminosityB / 255.0f;
            }

            _d2dContext.Clear(new Color4(grey, grey, grey));

            Pages[Page].Invoke(width, height, scale);

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
        if (_d2dContext == null || _brush == null)
        {
            throw new InvalidOperationException("missing reference");
        }

        float lumaA, lumaB;

        if (HdrMode)
        {
            lumaA = Eotf.ToScRGB(LuminosityA);
            lumaB = Eotf.ToScRGB(LuminosityB);
        }
        else
        {
            lumaA = LuminosityA / 255.0f;
            lumaB = LuminosityB / 255.0f;
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
        if (_d2dContext == null || _brush == null)
        {
            throw new InvalidOperationException("missing reference");
        }

        GammaSetup(scale, out var caption, out var gammaFunc, out var labelFunc,
            out var textFormat, out var textBrush);
        try
        {
            // Draw the ramp
            float cellWidth = width * 0.0625f; // 1/16
            for (int col = 0; col < 16; col++)
            {
                float point = (float)(LuminosityA + (LuminosityB - LuminosityA) * col / 15.0f);
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
        finally
        {
            textFormat.Dispose();
            textBrush.Dispose();
        }
    }

    private void DrawBandingTest(int width, int height, float scale)
    {
        if (_d2dContext == null || _brush == null)
        {
            throw new InvalidOperationException("missing reference");
        }

        GammaSetup(scale, out var caption, out var gammaFunc, out var labelFunc,
            out var textFormat, out var textBrush);

        try
        {
            // D2D gradient fills don't support HDR, so roll our own.
            for (int col = 0; col < width; col++)
            {
                float point = (float)(LuminosityA + (LuminosityB - LuminosityA) * col / (width - 1));
                float luma = gammaFunc(point);
                _brush.Color = new Color4(luma, luma, luma);

                // precise device-dependent single-pixel width
                var rect = new Rect(col, 0, 1, height);

                _d2dContext.FillRectangle(rect, _brush);
            }

            // main gamma curve caption
            DrawTextTop(caption, textFormat, textBrush, new Rect(0, 0, width, height));
        }
        finally
        {
            textFormat.Dispose();
            textBrush.Dispose();
        }
    }

    // common setup shared by both DrawGammaRamp and DrawBandingTest
    private void GammaSetup(float scale, out string caption, out Func<float, float> gammaFunc,
        out Func<float, string> labelFunc, out IDWriteTextFormat textFormat, out ID2D1SolidColorBrush textBrush)
    {
        if (_dwriteFactory == null || _d2dContext == null)
        {
            throw new InvalidOperationException("missing reference");
        }

        var desktopIsHDR = Util.IsHdrEnabled(Hwnd).GetValueOrDefault(HdrMode);
        if (HdrMode)
        {
            // `enc` is the raw slider value selected by user, [0..1023]
            // use the floating point representation and don't round here; give 12-bit monitors a chance to shine in
            // `DrawBandingTest`
            gammaFunc = Eotf.ToScRGB;
            labelFunc = enc => $"{Eotf.ToNits(enc):G4}";
            caption = desktopIsHDR ? $"{Eotf.DisplayName} EOTF" : $"{Eotf.DisplayName} EOTF (mapped onto monitor native gamma)";
        }
        else
        {
            // `enc` is [0..255]
            gammaFunc = enc => enc / 255.0f;
            labelFunc = enc => $"{(int)enc:X2}";
            caption = desktopIsHDR ? "sRGB gamma" : "Monitor native gamma";
        }
        textFormat = _dwriteFactory.CreateTextFormat(
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
        textBrush = _d2dContext.CreateSolidColorBrush(new Color4(1, 1, 1));
    }

    // draw text top-center within a bounding rect, and surrounded by a darker box for visibility
    private void DrawTextTop(string text, IDWriteTextFormat textFormat, ID2D1SolidColorBrush textBrush,
        Rect bounds)
    {
        if (_d2dContext == null || _dwriteFactory == null || _brush == null)
        {
            throw new InvalidOperationException("missing reference");
        }

        using var textLayout = _dwriteFactory.CreateTextLayout(text, textFormat, bounds.Width, bounds.Height);
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
        if (_d2dContext == null || _dwriteFactory == null || _brush == null)
        {
            throw new InvalidOperationException("missing reference");
        }

        using var textLayout = _dwriteFactory.CreateTextLayout(text, textFormat, bounds.Width, bounds.Height);
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