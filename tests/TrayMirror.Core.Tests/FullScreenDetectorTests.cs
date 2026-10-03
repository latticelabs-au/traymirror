using TrayMirror.Core.Geometry;
using Xunit;

namespace TrayMirror.Core.Tests;

public class FullScreenDetectorTests
{
    // The machine traymirror was verified on: a 1920x1080 primary at the origin whose taskbar is
    // 48 physical pixels tall, so the working area stops at y=1032.
    private static readonly PixelRect _monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void CoversMonitor_IsTrue_ForAWindowFillingTheMonitorExactly()
    {
        Assert.True(FullScreenDetector.CoversMonitor(new PixelRect(0, 0, 1920, 1080), _monitor));
    }

    [Fact]
    public void CoversMonitor_IsFalse_ForAMaximisedWindow()
    {
        // The distinction the whole detector exists for. A maximised window stops at the working
        // area, so its bottom edge is the top of the taskbar rather than the bottom of the screen.
        Assert.False(FullScreenDetector.CoversMonitor(new PixelRect(-8, -8, 1928, 1040), _monitor));
    }

    [Fact]
    public void CoversMonitor_IsTrue_ForAWindowOverhangingTheMonitor()
    {
        // Borderless windows routinely extend a few pixels past the monitor. Covering more than
        // the whole monitor is still covering the whole monitor.
        Assert.True(FullScreenDetector.CoversMonitor(new PixelRect(-8, -8, 1928, 1088), _monitor));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void CoversMonitor_AllowsWindowsThatFallAFewPixelsShort(int shortfall, bool expected)
    {
        // Some games place themselves one pixel inside the monitor, so a small tolerance is
        // needed. It has to stay far below the 48 pixel taskbar height to keep maximised windows
        // out.
        var window = new PixelRect(shortfall, shortfall, 1920 - shortfall, 1080 - shortfall);

        Assert.Equal(expected, FullScreenDetector.CoversMonitor(window, _monitor));
    }

    [Fact]
    public void CoversMonitor_IsFalse_ForAWindowOnAnotherMonitor()
    {
        // A full-screen window on the secondary must not be reported as full screen on the
        // primary. This is what makes the rule per monitor rather than global.
        var secondary = new PixelRect(-1920, 1, 0, 1081);
        var windowOnSecondary = new PixelRect(-1920, 1, 0, 1081);

        Assert.True(FullScreenDetector.CoversMonitor(windowOnSecondary, secondary));
        Assert.False(FullScreenDetector.CoversMonitor(windowOnSecondary, _monitor));
    }

    [Fact]
    public void CoversMonitor_IsFalse_ForAnOrdinaryWindow()
    {
        Assert.False(FullScreenDetector.CoversMonitor(new PixelRect(200, 150, 1200, 800), _monitor));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(10, 10, 10, 10)]
    public void CoversMonitor_IsFalse_ForAnEmptyWindow(int left, int top, int right, int bottom)
    {
        Assert.False(FullScreenDetector.CoversMonitor(new PixelRect(left, top, right, bottom), _monitor));
    }

    [Fact]
    public void CoversMonitor_IsFalse_WhenTheMonitorIsEmpty()
    {
        Assert.False(FullScreenDetector.CoversMonitor(new PixelRect(0, 0, 1920, 1080), PixelRect.Empty));
    }

    [Fact]
    public void CoversMonitor_RejectsANegativeTolerance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FullScreenDetector.CoversMonitor(_monitor, _monitor, -1));
    }
}
