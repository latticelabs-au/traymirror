using System.Globalization;
using System.Windows.Threading;
using TrayMirror.Core.Configuration;
using TrayMirror.Core.Diagnostics;
using TrayMirror.Core.Geometry;
using TrayMirror.Core.Input;
using TrayMirror.Core.Monitors;
using TrayMirror.Core.Scheduling;
using TrayMirror.Input;
using TrayMirror.Interop;
using TrayMirror.Shell;

namespace TrayMirror.Mirroring;

/// <summary>
/// Keeps one mirror per target monitor in step with the primary tray.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Threading.</strong> Everything in this class runs on the UI thread except the UI
/// Automation probe, which runs on the thread pool and hands its results back through the
/// dispatcher. That split is not a preference. DWM and Win32 window calls are apartment-threaded,
/// while UIA calls are cross-process COM round trips that block for tens to hundreds of
/// milliseconds; running them on the UI thread stalls the message pump and the user sees the
/// mirrors stop updating.
/// </para>
/// <para>
/// <strong>Rebuild discipline.</strong> Every rebuild releases every thumbnail handle before it
/// registers anything, including on paths where the handles are already dead. That is cheap, it is
/// safe, and it means there is exactly one code path in which a handle can exist.
/// </para>
/// </remarks>
internal sealed class MirrorController : IDisposable
{
    private static readonly TimeSpan _quietWindow = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _tickInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan _cursorRestoreDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan _menuArmWindow = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// How far apart two routed clicks may land and still count as the same tray icon.
    /// </summary>
    /// <remarks>
    /// Icons on the reference machine are 28 to 44 physical pixels wide at a 32 pixel pitch, so
    /// this is wide enough to cover two clicks near opposite edges of one icon and narrow enough
    /// not to swallow the next icon along.
    /// </remarks>
    private const int SameIconSlop = 24;

    /// <summary>
    /// How many times a failed boundary read is retried before traymirror waits for a shell event.
    /// </summary>
    /// <remarks>
    /// Reading the boundaries can fail transiently while the shell settles, and the mirrors are
    /// hidden when it does. Hiding is the right response, but on its own it leaves recovery to the
    /// next shell event, and there may not be one for hours: observed on build 26200, a single
    /// failed read left the mirrors hidden indefinitely with the desktop otherwise idle. This is a
    /// bounded retry after a failure, not a poll: it stops as soon as a read succeeds, and it stops
    /// anyway once the attempts run out.
    /// </remarks>
    private const int MaxStripRetries = 5;

    private static readonly TimeSpan _stripRetryDelay = TimeSpan.FromSeconds(1);

    private readonly Dispatcher _dispatcher;
    private readonly IDiagnosticsSink _log;
    private readonly ThumbnailHost _thumbnails;
    private readonly InputRouter _router;
    private readonly MenuRelocator _relocator;
    private readonly TrayChangeWatcher _trayWatcher;
    private readonly FullScreenWatcher _fullScreen;
    private readonly RebuildCoalescer _coalescer = new(_quietWindow);
    private readonly DispatcherTimer _tick;
    private readonly Dictionary<string, MirrorWindow> _mirrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PixelRect> _monitorWorkAreas = new(StringComparer.OrdinalIgnoreCase);

    private TrayMirrorConfig _config = TrayMirrorConfig.Default;
    private TrayStrip _strip;
    private nint _primaryTaskbar;
    private PixelRect _primaryTaskbarBounds;
    private PixelRect _primaryMonitorBounds;
    private bool _rebuildInFlight;
    private bool _disposed;
    private nint _openFlyout;
    private int _openFlyoutPrimaryX;
    private bool _flyoutOpenOnPress;
    private int _stripRetries;

    /// <summary>
    /// Initialises a new instance of the <see cref="MirrorController"/> class.
    /// </summary>
    /// <param name="dispatcher">The UI thread's dispatcher.</param>
    /// <param name="log">Where to record what happens.</param>
    /// <remarks>Must be constructed on the UI thread.</remarks>
    internal MirrorController(Dispatcher dispatcher, IDiagnosticsSink log)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(log);

        _dispatcher = dispatcher;
        _log = log;

