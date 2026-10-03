using TrayMirror.Core.Geometry;

namespace TrayMirror.Core.Monitors;

/// <summary>
/// One connected display, as traymirror needs to see it.
/// </summary>
/// <param name="DeviceName">
/// The GDI device name, for example <c>\.\DISPLAY1</c>. This is what the <c>monitors</c> and
/// <c>perMonitor</c> config keys match on, because it is the only monitor identifier that survives
/// a reboot and is stable enough to write in a config file by hand.
/// </param>
/// <param name="Handle">
/// The <c>HMONITOR</c> this record was built from. Valid only for the lifetime of the enumeration
/// that produced it: a display change invalidates it, so never cache one across a rebuild.
/// </param>
/// <param name="Bounds">The full monitor rectangle in virtual-screen physical pixels.</param>
/// <param name="WorkArea">The monitor rectangle minus the taskbar and any other appbar.</param>
/// <param name="IsPrimary">Whether this is the primary monitor, the one traymirror copies from.</param>
/// <param name="Dpi">The monitor's effective DPI at the time of enumeration.</param>
public sealed record MonitorInfo(
    string DeviceName,
    nint Handle,
    PixelRect Bounds,
    PixelRect WorkArea,
    bool IsPrimary,
    DpiScale Dpi)
{
    /// <summary>
    /// Gets the height of the appbar strip along the bottom of this monitor, or zero when the
    /// working area reaches the bottom edge.
    /// </summary>
    /// <remarks>
    /// Used as the fallback taskbar height when a monitor has no <c>Shell_SecondaryTrayWnd</c> to
    /// measure, which happens when "show taskbar on all displays" is off or when the taskbar is
    /// set to auto-hide.
    /// </remarks>
    public int BottomAppBarHeight => Math.Max(0, Bounds.Bottom - WorkArea.Bottom);
}
