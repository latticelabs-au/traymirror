using System.Globalization;
using TrayMirror.Core.Diagnostics;
using TrayMirror.Core.Geometry;
using TrayMirror.Interop;

namespace TrayMirror.Input;

/// <summary>
/// Moves the menu that a routed right-click opened on the primary monitor across to the mirror the
/// user actually clicked.
/// </summary>
/// <remarks>
/// <para>
/// Two out-of-process <c>SetWinEventHook</c> registrations, and both are needed:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <c>EVENT_SYSTEM_MENUPOPUPSTART</c> catches classic Win32 <c>#32768</c> tray menus, which is
/// what almost every older tray application uses.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>EVENT_OBJECT_SHOW</c> catches everything else: XAML island flyouts such as the
/// quick-settings panels and the hidden-icons overflow, and the ordinary top-level windows that
/// modern tray applications open in place of a menu. None of those raises the menu event at all,
/// so a single hook would miss them completely.
/// </description>
/// </item>
/// </list>
/// <para>
/// Neither hook requires injection. Both are registered with <c>WINEVENT_OUTOFCONTEXT</c>, so
/// nothing is loaded into <c>explorer.exe</c> and the events are delivered to this process through
/// its own message pump. That is what keeps traymirror usable on a machine with endpoint detection
/// or application control in place.
/// </para>
/// <para>
/// <strong>How a tray flyout is recognised.</strong> Not by window class alone. A class allow list
/// covers classic <c>#32768</c> menus and the known shell island classes, and it covers nothing
/// else: OneDrive, measured on build 26200, answers a tray right-click with a 360x640 window of
/// its own private class. So the class list is only a fast path, and the general rule is the shape
/// of the thing. A window that appears inside the arming window, sits horizontally within the
/// primary taskbar, is small enough not to be an application window, and has its bottom edge
/// resting just above that taskbar, is a tray flyout whatever it calls itself.
/// </para>
/// <para>
/// Relocation is armed for a short window around a routed right-click and disarmed as soon as it
/// fires. Without that arming window the geometric rule would move unrelated windows.
/// </para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Usage",
    "CA2216:Disposable types should declare a finalizer",
    Justification = "UnhookWinEvent is documented to work only from the thread that registered the hook, and a finaliser runs on the finaliser thread. Adding one would look like a safety net while doing nothing. Both registrations are released by the operating system when the process exits, so the only real leak window is a process that never exits, which this is not.")]
internal sealed class MenuRelocator : IDisposable
{
    /// <summary>
    /// How far above the taskbar a flyout's bottom edge may sit and still be recognised as one.
    /// </summary>
    /// <remarks>
    /// Windows 11 leaves a small gap between a tray flyout and the taskbar, and applications pick
    /// their own. The OneDrive panel measured 12 physical pixels on build 26200, so the tolerance
    /// is generous enough to absorb a different choice without being wide enough to catch a window
    /// that merely happens to end partway up the screen.
    /// </remarks>
    private const int FlyoutGapTolerance = 64;

    private static readonly string[] _knownMenuClasses =
    [
        "#32768",
        "Xaml_WindowedPopupClass",
        "XamlExplorerHostIslandWindow",
        "TopLevelWindowForOverflowXamlIsland",

        // Created at the full size of a monitor and then animated into the corner, so the general
        // shape rule below would reject it. It is the only shell host that needs to be here.
        //
        // Deliberately absent: Shell_InputSwitchTopLevelWindow, the language switcher's host, and
        // the Shell_InputSwitchDismissOverlay beside it. Measured on build 26200, moving that host
        // moves nothing the user can see, because the list is a separate 328x105
        // Xaml_WindowedPopupClass that the general rule already accepts. Worse, the host is raised
        // first, so accepting it consumed the arming and the real popup was never reached. Every
        // entry on this list must be the window that carries the pixels, not the window that hosts
        // the window that carries the pixels.
        "ControlCenterWindow",
    ];

    private readonly IDiagnosticsSink _log;
    private readonly NativeMethods.WinEventProc _callback;
    private readonly int _ownProcessId;

    private nint _menuHook;
    private nint _showHook;
    private nint _uncloakHook;
    private ArmedRelocation? _armed;

    /// <summary>
    /// Initialises a new instance of the <see cref="MenuRelocator"/> class and registers both
    /// hooks.
    /// </summary>
    /// <param name="log">Where to record what was relocated.</param>
    /// <remarks>
    /// Must be constructed on the UI thread, because that is the thread the hook events are
    /// delivered to.
    /// </remarks>
    internal MenuRelocator(IDiagnosticsSink log)
    {
        _log = log;
        _callback = OnWinEvent;
        _ownProcessId = Environment.ProcessId;

        _menuHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemMenuPopupStart,
            NativeMethods.EventSystemMenuPopupStart,
            0,
            _callback,
            0,
            0,
            NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);

