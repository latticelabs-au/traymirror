namespace TrayMirror.Core.Geometry;

/// <summary>
/// The resolved tray strip: the region of the primary taskbar that traymirror copies, expressed
/// both in screen coordinates and in the coordinate space DWM actually wants.
/// </summary>
/// <param name="ScreenBounds">
/// The strip in screen coordinates. Used to translate a click on a mirror back to a point on the
/// primary taskbar.
/// </param>
/// <param name="SourceBounds">
/// The same strip relative to the source window's own origin. This is the value that goes into
/// <c>DWM_THUMBNAIL_PROPERTIES.rcSource</c>, and it is not interchangeable with
/// <paramref name="ScreenBounds"/>.
/// </param>
public readonly record struct TrayStrip(PixelRect ScreenBounds, PixelRect SourceBounds)
{
    /// <summary>Gets the strip width in physical pixels on the primary monitor.</summary>
    public int Width => ScreenBounds.Width;

    /// <summary>Gets the strip height in physical pixels on the primary monitor.</summary>
    public int Height => ScreenBounds.Height;

    /// <summary>Gets the screen x coordinate of the strip's left edge.</summary>
    public int StripLeft => ScreenBounds.Left;
}
