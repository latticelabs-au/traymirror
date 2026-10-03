using TrayMirror.Core.Geometry;
using TrayMirror.Core.Monitors;
using Xunit;

namespace TrayMirror.Core.Tests;

public class MonitorTargetsTests
{
    private static readonly MonitorInfo _primary = new(
        @"\\.\DISPLAY2",
        1,
        ReferenceGeometry.PrimaryMonitor,
        new PixelRect(0, 0, 3440, 1392),
        IsPrimary: true,
        DpiScale.Identity);

    private static readonly MonitorInfo _secondary = new(
        @"\\.\DISPLAY1",
        2,
        ReferenceGeometry.SecondaryMonitor,
        new PixelRect(3440, 0, 6000, 1392),
        IsPrimary: false,
        DpiScale.Identity);

    private static readonly MonitorInfo _third = new(
        @"\\.\DISPLAY3",
        3,
        new PixelRect(6000, 0, 7920, 1080),
        new PixelRect(6000, 0, 7920, 1032),
        IsPrimary: false,
        DpiScale.Identity);

    [Fact]
    public void Select_ReturnsEverySecondaryMonitor_WhenTheAllowListIsEmpty()
    {
        MonitorSelection selection = MonitorTargets.Select([_primary, _secondary, _third], []);

        Assert.Equal([_secondary, _third], selection.Targets);
        Assert.Empty(selection.UnmatchedNames);
    }

    [Fact]
    public void Select_NeverTargetsThePrimaryMonitor()
    {
        // The primary is the monitor being copied from. Mirroring onto it would draw a live copy
        // of a strip that is already there.
        MonitorSelection selection = MonitorTargets.Select([_primary, _secondary], [@"\\.\DISPLAY2"]);

        Assert.Empty(selection.Targets);
        Assert.Equal([@"\\.\DISPLAY2"], selection.UnmatchedNames);
    }

    [Fact]
    public void Select_HonoursTheAllowList()
    {
        MonitorSelection selection = MonitorTargets.Select([_primary, _secondary, _third], [@"\\.\DISPLAY3"]);

        Assert.Equal([_third], selection.Targets);
    }

    [Fact]
    public void Select_MatchesDeviceNamesCaseInsensitively()
    {
        MonitorSelection selection = MonitorTargets.Select([_primary, _secondary], [@"\\.\display1"]);

        Assert.Equal([_secondary], selection.Targets);
    }

    [Fact]
    public void Select_ReportsNamesThatMatchedNothing()
    {
        // Silently dropping a typo produces an application that starts, logs nothing and mirrors
        // nowhere, which is the hardest kind of bug to report.
        MonitorSelection selection = MonitorTargets.Select([_primary, _secondary], [@"\\.\DISPLAY9"]);

        Assert.Empty(selection.Targets);
        Assert.Equal([@"\\.\DISPLAY9"], selection.UnmatchedNames);
    }

    [Fact]
    public void Select_DoesNotDuplicateAMonitorListedTwice()
    {
        MonitorSelection selection = MonitorTargets.Select(
            [_primary, _secondary],
            [@"\\.\DISPLAY1", @"\\.\DISPLAY1"]);

        Assert.Single(selection.Targets);
    }

    [Fact]
    public void Select_ReturnsNothing_WhenOnlyThePrimaryIsConnected()
    {
        MonitorSelection selection = MonitorTargets.Select([_primary], []);

        Assert.Empty(selection.Targets);
    }

    [Fact]
    public void BottomAppBarHeight_IsTheGapBetweenTheMonitorAndItsWorkingArea()
    {
        Assert.Equal(48, _secondary.BottomAppBarHeight);
    }
}
