namespace TrayMirror.Core.Geometry;

/// <summary>
/// A rectangle in physical pixels, expressed as edges rather than as an origin plus a size.
/// </summary>
/// <remarks>
/// This type deliberately mirrors the Win32 <c>RECT</c> layout (left, top, right, bottom) because
/// every value that flows through this library either came from a Win32 call or is on its way to
/// one. Keeping the same shape removes a class of conversion mistakes at the interop boundary.
/// It is a plain value type with no WPF dependency, so <c>TrayMirror.Core</c> stays testable
/// without a dispatcher or a display.
/// </remarks>
/// <param name="Left">The x coordinate of the left edge, inclusive.</param>
/// <param name="Top">The y coordinate of the top edge, inclusive.</param>
/// <param name="Right">The x coordinate of the right edge, exclusive.</param>
/// <param name="Bottom">The y coordinate of the bottom edge, exclusive.</param>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    /// <summary>Gets the width in physical pixels. Negative if the rectangle is inverted.</summary>
    public int Width => Right - Left;

    /// <summary>Gets the height in physical pixels. Negative if the rectangle is inverted.</summary>
    public int Height => Bottom - Top;

    /// <summary>
    /// Gets a value indicating whether the rectangle encloses at least one pixel.
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>An all-zero rectangle, used as the "not measured yet" sentinel.</summary>
    public static PixelRect Empty => default;

    /// <summary>
    /// Creates a rectangle from an origin and a size.
    /// </summary>
    /// <param name="left">The x coordinate of the left edge.</param>
    /// <param name="top">The y coordinate of the top edge.</param>
    /// <param name="width">The width in physical pixels.</param>
    /// <param name="height">The height in physical pixels.</param>
    /// <returns>The resulting rectangle.</returns>
    public static PixelRect FromSize(int left, int top, int width, int height)
    {
        return new(left, top, left + width, top + height);
    }

    /// <summary>
    /// Translates the rectangle by the supplied offsets.
    /// </summary>
    /// <param name="dx">The horizontal offset in physical pixels.</param>
    /// <param name="dy">The vertical offset in physical pixels.</param>
    /// <returns>The translated rectangle.</returns>
    public PixelRect Offset(int dx, int dy)
    {
        return new(Left + dx, Top + dy, Right + dx, Bottom + dy);
    }

    /// <summary>
    /// Determines whether the rectangle contains the supplied point.
    /// </summary>
    /// <param name="x">The x coordinate in the same space as this rectangle.</param>
    /// <param name="y">The y coordinate in the same space as this rectangle.</param>
    /// <returns><see langword="true"/> if the point falls inside the rectangle.</returns>
    public bool Contains(int x, int y)
    {
        return x >= Left && x < Right && y >= Top && y < Bottom;
    }

    /// <summary>
    /// Computes the intersection of this rectangle with another.
    /// </summary>
    /// <param name="other">The rectangle to intersect with.</param>
    /// <returns>
    /// The overlapping region, or <see cref="Empty"/> when the two rectangles do not overlap.
    /// </returns>
    public PixelRect Intersect(PixelRect other)
    {
        int left = Math.Max(Left, other.Left);
        int top = Math.Max(Top, other.Top);
        int right = Math.Min(Right, other.Right);
        int bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top ? Empty : new PixelRect(left, top, right, bottom);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"({Left},{Top})-({Right},{Bottom}) {Width}x{Height}");
    }
}
