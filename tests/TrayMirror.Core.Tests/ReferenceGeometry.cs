using TrayMirror.Core.Geometry;

namespace TrayMirror.Core.Tests;

/// <summary>
/// The arrangement measured on the development machine on 2026-08-10 and recorded in
/// docs/specs/2026-08-10-traymirror-design.md.
/// </summary>
/// <remarks>
/// Tests are written against these numbers rather than against invented ones so that a failure
/// here means the code no longer reproduces a placement that was verified by eye on real hardware,
/// not merely that it disagrees with an assumption someone made while writing a test.
/// </remarks>
public static class ReferenceGeometry
{
    /// <summary>The primary monitor, 3440x1440 at the origin.</summary>
    public static PixelRect PrimaryMonitor => new(0, 0, 3440, 1440);

    /// <summary>The primary taskbar, 48 physical pixels tall.</summary>
    public static PixelRect PrimaryTaskbar => new(0, 1392, 3440, 1440);

    /// <summary>The secondary monitor, 2560x1440 immediately right of the primary.</summary>
    public static PixelRect SecondaryMonitor => new(3440, 0, 6000, 1440);

    /// <summary>The secondary taskbar.</summary>
    public static PixelRect SecondaryTaskbar => new(3440, 1392, 6000, 1440);

    /// <summary>Left edge of the overflow chevron, the first notification-area element.</summary>
    public const int NotificationAreaLeft = 3156;

    /// <summary>Left edge of the primary clock.</summary>
    public const int ClockLeft = 3340;

    /// <summary>Left edge of the secondary clock, which mirrors are anchored to.</summary>
    public const int SecondaryClockLeft = 5900;

    /// <summary>The strip resolved from the readings above.</summary>
    public static TrayStrip Strip => TrayGeometry.Resolve(NotificationAreaLeft, ClockLeft, PrimaryTaskbar);
}
