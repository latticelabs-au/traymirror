using TrayMirror.Core.Diagnostics;
using TrayMirror.Core.Geometry;
using TrayMirror.Core.Monitors;
using TrayMirror.Interop;

namespace TrayMirror.Shell;

/// <summary>
/// Tracks which monitors currently have a full-screen application on them.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why this is not simpler.</strong> Three obvious mechanisms were checked and all three
/// are dead ends. <c>ABN_FULLSCREENAPP</c> is documented but carries a single boolean with no
/// indication of which monitor, which is useless on a multi-monitor desktop.
/// <c>SHQueryUserNotificationState</c> is a getter with no notification, and it is global rather
/// than per monitor. <c>IAppVisibility</c> reports whether a monitor is showing immersive Windows 8
/// shell surfaces, which on Windows 11 is essentially never, and TranslucentTB uses it only to
/// detect the Start menu. What is left is looking at the windows, which is also what the shell does.
/// </para>
/// <para>
/// <strong>Why it enumerates instead of watching only the foreground window.</strong> A video
/// playing full screen on the secondary monitor stays full screen when you click something on the
/// primary. Judging by the foreground window alone would un-hide that monitor's mirror the moment
/// focus moved, which is exactly the symptom this is meant to fix. So each event triggers a sweep
/// of the top-level windows. The sweep is the response to an event, never a timer, so this remains
/// a subscription and not a poll.
/// </para>
/// <para>
/// Two out-of-process hooks drive it. <c>EVENT_SYSTEM_FOREGROUND</c> catches an application being
/// brought up full screen. <c>EVENT_OBJECT_LOCATIONCHANGE</c>, re-scoped to the foreground window's
/// own thread each time the foreground changes, catches a window that is already in front and then
/// resizes itself to full screen: a video going full screen in a browser raises no foreground
/// event at all.
/// </para>
/// <para>All members must be called on the UI thread, which is also where the hook events arrive.</para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Usage",
    "CA2216:Disposable types should declare a finalizer",
    Justification = "UnhookWinEvent is documented to work only from the thread that registered the hook, and a finaliser runs on the finaliser thread. Adding one would look like a safety net while doing nothing. The registrations are released by the operating system when the process exits.")]
internal sealed class FullScreenWatcher : IDisposable
{
    /// <summary>
    /// Window classes that cover a whole monitor as a matter of course and are never applications.
    /// </summary>
    /// <remarks>
    /// The desktop is the important one: <c>Progman</c> and <c>WorkerW</c> are exactly monitor
    /// sized, so without this every monitor would report a full-screen application permanently.
    /// </remarks>
    private static readonly string[] _ignoredClasses =
    [
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",

        // Shell panels and flyouts that are monitor sized or popup islands.
        "ControlCenterWindow",
        "Shell_InputSwitchTopLevelWindow",
        "Shell_InputSwitchDismissOverlay",
        "TopLevelWindowForOverflowXamlIsland",
        "XamlExplorerHostIslandWindow",
        "Xaml_WindowedPopupClass",
        "Windows.UI.Composition.DesktopWindowContentBridge",
        "Windows.UI.Input.InputSite.WindowClass",
        "PopupHost",
        "Shell_Flyout",
    ];

    private readonly IDiagnosticsSink _log;
    private readonly NativeMethods.WinEventProc _foregroundCallback;
    private readonly NativeMethods.WinEventProc _locationCallback;
    private readonly int _ownProcessId;
    private int _explorerProcessId;

    private nint _foregroundHook;
    private nint _locationHook;
    private HashSet<string> _monitors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initialises a new instance of the <see cref="FullScreenWatcher"/> class and registers its
    /// hooks.
    /// </summary>
    /// <param name="log">Where to record what was detected.</param>
    internal FullScreenWatcher(IDiagnosticsSink log)
    {
        ArgumentNullException.ThrowIfNull(log);

        _log = log;
        _ownProcessId = Environment.ProcessId;
        _foregroundCallback = OnForegroundChanged;
        _locationCallback = OnLocationChanged;

        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground,
            0,
            _foregroundCallback,
            0,
            0,
            NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);

        if (_foregroundHook == 0)
        {
            _log.Write("fullscreen", "The foreground hook could not be registered, so full-screen applications will not be detected.");
        }

