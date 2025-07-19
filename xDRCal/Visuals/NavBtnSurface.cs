using System;
using System.Diagnostics;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace xDRCal.Visuals;

public partial class NavBtnSurface : Surface
{
    private readonly bool isRight;

    public NavBtnSurface(IDCompositionVisual2? _parentVisual, ISurfaceHost host, bool isRight, Surface aboveSurface) :
        base(_parentVisual, host, true, aboveSurface)
    {
        this.isRight = isRight;
        HasAlpha = true;
    }
    
    public override void Render()
    {
        try
        {
            if (host.IsUnloading || _d2dContext == null || _swapChain == null || _brush == null ||
                pos.Width <= 0 || pos.Height <= 0)
                return;

            _d2dContext.BeginDraw();
            _d2dContext.Clear(new Color4(0, 0, 0, 0));

            DrawButton();

            _d2dContext.EndDraw();
            _swapChain.Present(1, PresentFlags.None);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
        }
    }

    public void DrawButton()
    {
        if (_d2dContext == null || _d2dFactory == null || _brush == null)
        {
            throw new InvalidOperationException("missing reference");
        }
        const float radius = 12.0f;
        const float cx = radius, cy = radius;
        using var circle = _d2dFactory.CreateEllipseGeometry(new Ellipse(new(radius, radius), radius, radius));

        // Chevron: three points (top, tip, bottom)
        var chevronLength = 8.0f; // overall height of the chevron
        var chevronWidth = 6.0f;  // horizontal "spread" of the chevron
        var chevronThickness = 2.5f; // thickness of the chevron stroke
        var dir = isRight ? -1.0f : 1.0f;
        var pt1 = new Vector2(cx + dir * chevronWidth / 2, cy - chevronLength / 2);
        var pt2 = new Vector2(cx - dir * chevronWidth / 2, cy);
        var pt3 = new Vector2(cx + dir * chevronWidth / 2, cy + chevronLength / 2);

        // Build the chevron as a stroked path
        using var chevronGeometry = _d2dFactory.CreatePathGeometry();
        using (var sink = chevronGeometry.Open())
        {
            sink.BeginFigure(pt1, FigureBegin.Hollow);
            sink.AddLine(pt2);
            sink.AddLine(pt3);
            sink.EndFigure(FigureEnd.Open);
            sink.Close();
        }

        using var chevronWidened = _d2dFactory.CreatePathGeometry();
        using (var sink = chevronWidened.Open())
        {
            using var strokeStyle = _d2dFactory.CreateStrokeStyle(new StrokeStyleProperties
            {
                StartCap = CapStyle.Round,
                EndCap = CapStyle.Round,
                LineJoin = LineJoin.Round
            });
            chevronGeometry.Widen(chevronThickness, strokeStyle, null, chevronGeometry.FlatteningTolerance, sink);
            sink.Close();
        }


        // Combine: circle minus chevron
        using var combined = _d2dFactory.CreatePathGeometry();
        using (var sink = combined.Open())
        {
            circle.CombineWithGeometry(chevronWidened, CombineMode.Exclude, sink);
            sink.Close();
        }

        // Fill the result (light grey, semi-transparent)
        var circleColor = new Color4(220f / 255f, 220f / 255f, 220f / 255f, 160f / 255f);
        _brush.Color = circleColor;
        _d2dContext.FillGeometry(combined, _brush);
    }

}
