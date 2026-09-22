using Rex.Core;

namespace Rex.Tests;

public sealed class GeometryTests
{
    [Fact]
    public void FittedContentRect_LetterboxesEitherOrientation()
    {
        var portrait = PatternGeometry.FittedContentRect(1000, 1000, 1080, 2400);
        Assert.Equal(275, portrait.X, 3);
        Assert.Equal(0, portrait.Y, 3);
        Assert.Equal(450, portrait.Width, 3);
        Assert.Equal(1000, portrait.Height, 3);

        var landscape = PatternGeometry.FittedContentRect(1600, 900, 1080, 2400);
        Assert.Equal(0, landscape.X, 3);
        Assert.Equal(90, landscape.Y, 3);
        Assert.Equal(1600, landscape.Width, 3);
        Assert.Equal(720, landscape.Height, 3);

        Assert.Equal(new RectD(0, 0, 500, 300), PatternGeometry.FittedContentRect(500, 300, 0, 0));
    }

    [Fact]
    public void FromUiHierarchy_UsesParentViewSixths()
    {
        const string xml = """
            <hierarchy rotation="0">
              <node class="android.widget.FrameLayout" bounds="[0,0][1080,2400]">
                <node class="com.android.internal.widget.LockPatternView" resource-id="com.android.systemui:id/lockPatternView" content-desc="Pattern area" bounds="[140,820][940,1620]" />
              </node>
            </hierarchy>
            """;

        var geometry = PatternGeometry.FromUiHierarchy(xml)!;

        Assert.Equal(PatternGeometry.SourceUiView, geometry.Source);
        Assert.Equal(1080, geometry.ScreenWidth);
        Assert.Equal(2400, geometry.ScreenHeight);
        Assert.Equal((140.0 + 800.0 / 6) / 1080, geometry.Bounds.Left, 6);
        Assert.Equal((820.0 + 800.0 / 6) / 2400, geometry.Bounds.Top, 6);
    }

    [Fact]
    public void FromUiHierarchy_PrefersNineExactCells()
    {
        var cells = string.Join("\n", Enumerable.Range(0, 9).Select(i =>
            $"<node class=\"android.view.View\" content-desc=\"Pattern cell {i + 1}\" bounds=\"[{270 + (i % 3) * 240},{970 + (i / 3) * 220}][{330 + (i % 3) * 240},{1030 + (i / 3) * 220}]\" />"));
        var xml = $"""
            <hierarchy rotation="0"><node class="android.widget.FrameLayout" bounds="[0,0][1080,2400]">
              <node class="com.android.internal.widget.LockPatternView" bounds="[140,820][940,1620]">{cells}</node>
            </node></hierarchy>
            """;

        var geometry = PatternGeometry.FromUiHierarchy(xml)!;

        Assert.Equal(PatternGeometry.SourceUiDots, geometry.Source);
        Assert.True(geometry.ExactDots);
        Assert.Equal(300.0 / 1080, geometry.Bounds.Left, 6);
        Assert.Equal(780.0 / 1080, geometry.Bounds.Right, 6);
        Assert.Null(PatternGeometry.FromUiHierarchy("<hierarchy><node class='android.widget.TextView' bounds='[0,0][1080,2400]'/></hierarchy>"));
        Assert.Null(PatternGeometry.FromUiHierarchy("<not xml"));
    }

    [Fact]
    public void Layout_MapsAndroidCoordinatesIntoTheClient()
    {
        var geometry = new PatternGeometryInfo(PatternGeometry.SourceUiView, 1080, 2400, false, new PatternBounds(0.25, 0.4, 0.75, 0.6));

        var layout = PatternGeometry.Layout(geometry, 540, 1200, 1080, 2400, allowEstimate: true);

        Assert.Equal(9, layout.Points.Count);
        Assert.Equal(0.25 * 540, layout.Points[0].X, 3);
        Assert.Equal(0.4 * 1200, layout.Points[0].Y, 3);
        Assert.Equal(0.75 * 540, layout.Points[8].X, 3);

        var estimated = PatternGeometry.Layout(null, 540, 1200, 1080, 2400, allowEstimate: true);
        Assert.Equal(PatternGeometry.SourceEstimated, estimated.Source);
        Assert.Equal(9, estimated.Points.Count);
        Assert.Empty(PatternGeometry.Layout(null, 540, 1200, 1080, 2400, allowEstimate: false).Points);
    }

