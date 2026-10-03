namespace TrayMirror.Core.Geometry;

/// <summary>
/// Decides whether a window is running full screen on a monitor.
/// </summary>
/// <remarks>
/// <para>
/// This exists because Windows does not tell anyone. Going full screen raises no
/// <c>WM_DISPLAYCHANGE</c>, because the resolution does not change, and no working-area
/// <c>WM_SETTINGCHANGE</c>, because a full-screen window covers the taskbar rather than displacing
/// it. Measured on build 26200 with a real full-screen window on the primary: <c>Shell_TrayWnd</c>
/// stays <c>IsWindowVisible</c> and keeps exactly the same rectangle. The shell simply lets it be
/// covered. So the only way to know is to look at the windows.
/// </para>
/// <para>
/// The test is <see cref="PixelRect"/> against the monitor's full bounds, never its working area.
/// That single choice is what separates full screen from merely maximised: a maximised window
/// stops at the working area, so its bottom edge sits above the taskbar, while a full-screen
/// window covers the monitor to its last pixel.
/// </para>
/// </remarks>
public static class FullScreenDetector
{
    /// <summary>
    /// How far a window edge may fall short of the monitor edge and still count as full screen.
    /// </summary>
    /// <remarks>
    /// Some games place themselves one pixel inside the monitor. The tolerance has to stay small:
    /// the gap between a maximised window and a full-screen one is the height of the taskbar, 48
    /// physical pixels on the reference machine, so anything under about 40 keeps the two apart.
    /// </remarks>
    public const int DefaultTolerance = 2;

    /// <summary>
    /// Determines whether a window covers a monitor completely.
    /// </summary>
    /// <param name="window">The window rectangle, from <c>GetWindowRect</c>.</param>
    /// <param name="monitorBounds">
    /// The monitor's full bounds, <c>MONITORINFO.rcMonitor</c>. Passing the working area instead
    /// would report every maximised window as full screen.
    /// </param>
    /// <param name="tolerance">
    /// How far each edge may fall short. Defaults to <see cref="DefaultTolerance"/>.
    /// </param>
    /// <returns><see langword="true"/> when the window covers the whole monitor.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is negative.</exception>
    public static bool CoversMonitor(PixelRect window, PixelRect monitorBounds, int tolerance = DefaultTolerance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);

        if (window.IsEmpty || monitorBounds.IsEmpty)
        {
            return false;
        }

        return window.Left <= monitorBounds.Left + tolerance
               && window.Top <= monitorBounds.Top + tolerance
               && window.Right >= monitorBounds.Right - tolerance
               && window.Bottom >= monitorBounds.Bottom - tolerance;
    }
}
