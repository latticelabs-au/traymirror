namespace TrayMirror.Core.Scheduling;

/// <summary>
/// Why a rebuild was requested. Values combine, because one physical event usually raises several.
/// </summary>
[Flags]
public enum RebuildReasons
{
    /// <summary>Nothing is pending.</summary>
    None = 0,

    /// <summary>Explorer restarted and broadcast the registered <c>TaskbarCreated</c> message.</summary>
    TaskbarCreated = 1,

    /// <summary>A monitor was added, removed or reconfigured.</summary>
    DisplayChange = 2,

    /// <summary>A monitor's DPI changed.</summary>
    DpiChange = 4,

    /// <summary>The tray strip boundaries moved, usually because an icon appeared or vanished.</summary>
    TrayBoundaries = 8,

    /// <summary>The configuration file changed or was reloaded from the tray menu.</summary>
    Configuration = 16,
}

/// <summary>
/// Collapses a burst of invalidating events into a single rebuild.
/// </summary>
/// <remarks>
/// <para>
/// Unplugging a monitor produces several <c>WM_DISPLAYCHANGE</c> messages, and an Explorer restart
/// produces a <c>TaskbarCreated</c> broadcast followed by display and DPI traffic as the shell
/// settles. Rebuilding on each one would tear down and re-register DWM thumbnails repeatedly
/// against a taskbar that is still moving, which is both wasteful and the easiest way to end up
/// holding a handle to a window that no longer exists.
/// </para>
/// <para>
/// This type holds no timer of its own. The caller supplies the current time, so the quiet-window
/// logic is tested directly rather than by sleeping. The WPF shell drives it from a dispatcher
/// timer on the UI thread.
/// </para>
/// </remarks>
public sealed class RebuildCoalescer
{
    private DateTimeOffset _lastRequest;

    /// <summary>
    /// Initialises a new instance of the <see cref="RebuildCoalescer"/> class.
    /// </summary>
    /// <param name="quietWindow">
    /// How long the event stream must stay quiet before the rebuild is released.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="quietWindow"/> is negative.
    /// </exception>
    public RebuildCoalescer(TimeSpan quietWindow)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(quietWindow, TimeSpan.Zero);
        QuietWindow = quietWindow;
    }

    /// <summary>Gets the quiet period that must elapse before a rebuild is released.</summary>
    public TimeSpan QuietWindow { get; }

    /// <summary>Gets the reasons accumulated since the last rebuild was taken.</summary>
    public RebuildReasons Pending { get; private set; }

    /// <summary>Gets a value indicating whether a rebuild is waiting to be taken.</summary>
    public bool HasPending => Pending != RebuildReasons.None;

    /// <summary>
    /// Records an invalidating event and restarts the quiet window.
    /// </summary>
    /// <param name="reason">Why the rebuild is needed.</param>
    /// <param name="now">The current time.</param>
    public void Request(RebuildReasons reason, DateTimeOffset now)
    {
        if (reason == RebuildReasons.None)
        {
            return;
        }

        Pending |= reason;
        _lastRequest = now;
    }

    /// <summary>
    /// Releases the accumulated rebuild once the event stream has been quiet for the whole window.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="reasons">The accumulated reasons, when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the caller should rebuild now.</returns>
    public bool TryTake(DateTimeOffset now, out RebuildReasons reasons)
    {
        reasons = RebuildReasons.None;

        if (!HasPending || now - _lastRequest < QuietWindow)
        {
            return false;
        }

        reasons = Pending;
        Pending = RebuildReasons.None;
        return true;
    }

    /// <summary>
    /// Discards anything pending, for use when the application is shutting down and a rebuild
    /// would only register handles that are about to be released.
    /// </summary>
    public void Clear()
    {
        Pending = RebuildReasons.None;
    }
}
