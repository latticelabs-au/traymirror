using TrayMirror.Core.Geometry;
using TrayMirror.Core.Input;
using Xunit;

namespace TrayMirror.Core.Tests;

public class HitTestTranslatorTests
{
    private static readonly PixelRect _referenceMirror = new(5716, 1392, 5900, 1440);

    [Theory]
    [InlineData(0, 3156)]
    [InlineData(16, 3172)]
    [InlineData(100, 3256)]
    [InlineData(183, 3339)]
    public void ToPrimaryScreen_IsStripLeftPlusOffset_WhenBothTaskbarsAreTheSameHeight(int dx, int expectedX)
    {
        (int x, _) = HitTestTranslator.ToPrimaryScreen(ReferenceGeometry.Strip, _referenceMirror, dx, 24);

        Assert.Equal(expectedX, x);
    }

    [Fact]
    public void ToPrimaryScreen_MapsTheVerticalOffsetOntoTheSourceTaskbar()
    {
        (_, int y) = HitTestTranslator.ToPrimaryScreen(ReferenceGeometry.Strip, _referenceMirror, 16, 24);

        Assert.Equal(1416, y);
    }

    [Fact]
    public void ToPrimaryScreen_ClampsInsideTheStrip()
    {
        // The mirror's last pixel column would otherwise map one pixel past the clock boundary,
        // which opens the calendar instead of the icon the user clicked.
        (int x, int y) = HitTestTranslator.ToPrimaryScreen(ReferenceGeometry.Strip, _referenceMirror, 10_000, 10_000);

        Assert.Equal(3339, x);
        Assert.Equal(1439, y);
    }

    [Fact]
    public void ToPrimaryScreen_ScalesTheOffset_WhenTheMirrorWasResizedForADifferentDpi()
    {
        // A 150% primary mirrored onto a 100% secondary: the mirror is two thirds the width of the
        // strip, so an offset halfway across the mirror is halfway across the strip, not the same
        // number of pixels into it.
        var strip = TrayGeometry.Resolve(3000, 3276, new PixelRect(0, 1368, 3440, 1440));
        var mirror = new PixelRect(5716, 1392, 5900, 1440);

        (int x, _) = HitTestTranslator.ToPrimaryScreen(strip, mirror, 92, 24);

        Assert.Equal(3138, x);
    }

    [Fact]
    public void ToMirrorScreen_InvertsToPrimaryScreen()
    {
        for (int dx = 0; dx < _referenceMirror.Width; dx += 7)
        {
            (int primaryX, int primaryY) = HitTestTranslator.ToPrimaryScreen(ReferenceGeometry.Strip, _referenceMirror, dx, 24);
            (int mirrorX, _) = HitTestTranslator.ToMirrorScreen(ReferenceGeometry.Strip, _referenceMirror, primaryX, primaryY);

            Assert.Equal(_referenceMirror.Left + dx, mirrorX);
        }
    }

    [Fact]
    public void ToMirrorScreen_ClampsInsideTheMirror()
    {
        (int x, int y) = HitTestTranslator.ToMirrorScreen(ReferenceGeometry.Strip, _referenceMirror, 0, 0);

        Assert.Equal(_referenceMirror.Left, x);
        Assert.Equal(_referenceMirror.Top, y);
    }

    [Fact]
    public void ToPrimaryScreen_Throws_WhenTheMirrorIsEmpty()
    {
        Assert.Throws<ArgumentException>(
            () => HitTestTranslator.ToPrimaryScreen(ReferenceGeometry.Strip, PixelRect.Empty, 0, 0));
    }

    [Fact]
    public void ToMirrorScreen_Throws_WhenTheMirrorIsEmpty()
    {
        Assert.Throws<ArgumentException>(
            () => HitTestTranslator.ToMirrorScreen(ReferenceGeometry.Strip, PixelRect.Empty, 0, 0));
    }
}
