using TrayMirror.Core.Geometry;

namespace TrayMirror.Core.Input;

/// <summary>
/// Maps a click on a mirror window back to the point on the primary taskbar that owns it.
/// </summary>
/// <remarks>
/// <para>
/// A DWM thumbnail is a visual surface. It composites pixels and it does not hit test, so a click
/// landing on a mirror reaches traymirror's own window and stops there. The only way to make a
/// mirrored icon respond is to work out which primary-screen point the click corresponds to and
/// synthesise the click there.
/// </para>
/// <para>
/// That translation is arithmetic. It is not a UI Automation query and it is not a DWM query,
/// which is why it lives here in <c>TrayMirror.Core</c> with tests rather than in the interop
/// layer. In the common case, where source and destination are the same height, it reduces to the
/// documented <c>stripLeft + dx</c>. The ratio terms only do work when the two monitors scale
/// differently and the mirror was therefore resized.
/// </para>
/// </remarks>
public static class HitTestTranslator
{
    /// <summary>
    /// Translates a point expressed in mirror-window client coordinates to a screen point on the
    /// primary taskbar.
    /// </summary>
    /// <param name="strip">The source strip the mirror is showing.</param>
    /// <param name="mirrorBounds">The mirror window rectangle in screen coordinates.</param>
    /// <param name="dx">The click x offset from the mirror's left edge, in physical pixels.</param>
    /// <param name="dy">The click y offset from the mirror's top edge, in physical pixels.</param>
    /// <returns>The corresponding screen point on the primary taskbar.</returns>
    /// <exception cref="ArgumentException">The mirror rectangle encloses no pixels.</exception>
    public static (int X, int Y) ToPrimaryScreen(TrayStrip strip, PixelRect mirrorBounds, int dx, int dy)
    {
        if (mirrorBounds.IsEmpty)
        {
            throw new ArgumentException(
                "The mirror rectangle is empty, so no click can land in it.",
                nameof(mirrorBounds));
        }

        int x = strip.ScreenBounds.Left + Rescale(dx, mirrorBounds.Width, strip.Width);
        int y = strip.ScreenBounds.Top + Rescale(dy, mirrorBounds.Height, strip.Height);

        // Clamp to the strip. A click on the mirror's last pixel column would otherwise map one
        // pixel past the clock boundary and open the calendar instead of the icon that was clicked.
        return (
            Math.Clamp(x, strip.ScreenBounds.Left, strip.ScreenBounds.Right - 1),
            Math.Clamp(y, strip.ScreenBounds.Top, strip.ScreenBounds.Bottom - 1));
    }

    /// <summary>
    /// Translates a screen point on the primary taskbar into the matching screen point on a
    /// mirror.
    /// </summary>
    /// <param name="strip">The source strip the mirror is showing.</param>
    /// <param name="mirrorBounds">The mirror window rectangle in screen coordinates.</param>
    /// <param name="primaryX">The x coordinate on the primary screen.</param>
    /// <param name="primaryY">The y coordinate on the primary screen.</param>
    /// <returns>The corresponding screen point inside the mirror.</returns>
    /// <remarks>
    /// This is the inverse used by menu relocation: a menu that Windows opened at a point on the
    /// primary taskbar has to be moved to the equivalent point beneath the mirrored icon.
    /// </remarks>
    public static (int X, int Y) ToMirrorScreen(TrayStrip strip, PixelRect mirrorBounds, int primaryX, int primaryY)
    {
        if (mirrorBounds.IsEmpty)
        {
            throw new ArgumentException(
                "The mirror rectangle is empty, so nothing can be mapped into it.",
                nameof(mirrorBounds));
        }

        int x = mirrorBounds.Left + Rescale(primaryX - strip.ScreenBounds.Left, strip.Width, mirrorBounds.Width);
        int y = mirrorBounds.Top + Rescale(primaryY - strip.ScreenBounds.Top, strip.Height, mirrorBounds.Height);

        return (
            Math.Clamp(x, mirrorBounds.Left, mirrorBounds.Right - 1),
            Math.Clamp(y, mirrorBounds.Top, mirrorBounds.Bottom - 1));
    }

    private static int Rescale(int value, int fromExtent, int toExtent)
    {
        if (fromExtent <= 0 || fromExtent == toExtent)
        {
            return value;
        }

        return (int)Math.Round((double)value * toExtent / fromExtent, MidpointRounding.AwayFromZero);
    }
}
