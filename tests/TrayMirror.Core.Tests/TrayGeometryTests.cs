using TrayMirror.Core.Geometry;
using Xunit;

namespace TrayMirror.Core.Tests;

public class TrayGeometryTests
{
    [Fact]
    public void Resolve_ProducesTheMeasuredStrip_OnTheReferenceMachine()
    {
        TrayStrip strip = ReferenceGeometry.Strip;

        Assert.Equal(new PixelRect(3156, 1392, 3340, 1440), strip.ScreenBounds);
        Assert.Equal(184, strip.Width);
        Assert.Equal(48, strip.Height);
    }

    [Fact]
    public void Resolve_RebasesTheSourceRectangle_OntoTheSourceWindowOrigin()
    {
        TrayStrip strip = ReferenceGeometry.Strip;

        // The reference primary taskbar starts at y=1392, so the crop must start at y=0.
        Assert.Equal(new PixelRect(3156, 0, 3340, 48), strip.SourceBounds);
    }

    [Fact]
    public void Resolve_RebasesHorizontallyToo_WhenThePrimaryTaskbarIsNotAtTheOrigin()
    {
        // This is the case that a single-monitor 100% machine never exercises: a primary taskbar
        // whose screen origin is not near zero. Without the rebase, rcSource would be handed
        // screen coordinates and DWM would crop from the wrong part of the source window.
        var taskbar = new PixelRect(3440, 1392, 6000, 1440);

        TrayStrip strip = TrayGeometry.Resolve(5200, 5400, taskbar);

        Assert.Equal(new PixelRect(5200, 1392, 5400, 1440), strip.ScreenBounds);
        Assert.Equal(new PixelRect(1760, 0, 1960, 48), strip.SourceBounds);
    }

    [Fact]
    public void ToSourceRelative_IsTheIdentity_WhenTheSourceWindowIsAtTheOrigin()
    {
        var screen = new PixelRect(10, 20, 30, 40);

        PixelRect rebased = TrayGeometry.ToSourceRelative(screen, new PixelRect(0, 0, 100, 100));

        Assert.Equal(screen, rebased);
    }

    [Theory]
    [InlineData(3340, 3340)]
    [InlineData(3400, 3340)]
    public void TryResolve_Fails_WhenTheClockIsNotRightOfTheNotificationArea(int notificationArea, int clock)
    {
        bool resolved = TrayGeometry.TryResolve(notificationArea, clock, ReferenceGeometry.PrimaryTaskbar, out TrayStrip strip);

        Assert.False(resolved);
        Assert.Equal(default, strip);
    }

    [Fact]
    public void TryResolve_Fails_WhenTheTaskbarRectangleIsEmpty()
    {
        bool resolved = TrayGeometry.TryResolve(3156, 3340, PixelRect.Empty, out _);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolve_ClampsToTheTaskbar_WhenAReadingLandsOutsideIt()
    {
        // A boundary read while the taskbar is animating can legitimately fall outside it. An
        // rcSource wider than the source window makes DWM stretch whatever it finds there.
        bool resolved = TrayGeometry.TryResolve(-500, 9000, ReferenceGeometry.PrimaryTaskbar, out TrayStrip strip);

        Assert.True(resolved);
        Assert.Equal(ReferenceGeometry.PrimaryTaskbar, strip.ScreenBounds);
    }

    [Fact]
    public void Resolve_Throws_WhenTheReadingsAreUnusable()
    {
        Assert.Throws<ArgumentException>(
            () => TrayGeometry.Resolve(3340, 3156, ReferenceGeometry.PrimaryTaskbar));
    }
}