    [Fact]
    public void Effective_PrefersExactDotsThenCalibrationThenView()
    {
        var dots = new PatternGeometryInfo(PatternGeometry.SourceUiDots, 1, 1, true, new PatternBounds(0, 0, 1, 1));
        var view = dots with { Source = PatternGeometry.SourceUiView, ExactDots = false };
        var calibration = PatternGeometry.FromCalibration(new PatternCalibration(0.2, 0.3, 0.8, 0.7))!;

        Assert.Same(dots, PatternGeometry.Effective(dots, calibration));
        Assert.Same(calibration, PatternGeometry.Effective(view, calibration));
        Assert.Same(view, PatternGeometry.Effective(view, null));
        Assert.Null(PatternGeometry.FromCalibration(new PatternCalibration(0.8, 0.2, 0.2, 0.8)));
    }

    [Fact]
    public void ClampCalibration_SlidesInsteadOfCollapsing()
    {
        var clamped = PatternGeometry.ClampCalibration(new PatternBounds(-0.1, 0.2, 1.2, 0.8));
        Assert.Equal(0, clamped.Left, 4);
        Assert.Equal(1, clamped.Right, 4);

        var tiny = PatternGeometry.ClampCalibration(new PatternBounds(0.5, 0.5, 0.51, 0.51));
        Assert.True(tiny.Right - tiny.Left >= PatternGeometry.MinimumCalibrationSpan - 1e-9);

        var moved = PatternGeometry.Adjust(new PatternBounds(0.2, 0.2, 0.6, 0.6), "move-right", 0.1, 0.1);
        Assert.Equal(0.3, moved.Left, 6);
        Assert.Equal(0.7, moved.Right, 6);
    }

    [Theory]
    [InlineData("mKeyguardShowing=true", KeyguardState.Locked)]
    [InlineData("deviceLocked: 1", KeyguardState.Locked)]
    [InlineData("mShowingLockscreen=false", KeyguardState.Unlocked)]
    [InlineData("unrelated", KeyguardState.Unknown)]
    public void ParseKeyguardState(string text, KeyguardState expected) => Assert.Equal(expected, PatternGeometry.ParseKeyguardState(text));

    [Fact]
    public void Zoom_ComputeKeepsAnchorFixedAndClampsToViewport()
    {
        var fit = ZoomMath.FitRect(1000, 1000, 0.45, 1);
        Assert.Equal(1000, fit.Height, 3);
        Assert.Equal(450, fit.Width, 3);

        var view = ZoomMath.Compute(1000, 1000, fit, 2.0, 500, 500, null);
        Assert.Equal(900, view.SurfaceWidth, 3);
        Assert.Equal(2000, view.SurfaceHeight, 3);
        Assert.Equal(50, view.OffsetX, 3);
        Assert.Equal(-500, view.OffsetY, 3);

        // The point under the anchor (centre) stays put when zooming again from the same anchor.
        var further = ZoomMath.Compute(1000, 1000, fit, 4.0, 500, 500, view);
        var before = (500 - view.OffsetY) / view.SurfaceHeight;
        var after = (500 - further.OffsetY) / further.SurfaceHeight;
        Assert.Equal(before, after, 3);

        var panned = ZoomMath.Pan(1000, 1000, view, 5000, 5000);
        Assert.Equal(50, panned.OffsetX, 3);
        Assert.Equal(0, panned.OffsetY, 3);
        var panned2 = ZoomMath.Pan(1000, 1000, view, -5000, -5000);
        Assert.Equal(-1000, panned2.OffsetY, 3);
    }

    [Fact]
    public void Zoom_VisibleFractionAndCenterOnAgree()
    {
        var fit = ZoomMath.FitRect(1000, 1000, 1, 1);
        var view = ZoomMath.Compute(1000, 1000, fit, 4.0, 0, 0, null);

        var centered = ZoomMath.CenterOn(1000, 1000, view, 0.5, 0.5);
        var visible = ZoomMath.VisibleFraction(1000, 1000, centered);

        Assert.Equal(0.375, visible.X, 3);
        Assert.Equal(0.25, visible.Width, 3);
        Assert.Equal(new RectD(0, 0, 1, 1), ZoomMath.VisibleFraction(10, 10, ZoomView.Identity));
    }

    [Fact]
    public void Zoom_ClampAndSensitivity()
    {
        Assert.Equal(1.0, ZoomMath.ClampZoom(0.5, 4));
        Assert.Equal(1.0, ZoomMath.ClampZoom(1.01, 4));
        Assert.Equal(4.0, ZoomMath.ClampZoom(9, 4));
        Assert.Equal(1 / ZoomMath.SensitivityAdjustedScale(2, 0.7), ZoomMath.SensitivityAdjustedScale(0.5, 0.7), 9);
        Assert.Throws<ArgumentOutOfRangeException>(() => ZoomMath.SensitivityAdjustedScale(0, 1));
    }
}