        _showHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventObjectShow,
            NativeMethods.EventObjectShow,
            0,
            _callback,
            0,
            0,
            NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);

        _uncloakHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventObjectUncloaked,
            NativeMethods.EventObjectUncloaked,
            0,
            _callback,
            0,
            0,
            NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);

        if (_menuHook == 0 || _showHook == 0 || _uncloakHook == 0)
        {
            _log.Write("menu", "One or more WinEvent hooks could not be registered. Menus will stay on the primary monitor.");
        }
    }

    /// <summary>
    /// Arms relocation for the flyout that the next routed right-click is about to open.
    /// </summary>
    /// <param name="mirrorBounds">The mirror the user clicked, in screen coordinates.</param>
    /// <param name="monitorWorkArea">The target monitor's working area, used to clamp the flyout.</param>
    /// <param name="primaryTaskbar">
    /// The primary taskbar rectangle. A window resting just above it is what identifies it as a
    /// tray flyout rather than an ordinary window that happened to open at the same moment.
    /// </param>
    /// <param name="primaryMonitor">
    /// The primary monitor rectangle, used to tell a flyout from a shell host window that happens
    /// to be nearly as tall as the screen.
    /// </param>
    /// <param name="primaryPoint">The primary-screen point the click was routed to.</param>
    /// <param name="mirrorPoint">The screen point on the mirror that the user actually clicked.</param>
    /// <param name="window">How long to keep watching.</param>
    internal void Arm(
        PixelRect mirrorBounds,
        PixelRect monitorWorkArea,
        PixelRect primaryTaskbar,
        PixelRect primaryMonitor,
        (int X, int Y) primaryPoint,
        (int X, int Y) mirrorPoint,
        TimeSpan window)
    {
        _armed = new ArmedRelocation(
            mirrorBounds,
            monitorWorkArea,
            primaryTaskbar,
            primaryMonitor,
            primaryPoint,
            mirrorPoint,
            DateTimeOffset.UtcNow + window);
    }

    /// <summary>Raised after a flyout has been moved onto a mirror.</summary>
    internal event EventHandler<FlyoutRelocatedEventArgs>? FlyoutRelocated;

    /// <summary>Stops watching.</summary>
    internal void Disarm()
    {
        _armed = null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_menuHook != 0)
        {
            _ = NativeMethods.UnhookWinEvent(_menuHook);
            _menuHook = 0;
        }

        if (_showHook != 0)
        {
            _ = NativeMethods.UnhookWinEvent(_showHook);
            _showHook = 0;
        }

        if (_uncloakHook != 0)
        {
            _ = NativeMethods.UnhookWinEvent(_uncloakHook);
            _uncloakHook = 0;
        }

        GC.KeepAlive(_callback);
    }

    /// <summary>
    /// Decides whether a window that just appeared is the flyout the routed right-click opened.
    /// </summary>
    /// <param name="className">The window's class name.</param>
    /// <param name="bounds">The window's screen rectangle.</param>
    /// <param name="primaryTaskbar">The primary taskbar rectangle.</param>
    /// <param name="primaryMonitor">The primary monitor rectangle.</param>
    /// <returns><see langword="true"/> when the window should be moved to the mirror.</returns>
    private static bool LooksLikeATrayFlyout(
        string className,
        PixelRect bounds,
        PixelRect primaryTaskbar,
        PixelRect primaryMonitor)
    {
        // Checked before the class allow list, not after. A zero-sized window is never the flyout,
        // whatever it calls itself: measured on build 26200, opening quick settings also creates a
        // 0x0 Xaml_WindowedPopupClass, and accepting that one relocated nothing while consuming
        // the arming, so the real panel was never moved.
        if (bounds.IsEmpty || primaryTaskbar.IsEmpty)
        {
            return false;
        }

        if (_knownMenuClasses.Contains(className, StringComparer.Ordinal))
        {
            return true;
        }

        // Not a full-height shell host. Anything filling the screen vertically is a host window
        // rather than a panel, with one exception that is on the allow list above: the
        // quick-settings ControlCenterWindow really is created at the full height of the screen,
        // 384x1032 on the reference machine, and animates its content into the corner from there.
        if (!primaryMonitor.IsEmpty && bounds.Height > primaryMonitor.Height * 3 / 4)
        {
            return false;
        }

        // Resting on the taskbar, which is where a tray flyout opens and where an ordinary
        // application window almost never happens to land.
        int gap = primaryTaskbar.Top - bounds.Bottom;
        if (gap is < 0 or > FlyoutGapTolerance)
        {
            return false;
        }

        // Horizontally inside the primary taskbar's span, and small enough to be a panel rather
        // than a maximised window that merely stops where the taskbar starts.
        return bounds.Left >= primaryTaskbar.Left
               && bounds.Right <= primaryTaskbar.Right
               && bounds.Width < primaryTaskbar.Width / 2;
    }

    private static (int Left, int Top) TargetPosition(ArmedRelocation target, PixelRect menu, string className)
    {
        int left;
        int top;

        if (string.Equals(className, "TopLevelWindowForOverflowXamlIsland", StringComparison.Ordinal))
        {
            // Center the overflow flyout directly over the clicked chevron icon on the mirror
            left = target.MirrorPoint.X - (menu.Width / 2);
            int gap = 8;
            top = target.MirrorBounds.Top - gap - menu.Height;
        }
        else
        {
            // Preserve the flyout's offset from the click. One that Windows aligned to the left edge of
            // an icon should stay aligned to the left edge of the mirrored icon, not be re-centred.
            left = target.MirrorPoint.X + (menu.Left - target.PrimaryPoint.X);

            // Keep the gap the flyout chose above the primary taskbar rather than the absolute
            // vertical offset, because the two taskbars can be different heights.
            int gap = target.PrimaryTaskbar.IsEmpty ? 0 : target.PrimaryTaskbar.Top - menu.Bottom;
            top = target.MirrorBounds.Top - gap - menu.Height;
        }

        PixelRect work = target.MonitorWorkArea;
        if (!work.IsEmpty)
        {
            left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - menu.Width));
            top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - menu.Height));
        }

        return (left, top);
    }

    private void OnWinEvent(
        nint hook,
        int eventId,
        nint window,
        int objectId,
        int childId,
        int threadId,
        int timestamp)
    {
        if (_armed is not { } target || window == 0 || objectId != 0 || childId != 0)
        {
            return;
        }

        if (DateTimeOffset.UtcNow > target.ExpiresAt)
        {
            _armed = null;
            return;
        }

        _ = NativeMethods.GetWindowThreadProcessId(window, out int processId);
        if (processId == _ownProcessId)
        {
            return;
        }

        string className = NativeMethods.GetWindowClassName(window);
        PixelRect menu = NativeMethods.GetWindowBounds(window);

        if (!LooksLikeATrayFlyout(className, menu, target.PrimaryTaskbar, target.PrimaryMonitor))
        {
            // Recorded rather than dropped. The windows the Windows 11 shell and tray applications
            // use for menus are not documented and change between updates, so the log has to say
            // what was actually offered before anyone can judge whether it should have been taken.
            if (_log.IsEnabled)
            {
                _log.Write("menu", string.Create(
                    CultureInfo.InvariantCulture,
                    $"Ignored a {className} at {menu} while armed: it does not have the shape of a tray flyout."));
            }

            return;
        }

        (int left, int top) = TargetPosition(target, menu, className);

        bool moved = NativeMethods.SetWindowPos(
            window,
            NativeMethods.HwndTopmost,
            left,
            top,
            0,
            0,
            NativeMethods.SwpNoSize | NativeMethods.SwpShowWindow | NativeMethods.SwpNoActivate);

        _log.Write("menu", string.Create(
            CultureInfo.InvariantCulture,
            $"{(moved ? "Relocated" : "Failed to relocate")} a {className} from {menu} to ({left},{top})."));

        if (moved)
        {
            FlyoutRelocated?.Invoke(this, new FlyoutRelocatedEventArgs(window, target.PrimaryPoint.X));
        }

        _armed = null;
    }

    /// <summary>Identifies a flyout that has just been moved onto a mirror.</summary>
    /// <param name="window">The relocated window.</param>
    /// <param name="primaryX">
    /// The primary-screen x of the routed click that opened it, which is how the controller tells
    /// a second click on the same icon from a click on a different one.
    /// </param>
    internal sealed class FlyoutRelocatedEventArgs(nint window, int primaryX) : EventArgs
    {
        /// <summary>Gets the relocated window.</summary>
        internal nint Window { get; } = window;

        /// <summary>Gets the primary-screen x of the click that opened it.</summary>
        internal int PrimaryX { get; } = primaryX;
    }

    private sealed record ArmedRelocation(
        PixelRect MirrorBounds,
        PixelRect MonitorWorkArea,
        PixelRect PrimaryTaskbar,
        PixelRect PrimaryMonitor,
        (int X, int Y) PrimaryPoint,
        (int X, int Y) MirrorPoint,
        DateTimeOffset ExpiresAt);
}
