using TrayMirror.Core.Geometry;
using Xunit;

namespace TrayMirror.Core.Tests;

public class PixelRectTests
{
    [Fact]
    public void WidthAndHeight_AreDerivedFromTheEdges()
    {
        var rect = new PixelRect(3156, 1392, 3340, 1440);

        Assert.Equal(184, rect.Width);
        Assert.Equal(48, rect.Height);
    }

    [Fact]
    public void FromSize_BuildsTheSameRectangleAsTheEdgeConstructor()
    {
        Assert.Equal(new PixelRect(3156, 1392, 3340, 1440), PixelRect.FromSize(3156, 1392, 184, 48));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, true)]
    [InlineData(0, 0, 10, 0, true)]
    [InlineData(10, 0, 0, 10, true)]
    [InlineData(0, 0, 1, 1, false)]
    public void IsEmpty_IsTrue_WhenNoPixelIsEnclosed(int left, int top, int right, int bottom, bool expected)
    {
        Assert.Equal(expected, new PixelRect(left, top, right, bottom).IsEmpty);
    }

    [Fact]
    public void Offset_TranslatesAllFourEdges()
    {
        var rect = new PixelRect(10, 20, 30, 40);

        Assert.Equal(new PixelRect(15, 25, 35, 45), rect.Offset(5, 5));
    }

    [Theory]
    [InlineData(10, 20, true)]
    [InlineData(29, 39, true)]
    [InlineData(30, 20, false)]
    [InlineData(9, 20, false)]
    public void Contains_TreatsTheRightAndBottomEdgesAsExclusive(int x, int y, bool expected)
    {
        Assert.Equal(expected, new PixelRect(10, 20, 30, 40).Contains(x, y));
    }

    [Fact]
    public void Intersect_ReturnsTheOverlap()
    {
        var a = new PixelRect(0, 0, 100, 100);
        var b = new PixelRect(50, 50, 150, 150);

        Assert.Equal(new PixelRect(50, 50, 100, 100), a.Intersect(b));
    }

    [Fact]
    public void Intersect_ReturnsEmpty_WhenTheRectanglesDoNotOverlap()
    {
        var a = new PixelRect(0, 0, 10, 10);
        var b = new PixelRect(20, 20, 30, 30);

        Assert.True(a.Intersect(b).IsEmpty);
    }

    [Fact]
    public void ToString_IsStableAcrossCultures()
    {
        // The log is read alongside numbers copied out of a design document, so the formatting is
        // pinned to the invariant culture rather than to whatever the machine is set to.
        Assert.Equal("(3156,1392)-(3340,1440) 184x48", new PixelRect(3156, 1392, 3340, 1440).ToString());
    }
}