        _thumbnails = new ThumbnailHost(_log);
        _router = new InputRouter(_log);
        _relocator = new MenuRelocator(_log);
        _trayWatcher = new TrayChangeWatcher(_log, OnTrayStructureChanged);
        _fullScreen = new FullScreenWatcher(_log);
        _fullScreen.Changed += OnFullScreenChanged;
        _relocator.FlyoutRelocated += OnFlyoutRelocated;

        _tick = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = _tickInterval,
        };

        _tick.Tick += OnTick;
    }

    /// <summary>Gets or sets the configuration in force. Assigning triggers a rebuild.</summary>
    internal TrayMirrorConfig Config
    {
        get => _config;

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _config = value;
            RequestRebuild(RebuildReasons.Configuration);
        }
    }

    /// <summary>Gets the number of mirrors currently on screen.</summary>
    internal int MirrorCount => _mirrors.Count;

    /// <summary>Starts watching for changes and builds the first set of mirrors.</summary>
    internal void Start()
    {
        _tick.Start();
        RequestRebuild(RebuildReasons.Configuration);
    }

    /// <summary>
    /// Records that something invalidated the mirrors. The rebuild itself happens once the event
    /// stream goes quiet.
    /// </summary>
    /// <param name="reason">What happened.</param>
    internal void RequestRebuild(RebuildReasons reason)
    {
        _coalescer.Request(reason, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _tick.Stop();
        _tick.Tick -= OnTick;
        _coalescer.Clear();

        _trayWatcher.Dispose();
        _fullScreen.Changed -= OnFullScreenChanged;
        _fullScreen.Dispose();
        _relocator.FlyoutRelocated -= OnFlyoutRelocated;
        _relocator.Dispose();

        // Order matters. The thumbnails must be unregistered while their destination windows still
        // exist, because a closed window takes its registration's destination with it and leaves
        // the handle describing something that is no longer there.
        _thumbnails.Dispose();

        foreach (MirrorWindow mirror in _mirrors.Values)
        {
            mirror.MirrorInput -= OnMirrorInput;
            mirror.MirrorPressed -= OnMirrorPressed;
            mirror.MonitorDpiChanged -= OnMirrorDpiChanged;
            mirror.Close();
        }

        _mirrors.Clear();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_rebuildInFlight || !_coalescer.TryTake(DateTimeOffset.UtcNow, out RebuildReasons reasons))
        {
            return;
        }

        _rebuildInFlight = true;
        _ = RebuildAsync(reasons);
    }

    private void OnTrayStructureChanged()
    {
        // Arrives on a UI Automation worker thread. Nothing may touch a window from here.
        if (_disposed)
        {
            return;
        }

        _ = _dispatcher.InvokeAsync(
            () => RequestRebuild(RebuildReasons.TrayBoundaries),
            DispatcherPriority.Background);
    }

    private async Task RebuildAsync(RebuildReasons reasons)
    {
        try
        {
            // Phase 1, UI thread: read every window handle and rectangle. Win32 window calls belong
            // here and nowhere else.
            IReadOnlyList<MonitorInfo> monitors = MonitorEnumerator.Enumerate();
            MonitorSelection selection = MonitorTargets.Select(monitors, _config.Monitors);

            nint taskbar = TaskbarWindows.FindPrimary();
            PixelRect taskbarBounds = TaskbarWindows.GetVisibleBounds(taskbar);
            Dictionary<string, nint> secondaries = TaskbarWindows.FindSecondaries();

            var secondaryBounds = new Dictionary<string, PixelRect>(StringComparer.OrdinalIgnoreCase);
            foreach ((string device, nint window) in secondaries)
            {
                secondaryBounds[device] = TaskbarWindows.GetVisibleBounds(window);
            }

            foreach (string name in selection.UnmatchedNames)
            {
                _log.Write("config", $"The configured monitor \"{name}\" is not a connected secondary monitor, so nothing is mirrored there.");
            }

            if (taskbar == 0)
            {
                _log.Write("shell", "The primary taskbar does not exist. Mirrors are being torn down until Explorer brings it back.");
                TearDownAll();
                return;
            }

            if (taskbarBounds.IsEmpty)
            {
                // The taskbar is there but hidden, which is what a full-screen application on the
                // primary looks like. There is nothing to mirror while the source is off screen,
                // and leaving the strip up would float it over the full-screen application. The
                // windows and their registrations are kept, so coming back is just a placement.
                _log.Write("shell", "The primary taskbar is hidden, so the mirrors are hidden with it.");
                HideAllMirrors();
                return;
            }

            bool taskbarChanged = taskbar != _primaryTaskbar;
            _primaryTaskbar = taskbar;
            _primaryMonitorBounds = monitors.FirstOrDefault(m => m.IsPrimary)?.Bounds ?? PixelRect.Empty;

            // Phase 2, background thread: UI Automation only. Nothing here touches a window handle
            // owned by this process.
            ProbeResult probe = await Task.Run(() =>
                Probe(taskbar, taskbarBounds, selection, secondaries, secondaryBounds)).ConfigureAwait(true);

            if (taskbarChanged || reasons.HasFlag(RebuildReasons.TaskbarCreated))
            {
                await Task.Run(() => _trayWatcher.Subscribe(taskbar)).ConfigureAwait(true);
            }

            // Phase 3, back on the UI thread: windows and DWM.
            Apply(selection, probe, taskbar, taskbarBounds, secondaries, secondaryBounds);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _log.Write("rebuild", $"The rebuild failed at the interop layer: {ex.Message}");
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            _log.Write("rebuild", $"The rebuild failed inside a COM call: {ex.Message}");
        }
        finally
        {
            _rebuildInFlight = false;
        }
    }

    private ProbeResult Probe(
        nint taskbar,
        PixelRect taskbarBounds,
        MonitorSelection selection,
        Dictionary<string, nint> secondaries,
        Dictionary<string, PixelRect> secondaryBounds)
    {
        NativeMethods.EnsureDefaultDesktop();
        TrayBoundaries? boundaries = TrayBoundaryProbe.ResolvePrimary(taskbar, taskbarBounds, _log);

        var clocks = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        foreach (MonitorInfo monitor in selection.Targets)
        {
            if (secondaries.TryGetValue(monitor.DeviceName, out nint bar)
                && secondaryBounds.TryGetValue(monitor.DeviceName, out PixelRect bounds)
                && !bounds.IsEmpty)
            {
                clocks[monitor.DeviceName] = TrayBoundaryProbe.ResolveSecondaryClockLeft(bar, bounds, _log);
            }
            else
            {
                clocks[monitor.DeviceName] = null;
            }
        }

        return new ProbeResult(boundaries, clocks);
    }

    private void Apply(
        MonitorSelection selection,
        ProbeResult probe,
        nint taskbar,
        PixelRect taskbarBounds,
        Dictionary<string, nint> secondaries,
        Dictionary<string, PixelRect> secondaryBounds)
    {
        if (probe.Boundaries is not { } boundaries
            || !TrayGeometry.TryResolve(boundaries.NotificationAreaLeft, boundaries.ClockLeft, taskbarBounds, out TrayStrip resolved))
        {
            // Hidden, not torn down. A boundary read can fail transiently while the shell is
            // still settling, and destroying the mirrors would mean nothing brings them back until
            // the next shell event, which may be a long way off.
            _log.Write("rebuild", "The tray strip could not be resolved, so the mirrors are hidden for now.");
            HideAllMirrors();
            ScheduleStripRetry();
            return;
        }

        _strip = resolved;
        _stripRetries = 0;
        _primaryTaskbarBounds = taskbarBounds;

        // Release everything first. On an Explorer restart these handles are already dead, and on
        // every other path they are about to be replaced. One path, no special cases.
        _thumbnails.ReleaseAll();

        var wanted = new HashSet<string>(
            selection.Targets.Select(m => m.DeviceName),
            StringComparer.OrdinalIgnoreCase);

        foreach (string device in _mirrors.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            CloseMirror(device);
        }

        _monitorWorkAreas.Clear();

        foreach (MonitorInfo monitor in selection.Targets)
        {
            _monitorWorkAreas[monitor.DeviceName] = monitor.WorkArea;

            _ = secondaryBounds.TryGetValue(monitor.DeviceName, out PixelRect bar);
            probe.SecondaryClocks.TryGetValue(monitor.DeviceName, out int? clockLeft);

            // A full-screen application takes the whole screen, taskbar included. Nothing else
            // reports that: the taskbar window stays visible and keeps its rectangle, the shell
            // just lets it be covered. Measured on build 26200. A mirror is topmost, so without
            // this it would float over the full-screen application instead of being covered along
            // with the bar it belongs to.
            //
            // Only this monitor counts. A full-screen application on the primary deliberately
            // does not hide the mirror: watching a video full screen on one screen is exactly when
            // having the tray still readable on the other is worth something. The strip does become
            // a view of a tray the user cannot click through to until they leave full screen, and
            // that is the accepted trade.
            if (_fullScreen.IsFullScreen(monitor.DeviceName))
            {
                if (_mirrors.TryGetValue(monitor.DeviceName, out MirrorWindow? covered))
                {
                    covered.HideMirror();
                }

                continue;
            }

            // A monitor whose taskbar exists but is hidden gets no mirror until it comes back.
            // A monitor with no secondary taskbar at all is a different case: that user has turned
            // the feature off, and the mirror is anchored to the corner instead.
            if (secondaries.TryGetValue(monitor.DeviceName, out nint barHandle)
                && TaskbarWindows.IsPresentButHidden(barHandle))
            {
                if (_mirrors.TryGetValue(monitor.DeviceName, out MirrorWindow? hidden))
                {
                    hidden.HideMirror();
                }

                continue;
            }

            MonitorSettings settings = _config.SettingsFor(monitor.DeviceName);
            PixelRect placement = MirrorPlacement.Compute(
                _strip,
                monitor.Bounds,
                bar,
                clockLeft,
                settings.StripPaddingRight);

            if (placement.IsEmpty)
            {
                _log.Write("rebuild", $"No usable placement was found on {monitor.DeviceName}, so its mirror is hidden.");
                CloseMirror(monitor.DeviceName);
                continue;
            }

            MirrorWindow mirror = EnsureMirror(monitor.DeviceName);
            if (!mirror.PlaceAt(placement))
            {
                _log.Write("rebuild", $"The mirror window on {monitor.DeviceName} could not be positioned.");
                continue;
            }

            if (!_thumbnails.Register(monitor.DeviceName, mirror.Handle, taskbar))
            {
                continue;
            }

            _ = _thumbnails.Update(
                monitor.DeviceName,
                mirror.ClientBounds.IsEmpty ? mirror.Bounds : mirror.ClientBounds,
                _strip.SourceBounds,
                settings.Opacity);

            if (_log.IsEnabled)
            {
                _log.Write("rebuild", string.Create(
                    CultureInfo.InvariantCulture,
                    $"{monitor.DeviceName}: mirror at {placement}, clock left {clockLeft?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}, strip {_strip.ScreenBounds}."));
            }
        }
    }

    private MirrorWindow EnsureMirror(string deviceName)
    {
        if (_mirrors.TryGetValue(deviceName, out MirrorWindow? existing))
        {
            return existing;
        }

        var mirror = new MirrorWindow(deviceName);
        mirror.MirrorInput += OnMirrorInput;
        mirror.MirrorPressed += OnMirrorPressed;
        mirror.MonitorDpiChanged += OnMirrorDpiChanged;
        mirror.Show();

        _mirrors[deviceName] = mirror;
        return mirror;
    }

    private void CloseMirror(string deviceName)
    {
        // Unregister before the window goes away. A destroyed destination window leaves the
        // registration pointing at nothing, and the handle is then leaked for the session.
        _thumbnails.Release(deviceName);

        if (!_mirrors.Remove(deviceName, out MirrorWindow? mirror))
        {
            return;
        }

        mirror.MirrorInput -= OnMirrorInput;
        mirror.MirrorPressed -= OnMirrorPressed;
        mirror.MonitorDpiChanged -= OnMirrorDpiChanged;
        mirror.Close();
    }

    private void ScheduleStripRetry()
    {
        if (_disposed || _stripRetries >= MaxStripRetries)
        {
            if (_stripRetries >= MaxStripRetries)
            {
                _log.Write("rebuild", "The tray strip still cannot be resolved. Waiting for a shell event rather than retrying further.");
            }

            return;
        }

        _stripRetries++;

        var retry = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = _stripRetryDelay,
        };

        retry.Tick += OnRetry;
        retry.Start();

        void OnRetry(object? sender, EventArgs e)
        {
            retry.Stop();
            retry.Tick -= OnRetry;
            RequestRebuild(RebuildReasons.TrayBoundaries);
        }
    }

    private void HideAllMirrors()
    {
        foreach (MirrorWindow mirror in _mirrors.Values)
        {
            mirror.HideMirror();
        }
    }

    private void TearDownAll()
    {
        _thumbnails.ReleaseAll();

        foreach (string device in _mirrors.Keys.ToList())
        {
            CloseMirror(device);
        }
    }

    private void OnFlyoutRelocated(object? sender, MenuRelocator.FlyoutRelocatedEventArgs e)
    {
        _openFlyout = e.Window;
        _openFlyoutPrimaryX = e.PrimaryX;
    }

    private void OnMirrorPressed(object? sender, EventArgs e)
    {
        // Sampled on the press, not on the release. Some tray flyouts close themselves the instant
        // a click lands anywhere outside them, so by the time the button comes up the answer to
        // "was this flyout open" would already have changed to no, and the click would be routed
        // as an open.
        _flyoutOpenOnPress = IsOpenFlyoutVisible();
    }

    private bool IsOpenFlyoutVisible()
    {
        if (_openFlyout == 0 || !NativeMethods.IsWindow(_openFlyout) || !NativeMethods.IsWindowVisible(_openFlyout))
        {
            _openFlyout = 0;
            return false;
        }

        int hr = DwmApi.DwmGetWindowAttribute(_openFlyout, DwmApi.DwmwaCloaked, out int cloaked, sizeof(int));
        if (hr >= 0 && cloaked != 0)
        {
            _openFlyout = 0;
            return false;
        }

        return true;
    }

    private void DismissOpenFlyout()
    {
        // Handing the foreground back to the shell is exactly what clicking the real tray icon
        // does, and it is the signal the flyout is already listening for. Nothing is forced shut:
        // no WM_CLOSE, no ShowWindow on another process's window, so an application that keeps its
        // panel open on deactivation keeps it open here too.
        if (IsOpenFlyoutVisible() && _primaryTaskbar != 0)
        {
            _ = NativeMethods.SetForegroundWindow(_primaryTaskbar);
        }

        _log.Write("input", "A second click on an open flyout was treated as a dismissal rather than routed.");

        _openFlyout = 0;
        _flyoutOpenOnPress = false;
        _relocator.Disarm();
    }

    private void OnFullScreenChanged(object? sender, EventArgs e)
    {
        // Applied here and now, not left to the rebuild. A rebuild waits for the coalescer's quiet
        // window and then re-reads the tray over UI Automation, which puts half a second between
        // an application going full screen and the strip getting out of the way. Showing and
        // hiding needs none of that: the windows already exist and already know where they go.
        ApplyFullScreenVisibility();

        // Still request one, so that placement is refreshed against whatever else changed while a
        // full-screen application was in the way.
        RequestRebuild(RebuildReasons.DisplayChange);
    }

    private void ApplyFullScreenVisibility()
    {
        bool sourceReady = !TaskbarWindows.GetVisibleBounds(TaskbarWindows.FindPrimary()).IsEmpty;
        Dictionary<string, nint> secondaries = TaskbarWindows.FindSecondaries();

        foreach ((string device, MirrorWindow mirror) in _mirrors)
        {
            if (_fullScreen.IsFullScreen(device))
            {
                mirror.HideMirror();
                continue;
            }

            if (mirror.IsMirrorVisible || mirror.Bounds.IsEmpty || !sourceReady)
            {
                continue;
            }

            // Do not undo a hide that something else asked for. A mirror can also be hidden
            // because its own taskbar is away, and full screen ending says nothing about that.
            if (secondaries.TryGetValue(device, out nint bar) && TaskbarWindows.IsPresentButHidden(bar))
            {
                continue;
            }

            _ = mirror.PlaceAt(mirror.Bounds);
        }
    }

    private void OnMirrorDpiChanged(object? sender, EventArgs e)
    {
        RequestRebuild(RebuildReasons.DpiChange);
    }

    private void OnMirrorInput(object? sender, MirrorInputEventArgs e)
    {
        if (sender is not MirrorWindow mirror || _strip.Width <= 0)
        {
            return;
        }

        PixelRect bounds = mirror.ClientBounds.IsEmpty ? mirror.Bounds : mirror.ClientBounds;
        if (bounds.IsEmpty)
        {
            return;
        }

        (int x, int y) = HitTestTranslator.ToPrimaryScreen(_strip, bounds, e.Dx, e.Dy);

        // A second click on an icon whose flyout is already open means close, not open again.
        // Routing it would reach the tray icon after the application had hidden the flyout in
        // response to losing activation, and the icon handler would read that as "not showing" and
        // show it once more. The user sees the flyout vanish from the mirror, reappear on the
        // primary, and get relocated straight back, never closing. OneDrive guards against this
        // itself; kDrive, measured on build 26200, does not, and neither is something traymirror
        // can rely on.
        if (e.Kind != MirrorInputKind.Wheel
            && _flyoutOpenOnPress
            && Math.Abs(x - _openFlyoutPrimaryX) <= SameIconSlop)
        {
            DismissOpenFlyout();
            return;
        }

        _flyoutOpenOnPress = false;

        // Armed for every button, not just the right one. A left-click on a modern tray icon opens
        // a flyout just as a right-click does, and leaving those on the primary is the difference
        // between a mirror you can look at and a mirror you can use.
        if (e.Kind != MirrorInputKind.Wheel)
        {
            _ = _monitorWorkAreas.TryGetValue(mirror.DeviceName, out PixelRect workArea);
            _relocator.Arm(
                bounds,
                workArea,
                _primaryTaskbarBounds,
                _primaryMonitorBounds,
                (x, y),
                (bounds.Left + e.Dx, bounds.Top + e.Dy),
                _menuArmWindow);
        }

        // A left-click is an Invoke, not a synthesised click. UI Automation activates the real
        // tray icon without moving the cursor and without taking the foreground, which is what
        // makes the open-and-close behaviour of a flyout survive being driven from a mirror.
        // Everything else still goes through SendInput: Invoke is the element's default action, so
        // it has no right-click, middle-click or wheel equivalent.
        if (e.Kind == MirrorInputKind.LeftClick)
        {
            InvokeThenFallBack(x, y, e);
            return;
        }

        (int X, int Y)? origin = _router.Send(e.Kind, x, y, e.WheelDelta);
        if (origin is not { } previous)
        {
            _relocator.Disarm();
            return;
        }

        RestoreCursorShortly(previous);
    }

    private void InvokeThenFallBack(int x, int y, MirrorInputEventArgs e)
    {
        nint taskbar = _primaryTaskbar;

        // UI Automation calls block on cross-process round trips, so they never run on the UI
        // thread. The relocator is already armed, and it is driven by WinEvent callbacks that
        // arrive on the UI thread independently of this task.
        _ = Task.Run(() =>
        {
            if (TrayIconInvoker.TryInvokeAt(taskbar, x, y, _log))
            {
                return;
            }

            // The element could not be invoked: an icon that vanished mid-click, or a shell build
            // whose tray does not expose InvokePattern. Synthesising the click still works, so the
            // fallback keeps the mirror usable rather than silently doing nothing.
            _ = _dispatcher.InvokeAsync(() =>
            {
                (int X, int Y)? origin = _router.Send(e.Kind, x, y, e.WheelDelta);
                if (origin is { } previous)
                {
                    RestoreCursorShortly(previous);
                }
                else
                {
                    _relocator.Disarm();
                }
            });
        });
    }

    private void RestoreCursorShortly((int X, int Y) origin)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Input, _dispatcher)
        {
            Interval = _cursorRestoreDelay,
        };

        timer.Tick += Restore;
        timer.Start();

        void Restore(object? sender, EventArgs e)
        {
            timer.Stop();
            timer.Tick -= Restore;
            InputRouter.RestoreCursor(origin);
        }
    }

    private sealed record ProbeResult(
        TrayBoundaries? Boundaries,
        Dictionary<string, int?> SecondaryClocks);
}
