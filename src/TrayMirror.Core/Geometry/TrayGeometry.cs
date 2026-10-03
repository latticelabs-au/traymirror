namespace TrayMirror.Core.Geometry;

/// <summary>
/// Turns two boundary readings taken from the primary taskbar into the crop rectangle that DWM
/// composites.
/// </summary>
/// <remarks>
/// <para>
/// UI Automation supplies exactly two numbers: the left edge of the first notification-area
/// element, and the left edge of the clock. Everything between them is the strip Windows refuses
/// to draw on secondary monitors. UIA does no other work in this application. It does not
/// hit test, and it does not render.
/// </para>
/// <para>
/// The one subtlety worth stating plainly, because getting it wrong produces a bug that only
/// shows up on someone else's machine: <c>rcSource</c> is in source window coordinates, not screen
/// coordinates. The source window is <c>Shell_TrayWnd</c>, whose origin is (0, 0) on a
/// single-monitor 100% setup and is not (0, 0) anywhere else. <see cref="Resolve"/> subtracts the
/// source origin so no caller has to remember to.
/// </para>
/// </remarks>
public static class TrayGeometry
{
    /// <summary>
    /// Resolves the tray strip from raw boundary readings.
    /// </summary>
    /// <param name="notificationAreaLeft">
    /// Screen x of the left edge of the leftmost notification-area element, which is the overflow
    /// chevron when hidden icons exist and otherwise the first visible tray icon.
    /// </param>
    /// <param name="clockLeft">Screen x of the left edge of the primary taskbar clock.</param>
    /// <param name="taskbarBounds">The primary taskbar window rectangle, in screen coordinates.</param>
    /// <returns>The resolved strip.</returns>
    /// <exception cref="ArgumentException">
    /// The readings do not describe a usable strip. Callers that poll UIA should prefer
    /// <see cref="TryResolve"/>, because a taskbar caught mid-relayout legitimately reports
    /// nonsense for a frame or two.
    /// </exception>
    public static TrayStrip Resolve(int notificationAreaLeft, int clockLeft, PixelRect taskbarBounds)
    {
        if (!TryResolve(notificationAreaLeft, clockLeft, taskbarBounds, out var strip))
        {
            throw new ArgumentException(
                $"Tray boundaries {notificationAreaLeft} and {clockLeft} do not describe a strip inside taskbar {taskbarBounds}.",
                nameof(clockLeft));
        }

        return strip;
    }

    /// <summary>
    /// Attempts to resolve the tray strip from raw boundary readings.
    /// </summary>
    /// <param name="notificationAreaLeft">Screen x of the left edge of the first tray element.</param>
    /// <param name="clockLeft">Screen x of the left edge of the primary taskbar clock.</param>
    /// <param name="taskbarBounds">The primary taskbar window rectangle, in screen coordinates.</param>
    /// <param name="strip">The resolved strip when this method returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="false"/> when the readings are unusable: the clock at or left of the
    /// notification area, an empty taskbar rectangle, or a strip that falls outside the taskbar.
    /// </returns>
    public static bool TryResolve(
        int notificationAreaLeft,
        int clockLeft,
        PixelRect taskbarBounds,
        out TrayStrip strip)
    {
        strip = default;

        if (taskbarBounds.IsEmpty || clockLeft <= notificationAreaLeft)
        {
            return false;
        }

        // Clamp to the taskbar. A boundary read while the taskbar is animating can land outside
        // it, and an rcSource that exceeds the source window makes DWM stretch whatever it finds.
        int left = Math.Max(notificationAreaLeft, taskbarBounds.Left);
        int right = Math.Min(clockLeft, taskbarBounds.Right);
        if (right <= left)
        {
            return false;
        }

        var screenBounds = new PixelRect(left, taskbarBounds.Top, right, taskbarBounds.Bottom);
        strip = new TrayStrip(screenBounds, ToSourceRelative(screenBounds, taskbarBounds));
        return true;
    }

    /// <summary>
    /// Rebases a screen-space rectangle into the coordinate space of a source window.
    /// </summary>
    /// <param name="screenBounds">The rectangle in screen coordinates.</param>
    /// <param name="sourceWindowBounds">
    /// The source window rectangle in screen coordinates, as returned by <c>GetWindowRect</c>.
    /// </param>
    /// <returns>The rectangle relative to the source window's top-left corner.</returns>
    /// <remarks>
    /// This is the step that is easy to skip and hard to notice skipping. On a primary taskbar
    /// whose origin happens to be near (0, 0), the unrebased rectangle is close enough to correct
    /// that the mirror looks right, and it then breaks on any machine whose primary monitor is not
    /// at the origin.
    /// </remarks>
    public static PixelRect ToSourceRelative(PixelRect screenBounds, PixelRect sourceWindowBounds)
    {
        return screenBounds.Offset(-sourceWindowBounds.Left, -sourceWindowBounds.Top);
    }
}
