using TrayMirror.Core.Geometry;
using Xunit;

namespace TrayMirror.Core.Tests;

public class DpiScaleTests
{
    [Theory]
    [InlineData(96, 1.0)]
    [InlineData(120, 1.25)]
    [InlineData(144, 1.5)]
    [InlineData(192, 2.0)]
    public void Scale_IsTheDpiOverNinetySix(int dpi, double expected)
    {
        var scale = new DpiScale(dpi, dpi);

        Assert.Equal(expected, scale.ScaleX);
        Assert.Equal(expected, scale.ScaleY);
    }

    [Fact]
    public void Identity_IsNinetySixOnBothAxes()
    {
        Assert.Equal(96, DpiScale.Identity.DpiX);
        Assert.Equal(96, DpiScale.Identity.DpiY);
    }

    [Fact]
    public void ToPhysical_AndToLogical_RoundTrip()
    {
        var scale = new DpiScale(144, 144);

        Assert.Equal(72, scale.ToPhysicalX(48));
        Assert.Equal(48, scale.ToLogicalX(72));
    }

    [Fact]
    public void RescaleX_ConvertsAMeasurementBetweenTwoMonitors()
    {
        var primary = new DpiScale(144, 144);
        var secondary = new DpiScale(96, 96);

        // A 276px strip measured on a 150% primary is 184px of a 100% secondary.
        Assert.Equal(184, primary.RescaleX(276, secondary));
        Assert.Equal(276, secondary.RescaleX(184, primary));
    }

    [Fact]
    public void RescaleY_ConvertsAMeasurementBetweenTwoMonitors()
    {
        var primary = new DpiScale(144, 144);
        var secondary = new DpiScale(96, 96);

        Assert.Equal(48, primary.RescaleY(72, secondary));
    }

    [Theory]
    [InlineData(0, 96)]
    [InlineData(96, 0)]
    [InlineData(-96, 96)]
    public void Constructor_Rejects_ADpiWindowsWouldNeverReport(int dpiX, int dpiY)
    {
        // A zero or negative DPI means an interop call failed and its result was used anyway.
        Assert.Throws<ArgumentOutOfRangeException>(() => new DpiScale(dpiX, dpiY));
    }
}
