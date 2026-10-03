namespace TrayMirror.Core.Geometry;

/// <summary>
/// Works out where a mirror window sits on a secondary monitor and how big it is.
/// </summary>
/// <remarks>
/// <para>
/// The mirror is right aligned so its right edge stops where that monitor's own clock begins.
/// Anchoring to the clock rather than to the monitor edge is what makes the mirrored strip look
/// like part of the native secondary taskbar instead of a floating overlay: the icons land exactly
/// where the eye already expects them, immediately left of the time.
/// </para>
/// <para>
/// Height comes from the destination taskbar, not from the source strip, and the width is then
/// scaled to preserve the source aspect ratio. A primary at 150% has a 72px taskbar while a
/// secondary at 100% has a 48px one, and copying the source height across would leave the mirror
/// overhanging the bar it is meant to sit in.
/// </para>
/// </remarks>
public static class MirrorPlacement
{
    /// <summary>
    /// Computes the mirror window rectangle, in screen coordinates.
    /// </summary>
    /// <param name="strip">The resolved source strip on the primary taskbar.</param>
    /// <param name="monitorBounds">The target monitor's full bounds.</param>
    /// <param name="taskbarBounds">
    /// The target monitor's <c>Shell_SecondaryTrayWnd</c> rectangle, or <see cref="PixelRect.Empty"/>
    /// when that monitor has no secondary taskbar.
    /// </param>
    /// <param name="clockLeft">
    /// Screen x of the left edge of the target monitor's clock, or <see langword="null"/> when it
    /// could not be located. Falling back to the monitor's right edge keeps the mirror visible
    /// rather than hiding it, which is the more debuggable failure.
    /// </param>
    /// <param name="paddingRight">
    /// Extra pixels of gap between the mirror and the clock, from the <c>stripPaddingRight</c>
    /// config key.
    /// </param>
    /// <returns>
    /// The window rectangle, or <see cref="PixelRect.Empty"/> when the strip is unusable.
    /// </returns>
    public static PixelRect Compute(
        TrayStrip strip,
        PixelRect monitorBounds,
        PixelRect taskbarBounds,
        int? clockLeft,
        int paddingRight)
    {
        if (strip.Width <= 0 || strip.Height <= 0 || monitorBounds.IsEmpty)
        {
            return PixelRect.Empty;
        }

        int height = taskbarBounds.IsEmpty ? strip.Height : taskbarBounds.Height;
        if (height <= 0)
        {
            return PixelRect.Empty;
        }

        int width = ScaleWidth(strip.Width, strip.Height, height);

        int top = taskbarBounds.IsEmpty ? monitorBounds.Bottom - height : taskbarBounds.Top;
        int right = (clockLeft ?? monitorBounds.Right) - paddingRight;

        // Keep the window inside the monitor even when the padding or the strip is wider than the
        // space left of the clock. A mirror clipped to the monitor edge is still readable; one
        // placed off screen is invisible with no error to explain it.
        right = Math.Min(right, monitorBounds.Right);
        int left = Math.Max(right - width, monitorBounds.Left);

        return left >= right ? PixelRect.Empty : new PixelRect(left, top, right, top + height);
    }

    /// <summary>
    /// Scales a source width to a destination height while preserving the aspect ratio.
    /// </summary>
    /// <param name="sourceWidth">The source strip width in physical pixels.</param>
    /// <param name="sourceHeight">The source strip height in physical pixels.</param>
    /// <param name="destinationHeight">The destination height in physical pixels.</param>
    /// <returns>The destination width in physical pixels.</returns>
    public static int ScaleWidth(int sourceWidth, int sourceHeight, int destinationHeight)
    {
        if (sourceHeight <= 0)
        {
            return 0;
        }

        return sourceHeight == destinationHeight
            ? sourceWidth
            : (int)Math.Round((double)sourceWidth * destinationHeight / sourceHeight, MidpointRounding.AwayFromZero);
    }
}
