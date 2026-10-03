using TrayMirror.Core.Geometry;
using Xunit;

namespace TrayMirror.Core.Tests;

public class MirrorPlacementTests
{
    [Fact]
    public void Compute_ReproducesThePlacementValidatedOnTheReferenceMachine()
    {
        PixelRect placement = MirrorPlacement.Compute(
            ReferenceGeometry.Strip,
            ReferenceGeometry.SecondaryMonitor,
            ReferenceGeometry.SecondaryTaskbar,
            ReferenceGeometry.SecondaryClockLeft,
            paddingRight: 0);

        // The design document records a 184x48 window at (5716,1392), flush against the secondary
        // clock at 5900 with no visible seam.
        Assert.Equal(new PixelRect(5716, 1392, 5900, 1440), placement);
    }

    [Fact]
    public void Compute_MovesTheMirrorLeft_ByTheConfiguredPadding()
    {
        PixelRect placement = MirrorPlacement.Compute(
            ReferenceGeometry.Strip,
            ReferenceGeometry.SecondaryMonitor,
            ReferenceGeometry.SecondaryTaskbar,
            ReferenceGeometry.SecondaryClockLeft,
            paddingRight: 8);

        Assert.Equal(new PixelRect(5708, 1392, 5892, 1440), placement);
    }

    [Fact]
    public void Compute_AnchorsToTheMonitorEdge_WhenTheClockCannotBeLocated()
    {
        PixelRect placement = MirrorPlacement.Compute(
            ReferenceGeometry.Strip,
            ReferenceGeometry.SecondaryMonitor,
            ReferenceGeometry.SecondaryTaskbar,
            clockLeft: null,
            paddingRight: 0);

        // Visible and obviously misplaced beats invisible and unexplained.
        Assert.Equal(new PixelRect(5816, 1392, 6000, 1440), placement);
    }

    [Fact]
    public void Compute_FallsBackToTheBottomOfTheMonitor_WhenThereIsNoSecondaryTaskbar()
    {
        PixelRect placement = MirrorPlacement.Compute(
            ReferenceGeometry.Strip,
            ReferenceGeometry.SecondaryMonitor,
            PixelRect.Empty,
            clockLeft: null,
            paddingRight: 0);

        Assert.Equal(1392, placement.Top);
        Assert.Equal(1440, placement.Bottom);
        Assert.Equal(6000, placement.Right);
    }

    [Fact]
    public void Compute_ScalesTheWidth_WhenTheTwoTaskbarsAreDifferentHeights()
    {
        // Primary at 150% has a 72px bar; the secondary at 100% has 48. Copying the source width
        // across unchanged would leave the mirrored icons stretched.
        var strip = TrayGeometry.Resolve(3000, 3276, new PixelRect(0, 1368, 3440, 1440));

        PixelRect placement = MirrorPlacement.Compute(
            strip,
            ReferenceGeometry.SecondaryMonitor,
            ReferenceGeometry.SecondaryTaskbar,
            ReferenceGeometry.SecondaryClockLeft,
            paddingRight: 0);

        Assert.Equal(48, placement.Height);
        Assert.Equal(184, placement.Width);
    }

    [Fact]
    public void Compute_KeepsTheMirrorOnTheMonitor_WhenTheStripIsWiderThanTheSpaceAvailable()
    {
        var wide = TrayGeometry.Resolve(0, 3000, ReferenceGeometry.PrimaryTaskbar);

        PixelRect placement = MirrorPlacement.Compute(
            wide,
            ReferenceGeometry.SecondaryMonitor,
            ReferenceGeometry.SecondaryTaskbar,
            clockLeft: 3600,
            paddingRight: 0);

        Assert.Equal(3440, placement.Left);
        Assert.Equal(3600, placement.Right);
    }

    [Fact]
    public void Compute_ReturnsEmpty_WhenTheStripHasNoWidth()
    {
        PixelRect placement = MirrorPlacement.Compute(
            default,
            ReferenceGeometry.SecondaryMonitor,
            ReferenceGeometry.SecondaryTaskbar,
            ReferenceGeometry.SecondaryClockLeft,
            paddingRight: 0);

        Assert.True(placement.IsEmpty);
    }

    [Theory]
    [InlineData(184, 48, 48, 184)]
    [InlineData(184, 48, 72, 276)]
    [InlineData(184, 72, 48, 123)]
    [InlineData(100, 0, 48, 0)]
    public void ScaleWidth_PreservesTheAspectRatio(int sourceWidth, int sourceHeight, int destinationHeight, int expected)
    {
        Assert.Equal(expected, MirrorPlacement.ScaleWidth(sourceWidth, sourceHeight, destinationHeight));
    }
}