        Evaluate();
    }

    /// <summary>Raised when the set of monitors with a full-screen application changes.</summary>
    internal event EventHandler? Changed;

    /// <summary>
    /// Gets the device names of the monitors that currently have a full-screen application on them.
    /// </summary>
    internal IReadOnlyCollection<string> Monitors => _monitors;

    /// <summary>
    /// Determines whether a monitor currently has a full-screen application on it.
    /// </summary>
    /// <param name="deviceName">The monitor's device name.</param>
    /// <returns><see langword="true"/> when a mirror on that monitor should be hidden.</returns>
    internal bool IsFullScreen(string deviceName)
    {
        return _monitors.Contains(deviceName);
    }

    /// <summary>
    /// Sweeps the top-level windows and updates the set, raising <see cref="Changed"/> if it moved.
    /// </summary>
    internal void Evaluate()
    {
        HashSet<string> found = Sweep();

        if (found.SetEquals(_monitors))
        {
            return;
        }

        string before = Describe(_monitors);
        _monitors = found;

        _log.Write("fullscreen", $"Monitors running a full-screen application: {Describe(found)} (was {before}).");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_foregroundHook != 0)
        {
            _ = NativeMethods.UnhookWinEvent(_foregroundHook);
            _foregroundHook = 0;
        }

        UnhookLocation();

        GC.KeepAlive(_foregroundCallback);
        GC.KeepAlive(_locationCallback);
    }

    private static string Describe(HashSet<string> monitors)
    {
        return monitors.Count == 0 ? "none" : string.Join(", ", monitors);
    }

    private HashSet<string> Sweep()
    {
        IReadOnlyList<MonitorInfo> monitors = MonitorEnumerator.Enumerate();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (monitors.Count == 0)
        {
            return found;
        }

        bool Callback(nint window, nint data)
        {
            if (!IsCandidate(window, out string className, out int processId))
            {
                return true;
            }

            PixelRect bounds = NativeMethods.GetWindowBounds(window);
            if (bounds.IsEmpty)
            {
                return true;
            }

            foreach (MonitorInfo monitor in monitors)
            {
                if (FullScreenDetector.CoversMonitor(bounds, monitor.Bounds))
                {
                    _ = found.Add(monitor.DeviceName);
                    if (_log.IsEnabled)
                    {
                        _log.Write("fullscreen", $"Monitor {monitor.DeviceName} covered by [{className}] (pid {processId}, bounds {bounds}).");
                    }
                }
            }

            return true;
        }

        NativeMethods.EnumWindowsProc callback = Callback;
        _ = NativeMethods.EnumWindows(callback, 0);
        GC.KeepAlive(callback);

        return found;
    }

    private bool IsCandidate(nint window, out string className, out int processId)
    {
        className = string.Empty;
        processId = 0;

        if (!NativeMethods.IsWindowVisible(window))
        {
            return false;
        }

        // Our own mirrors are topmost and sit on the monitors being judged. Counting them would
        // make every monitor look occupied the moment a mirror was placed on it.
        _ = NativeMethods.GetWindowThreadProcessId(window, out processId);
        if (processId == _ownProcessId)
        {
            return false;
        }

        className = NativeMethods.GetWindowClassName(window);
        if (_ignoredClasses.Contains(className, StringComparer.Ordinal))
        {
            return false;
        }

        // Explorer windows that cover a monitor are almost always shell surfaces (desktop, taskbar,
        // backdrop overlays, start menu, flyouts). The only Explorer window that is a true full-screen
        // application is a full-screen File Explorer window ("CabinetWClass").
        if (_explorerProcessId == 0)
        {
            nint primary = TaskbarWindows.FindPrimary();
            if (primary != 0)
            {
                _ = NativeMethods.GetWindowThreadProcessId(primary, out _explorerProcessId);
            }
        }

        if (_explorerProcessId != 0 && processId == _explorerProcessId)
        {
            if (!string.Equals(className, "CabinetWClass", StringComparison.Ordinal))
            {
                return false;
            }
        }

        // A cloaked window has a perfectly valid rectangle and is not on screen at all: a suspended
        // packaged application, or a window belonging to another virtual desktop. Without this
        // check, switching virtual desktops leaves phantom full-screen windows behind.
        return !IsCloaked(window);
    }

    private static bool IsCloaked(nint window)
    {
        int hr = DwmApi.DwmGetWindowAttribute(window, DwmApi.DwmwaCloaked, out int cloaked, sizeof(int));
        return hr >= 0 && cloaked != 0;
    }

    private void OnForegroundChanged(
        nint hook,
        int eventId,
        nint window,
        int objectId,
        int childId,
        int threadId,
        int timestamp)
    {
        if (objectId != 0 || childId != 0)
        {
            return;
        }

        // Follow the new foreground window so that it going full screen in place is also seen.
        RehookLocation(window);
        Evaluate();
    }

    private void OnLocationChanged(
        nint hook,
        int eventId,
        nint window,
        int objectId,
        int childId,
        int threadId,
        int timestamp)
    {
        if (objectId != 0 || childId != 0)
        {
            return;
        }

        Evaluate();
    }

    private void RehookLocation(nint window)
    {
        UnhookLocation();

        if (window == 0)
        {
            return;
        }

        // Scoped to the foreground window's own thread. Registered globally, this event fires for
        // every moving window on the desktop, hundreds of times a second while anything animates.
        int thread = NativeMethods.GetWindowThreadProcessId(window, out int processId);
        if (thread == 0 || processId == _ownProcessId)
        {
            return;
        }

        _locationHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventObjectLocationChange,
            NativeMethods.EventObjectLocationChange,
            0,
            _locationCallback,
            processId,
            thread,
            NativeMethods.WineventOutOfContext);
    }

    private void UnhookLocation()
    {
        if (_locationHook == 0)
        {
            return;
        }

        _ = NativeMethods.UnhookWinEvent(_locationHook);
        _locationHook = 0;
    }
}
