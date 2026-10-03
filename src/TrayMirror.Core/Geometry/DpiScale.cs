namespace TrayMirror.Core.Geometry;

/// <summary>
/// Converts between the device-independent units WPF lays out in and the physical pixels every
/// Win32 and DWM call expects.
/// </summary>
/// <remarks>
/// <para>
/// Windows reports DPI as a raw dots-per-inch value where 96 is 100% scaling. A monitor at 150%
/// reports 144. Nothing in this type talks to Windows: the caller reads the DPI once with
/// <c>GetDpiForMonitor</c> or <c>GetDpiForWindow</c> and hands the number over, which keeps the
/// arithmetic unit testable across scaling combinations that the development machine cannot
/// physically produce.
/// </para>
/// <para>
/// Mirror placement and the DWM source crop are computed entirely in physical pixels. This type
/// exists for the two boundaries where that is not possible: WPF window sizing, and comparing
/// geometry read at one monitor's DPI against geometry read at another's.
/// </para>
/// </remarks>
public readonly record struct DpiScale
{
    /// <summary>The DPI value Windows reports for an unscaled (100%) display.</summary>
    public const int DefaultDpi = 96;

    /// <summary>
    /// Initialises a new instance of the <see cref="DpiScale"/> struct.
    /// </summary>
    /// <param name="dpiX">The horizontal DPI, where 96 is 100% scaling.</param>
    /// <param name="dpiY">The vertical DPI, where 96 is 100% scaling.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either DPI is zero or negative. Windows never reports such a value, so receiving one means
    /// an interop call failed and its result was used without being checked.
    /// </exception>
    public DpiScale(int dpiX, int dpiY)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpiX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpiY);
        DpiX = dpiX;
        DpiY = dpiY;
    }

    /// <summary>Gets the horizontal DPI.</summary>
    public int DpiX { get; }

    /// <summary>Gets the vertical DPI.</summary>
    public int DpiY { get; }

    /// <summary>The identity scale, 96 DPI on both axes.</summary>
    public static DpiScale Identity => new(DefaultDpi, DefaultDpi);

    /// <summary>Gets the horizontal scale factor, where 1.0 is 100%.</summary>
    public double ScaleX => (double)DpiX / DefaultDpi;

    /// <summary>Gets the vertical scale factor, where 1.0 is 100%.</summary>
    public double ScaleY => (double)DpiY / DefaultDpi;

    /// <summary>Converts a horizontal measurement from physical pixels to WPF units.</summary>
    /// <param name="physicalX">The measurement in physical pixels.</param>
    /// <returns>The equivalent measurement in device-independent units.</returns>
    public double ToLogicalX(int physicalX)
    {
        return physicalX / ScaleX;
    }

    /// <summary>Converts a vertical measurement from physical pixels to WPF units.</summary>
    /// <param name="physicalY">The measurement in physical pixels.</param>
    /// <returns>The equivalent measurement in device-independent units.</returns>
    public double ToLogicalY(int physicalY)
    {
        return physicalY / ScaleY;
    }

    /// <summary>Converts a horizontal measurement from WPF units to physical pixels.</summary>
    /// <param name="logicalX">The measurement in device-independent units.</param>
    /// <returns>The equivalent measurement in physical pixels, rounded to nearest.</returns>
    public int ToPhysicalX(double logicalX)
    {
        return (int)Math.Round(logicalX * ScaleX, MidpointRounding.AwayFromZero);
    }

    /// <summary>Converts a vertical measurement from WPF units to physical pixels.</summary>
    /// <param name="logicalY">The measurement in device-independent units.</param>
    /// <returns>The equivalent measurement in physical pixels, rounded to nearest.</returns>
    public int ToPhysicalY(double logicalY)
    {
        return (int)Math.Round(logicalY * ScaleY, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Rescales a horizontal measurement taken at this DPI into the equivalent measurement at
    /// another DPI.
    /// </summary>
    /// <param name="physicalX">The measurement in physical pixels at this DPI.</param>
    /// <param name="target">The DPI to express the result in.</param>
    /// <returns>The measurement in physical pixels at <paramref name="target"/>.</returns>
    /// <remarks>
    /// Needed because the tray strip is measured on the primary monitor but drawn on a secondary
    /// one. When the two monitors scale differently, a width in primary pixels is not the same
    /// number of secondary pixels.
    /// </remarks>
    public int RescaleX(int physicalX, DpiScale target)
    {
        return (int)Math.Round(physicalX * (target.ScaleX / ScaleX), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Rescales a vertical measurement taken at this DPI into the equivalent measurement at
    /// another DPI.
    /// </summary>
    /// <param name="physicalY">The measurement in physical pixels at this DPI.</param>
    /// <param name="target">The DPI to express the result in.</param>
    /// <returns>The measurement in physical pixels at <paramref name="target"/>.</returns>
    public int RescaleY(int physicalY, DpiScale target)
    {
        return (int)Math.Round(physicalY * (target.ScaleY / ScaleY), MidpointRounding.AwayFromZero);
    }
}
