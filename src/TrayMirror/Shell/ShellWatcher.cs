using TrayMirror.Core.Diagnostics;
using TrayMirror.Core.Scheduling;
using TrayMirror.Interop;

namespace TrayMirror.Shell;

/// <summary>
/// Turns the shell events that invalidate a mirror into rebuild requests.
/// </summary>
/// <remarks>
/// <para>
/// All three sources are messages Windows sends. None of them is a poll, and adding one would be a
/// regression: polling is power-expensive, and it races with the very restarts it is trying to
/// detect, because the poll can read a half-built taskbar and cache the result.
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <c>TaskbarCreated</c>, a registered window message broadcast to every top-level window when
/// Explorer recreates the taskbar. This is the one that matters most: every thumbnail handle
/// targeting the old <c>Shell_TrayWnd</c> is dead by the time it arrives.
/// </description>
/// </item>
/// <item>
/// <description><c>WM_DISPLAYCHANGE</c>, for monitors arriving, leaving or being reconfigured.</description>
/// </item>
/// <item>
/// <description>
/// <c>WM_SETTINGCHANGE</c> with a <c>SPI_SETWORKAREA</c> payload, which is what a taskbar moving,
/// resizing or switching to auto-hide looks like from outside Explorer.
/// </description>
/// </item>
/// </list>
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Usage",
    "CA2216:Disposable types should declare a finalizer",
    Justification = "UnhookWinEvent is documented to work only from the thread that registered the hook, and a finaliser runs on the finaliser thread. Adding one would look like a safety net while doing nothing. The registration is released by the operating system when the process exits.")]
internal sealed class ShellWatcher : IDisposable
{
    private readonly IDiagnosticsSink _log;
    private readonly int _taskbarCreatedMessage;
    private readonly NativeMethods.WinEventProc _visibilityCallback;

    private nint _visibilityHook;
    private nint _watchedTaskbar;

    /// <summary>
    /// Initialises a new instance of the <see cref="ShellWatcher"/> class and attaches it to a
    /// message window.
    /// </summary>
    /// <param name="window">The window whose messages are watched.</param>
    /// <param name="log">Where to record the events that arrive.</param>
    /// <remarks>Must be constructed on the UI thread.</remarks>
    internal ShellWatcher(MessageWindow window, IDiagnosticsSink log)
    {
        ArgumentNullException.ThrowIfNull(window);

        _log = log;
        _visibilityCallback = OnVisibilityEvent;
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        if (_taskbarCreatedMessage == 0)
        {
            _log.Write("shell", "TaskbarCreated could not be registered, so an Explorer restart will not be detected.");
        }

        window.AddHook(OnMessage);
        WatchTaskbarVisibility(TaskbarWindows.FindPrimary());
    }

    /// <summary>Raised when something happened that invalidates the current mirrors.</summary>
    internal event EventHandler<RebuildReasons>? Invalidated;

    /// <inheritdoc/>
    public void Dispose()
    {
        UnhookVisibility();
        GC.KeepAlive(_visibilityCallback);
    }

    /// <summary>
    /// Watches the shell for taskbars being hidden and shown.
    /// </summary>
    /// <param name="primaryTaskbar">
    /// The primary taskbar, used only to find the process the hook is scoped to.
    /// </param>
    /// <remarks>
    /// <para>
    /// An application going full screen makes the shell hide the taskbar, and nothing else reports
    /// it. There is no display change, because the resolution did not change, and no working-area
    /// change, because a full-screen window covers the taskbar rather than displacing it. Without
    /// this, a mirror is left floating over the full-screen application, and the mirror then
    /// disappears when the taskbar comes back, which is precisely backwards.
    /// </para>
    /// <para>
    /// The hook is out of process and scoped to the shell's process, so nothing is loaded into
    /// <c>explorer.exe</c> and the callback only sees Explorer's own windows. It is still a
    /// subscription and not a poll: the shell raises the event when it hides the bar.
    /// </para>
    /// </remarks>
    internal void WatchTaskbarVisibility(nint primaryTaskbar)
    {
        if (primaryTaskbar == _watchedTaskbar && _visibilityHook != 0)
        {
            return;
        }

        UnhookVisibility();
        _watchedTaskbar = primaryTaskbar;

        if (primaryTaskbar == 0)
        {
            return;
        }

        _ = NativeMethods.GetWindowThreadProcessId(primaryTaskbar, out int shellProcessId);
        if (shellProcessId == 0)
        {
            return;
        }

        _visibilityHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventObjectShow,
            NativeMethods.EventObjectHide,
            0,
            _visibilityCallback,
            shellProcessId,
            0,
            NativeMethods.WineventOutOfContext);

        if (_visibilityHook == 0)
        {
            _log.Write("shell", "The taskbar visibility hook could not be registered, so a mirror may outlive a hidden taskbar.");
        }
    }

    private void UnhookVisibility()
    {
        if (_visibilityHook == 0)
        {
            return;
        }

        _ = NativeMethods.UnhookWinEvent(_visibilityHook);
        _visibilityHook = 0;
    }

    private void OnVisibilityEvent(
        nint hook,
        int eventId,
        nint window,
        int objectId,
        int childId,
        int threadId,
        int timestamp)
    {
        if (window == 0 || objectId != 0 || childId != 0)
        {
            return;
        }

        string className = NativeMethods.GetWindowClassName(window);
        if (!TaskbarWindows.IsTaskbarClass(className))
        {
            return;
        }

        _log.Write("shell", $"{className} was {(eventId == NativeMethods.EventObjectShow ? "shown" : "hidden")}.");
        Invalidated?.Invoke(this, RebuildReasons.DisplayChange);
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
        {
            _log.Write("shell", "Explorer restarted (TaskbarCreated). Every thumbnail handle is now stale.");

            // The old taskbar window is gone, so the visibility hook is watching a dead handle.
            WatchTaskbarVisibility(TaskbarWindows.FindPrimary());
            Invalidated?.Invoke(this, RebuildReasons.TaskbarCreated);
            return 0;
        }

        switch (message)
        {
            case NativeMethods.WmDisplayChange:
                _log.Write("shell", "WM_DISPLAYCHANGE.");
                Invalidated?.Invoke(this, RebuildReasons.DisplayChange);
                break;

            case NativeMethods.WmDpiChanged:
                _log.Write("shell", "WM_DPICHANGED.");
                Invalidated?.Invoke(this, RebuildReasons.DpiChange);
                break;

            case NativeMethods.WmSettingChange:
                // SPI_SETWORKAREA. A taskbar that moved, resized or started auto-hiding changes the
                // working area, and every mirror is anchored to a taskbar.
                if (wParam == 47)
                {
                    _log.Write("shell", "The working area changed.");
                    Invalidated?.Invoke(this, RebuildReasons.DisplayChange);
                }

                break;

            default:
                break;
        }

        return 0;
    }
}
