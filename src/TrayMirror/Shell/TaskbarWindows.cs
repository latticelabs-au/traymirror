using TrayMirror.Core.Geometry;
using TrayMirror.Core.Monitors;
using TrayMirror.Interop;

namespace TrayMirror.Shell;

/// <summary>
/// Finds the taskbar windows traymirror reads from and anchors to.
/// </summary>
/// <remarks>
/// Nothing here is cached across a rebuild. Explorer destroys and recreates every one of these
/// windows when it restarts, and a stale <c>Shell_TrayWnd</c> handle registered as a DWM source
/// produces a mirror that renders nothing with no error anywhere.
/// </remarks>
internal static class TaskbarWindows
{
    private const string PrimaryClass = "Shell_TrayWnd";
    private const string SecondaryClass = "Shell_SecondaryTrayWnd";
    private const string NotifyAreaClass = "TrayNotifyWnd";

    /// <summary>Finds the primary taskbar, which is the window every mirror copies from.</summary>
    /// <returns>The window handle, or zero when Explorer is not running.</returns>
    internal static nint FindPrimary()
    {
        NativeMethods.EnsureDefaultDesktop();
        return NativeMethods.FindWindow(PrimaryClass, null);
    }

    /// <summary>
    /// Finds the legacy notification-area child of the primary taskbar.
    /// </summary>
    /// <param name="primaryTaskbar">The primary taskbar window.</param>
    /// <returns>The window handle, or zero when it does not exist.</returns>
    /// <remarks>
    /// On Windows 11 24H2 this window still exists but has no children, because the tray itself is
    /// XAML. Its rectangle is used only as a fallback boundary when UI Automation cannot resolve
    /// the notification area, never as a source of icon positions. Reading icon data out of it
    /// would mean reading undocumented Explorer internals, which this project does not do.
    /// </remarks>
    internal static nint FindNotifyArea(nint primaryTaskbar)
    {
        if (primaryTaskbar == 0)
        {
            return 0;
        }

        return NativeMethods.FindWindowEx(primaryTaskbar, 0, NotifyAreaClass, null);
    }

    /// <summary>
    /// Finds every secondary taskbar and the monitor each one sits on.
    /// </summary>
    /// <returns>A map from monitor device name to that monitor's secondary taskbar window.</returns>
    /// <remarks>
    /// A monitor with no entry has "show my taskbar on all displays" turned off, or Explorer has
    /// not finished creating its bar yet. Mirrors still get placed on such a monitor, anchored to
    /// its bottom right corner instead of to a clock.
    /// </remarks>
    internal static Dictionary<string, nint> FindSecondaries()
    {
        NativeMethods.EnsureDefaultDesktop();
        var found = new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase);

        bool Callback(nint window, nint data)
        {
            if (!string.Equals(NativeMethods.GetWindowClassName(window), SecondaryClass, StringComparison.Ordinal))
            {
                return true;
            }

            MonitorInfo? monitor = MonitorEnumerator.FromWindow(window);
            if (monitor is not null)
            {
                found[monitor.DeviceName] = window;
            }

            return true;
        }

        NativeMethods.EnumWindowsProc callback = Callback;
        _ = NativeMethods.EnumWindows(callback, 0);
        GC.KeepAlive(callback);

        return found;
    }

    /// <summary>
    /// Determines whether a window class is one of the two taskbar classes.
    /// </summary>
    /// <param name="className">The class name to test.</param>
    /// <returns><see langword="true"/> for the primary or a secondary taskbar.</returns>
    internal static bool IsTaskbarClass(string className)
    {
        return string.Equals(className, PrimaryClass, StringComparison.Ordinal)
               || string.Equals(className, SecondaryClass, StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether a taskbar window exists but is currently not on screen.
    /// </summary>
    /// <param name="taskbar">The taskbar window handle.</param>
    /// <returns><see langword="true"/> when the window is real but hidden.</returns>
    /// <remarks>
    /// Hidden is not the same as absent, and the two need different answers. A monitor with no
    /// secondary taskbar at all has the feature turned off, and a mirror there is placed in the
    /// corner instead. A taskbar that exists and is hidden has been put away by the shell, because
    /// an application went full screen or because auto-hide is on, and the mirror has to go away
    /// with it rather than be left floating over the full-screen application.
    /// </remarks>
    internal static bool IsPresentButHidden(nint taskbar)
    {
        return taskbar != 0 && NativeMethods.IsWindow(taskbar) && !NativeMethods.IsWindowVisible(taskbar);
    }

    /// <summary>Reads a taskbar rectangle, treating a hidden bar as absent.</summary>
    /// <param name="taskbar">The taskbar window handle.</param>
    /// <returns>The rectangle, or <see cref="PixelRect.Empty"/>.</returns>
    /// <remarks>
    /// An auto-hidden taskbar keeps a valid window rectangle that sits almost entirely off screen.
    /// Anchoring a mirror to that rectangle would park the mirror off screen too, so a bar that is
    /// not visible is reported as absent and the caller falls back to corner placement.
    /// </remarks>
    internal static PixelRect GetVisibleBounds(nint taskbar)
    {
        if (taskbar == 0 || !NativeMethods.IsWindowVisible(taskbar))
        {
            return PixelRect.Empty;
        }

        return NativeMethods.GetWindowBounds(taskbar);
    }
}
