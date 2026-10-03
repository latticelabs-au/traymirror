using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using TrayMirror.Core.Diagnostics;
using TrayMirror.Core.Geometry;
using TrayMirror.Interop;

namespace TrayMirror.Shell;

/// <summary>
/// Locates the two crop boundaries on the primary taskbar, and the clock on each secondary one.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of traymirror's use of UI Automation. UIA answers two questions, both of the
/// form "where does this thing start". It does not hit test, and it does not render. Rendering is
/// DWM compositing the real pixels, and hit-testing is coordinate arithmetic in
/// <c>TrayMirror.Core</c>.
/// </para>
/// <para>
/// <strong>Threading.</strong> Every method here makes cross-process COM calls into Explorer's UIA
/// provider and can block for tens to hundreds of milliseconds. They must be called from a
/// background thread. Calling them on the UI thread stalls the message pump, which delays
/// <c>WM_PAINT</c>, input handling and DWM frame callbacks, and is visible to the user as the
/// mirrors freezing.
/// </para>
/// <para>
/// <strong>Why heuristics rather than one fixed query.</strong> The Windows 11 tray is a XAML
/// surface whose element names, automation identifiers and nesting have all changed across feature
/// updates, and they differ again under a non-English display language. So each boundary is
/// resolved by an ordered list of strategies, from the most specific to the most general, and the
/// strategy that produced the answer is recorded in <see cref="TrayBoundaries.Method"/> so a bug
/// report says which one fired. Run the application with <c>--probe</c> to dump the tree this
/// machine actually has.
/// </para>
/// </remarks>
internal static partial class TrayBoundaryProbe
{
    private const int MaxElements = 512;

    /// <summary>
    /// Reads the primary taskbar's tray boundaries.
    /// </summary>
    /// <param name="primaryTaskbar">The <c>Shell_TrayWnd</c> window handle.</param>
    /// <param name="taskbarBounds">That window's screen rectangle.</param>
    /// <param name="log">Where to record what was found.</param>
    /// <returns>The boundaries, or <see langword="null"/> when they could not be resolved.</returns>
    /// <remarks>Call from a background thread.</remarks>
    internal static TrayBoundaries? ResolvePrimary(
        nint primaryTaskbar,
        PixelRect taskbarBounds,
        IDiagnosticsSink log)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (primaryTaskbar == 0 || taskbarBounds.IsEmpty)
        {
            return null;
        }

        IReadOnlyList<TrayElement> elements = Snapshot(primaryTaskbar, log);
        if (elements.Count == 0)
        {
            return null;
        }

        int? clockLeft = FindClockLeft(elements, taskbarBounds);
        if (clockLeft is not { } clock)
        {
            log.Write("uia", "No clock element was found on the primary taskbar.");
            return null;
        }

        PixelRect legacy = NativeMethods.GetWindowBounds(TaskbarWindows.FindNotifyArea(primaryTaskbar));
        int? hardFloor = (!legacy.IsEmpty && legacy.Left < clock) ? legacy.Left : null;

        (int? left, string method) = FindNotificationAreaLeft(elements, taskbarBounds, clock, hardFloor);

        if (left is null && hardFloor is not null)
        {
            left = hardFloor;
            method = "TrayNotifyWnd";
        }
        else if (left is not null && hardFloor is not null && left.Value < hardFloor.Value)
        {
            left = hardFloor;
            method = "TrayNotifyWnd (clamped)";
        }

        if (left is not { } notificationArea)
        {
            log.Write("uia", "No notification-area boundary could be resolved on the primary taskbar.");
            return null;
        }

        var boundaries = new TrayBoundaries(notificationArea, clock, method);
        if (log.IsEnabled)
        {
            log.Write("uia", string.Create(
                CultureInfo.InvariantCulture,
                $"Primary boundaries: notification area at {notificationArea}, clock at {clock}, via {method}. Taskbar {taskbarBounds}."));
        }

        return boundaries;
    }

    /// <summary>
    /// Reads the left edge of a secondary taskbar's clock, which is what mirrors are anchored to.
    /// </summary>
    /// <param name="secondaryTaskbar">The <c>Shell_SecondaryTrayWnd</c> window handle.</param>
    /// <param name="taskbarBounds">That window's screen rectangle.</param>
    /// <param name="log">Where to record what was found.</param>
    /// <returns>The screen x of the clock's left edge, or <see langword="null"/>.</returns>
    /// <remarks>Call from a background thread.</remarks>
    internal static int? ResolveSecondaryClockLeft(
        nint secondaryTaskbar,
        PixelRect taskbarBounds,
        IDiagnosticsSink log)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (secondaryTaskbar == 0 || taskbarBounds.IsEmpty)
        {
            return null;
        }

        IReadOnlyList<TrayElement> elements = Snapshot(secondaryTaskbar, log);
        return elements.Count == 0 ? null : FindClockLeft(elements, taskbarBounds);
    }

    /// <summary>
    /// Reads a taskbar's automation tree into a flat, already-cached list.
    /// </summary>
    /// <param name="taskbar">The taskbar window handle.</param>
    /// <param name="log">Where to record failures.</param>
    /// <returns>The elements, or an empty list when the tree could not be read.</returns>
    /// <remarks>
    /// One cached <c>FindAll</c> rather than a walk. A tree walk costs a cross-process round trip
    /// per node and per property, which on a busy tray is hundreds of round trips; the cache
    /// request collapses that into a single call.
    /// </remarks>
    internal static IReadOnlyList<TrayElement> Snapshot(nint taskbar, IDiagnosticsSink log)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (taskbar == 0)
        {
            return [];
        }

        NativeMethods.EnsureDefaultDesktop();

        try
        {
            var root = AutomationElement.FromHandle(taskbar);
            if (root is null)
            {
                return [];
            }

            var request = new CacheRequest
            {
                TreeScope = TreeScope.Element | TreeScope.Descendants,
                AutomationElementMode = AutomationElementMode.None,
            };

            request.Add(AutomationElement.NameProperty);
            request.Add(AutomationElement.AutomationIdProperty);
            request.Add(AutomationElement.ClassNameProperty);
            request.Add(AutomationElement.ControlTypeProperty);
            request.Add(AutomationElement.BoundingRectangleProperty);

            var collected = new List<TrayElement>();

            using (request.Activate())
            {
                AutomationElementCollection all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);

                foreach (AutomationElement? element in all)
                {
                    if (element is null || collected.Count >= MaxElements)
                    {
                        break;
                    }

                    TrayElement? described = Describe(element);
                    if (described is not null)
                    {
                        collected.Add(described);
                    }
                }
            }

            return collected;
        }
        catch (ElementNotAvailableException)
        {
            // Explorer restarted while the tree was being read. The caller retries after the
            // TaskbarCreated message arrives.
            log.Write("uia", "The taskbar element disappeared while its tree was being read.");
            return [];
        }
        catch (COMException ex)
        {
            log.Write("uia", $"The UI Automation provider failed: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Picks the clock out of a taskbar's elements.
    /// </summary>
    /// <param name="elements">The taskbar's elements.</param>
    /// <param name="taskbarBounds">The taskbar rectangle.</param>
    /// <returns>The screen x of the clock's left edge, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Ordered from most specific to most general. The automation identifier is stable when it is
    /// present at all; the name pattern is the fallback that survives a shell update but not a
    /// display language whose digits are not ASCII, and the positional guess is the last resort.
    /// </remarks>
    internal static int? FindClockLeft(IReadOnlyList<TrayElement> elements, PixelRect taskbarBounds)
    {
        ArgumentNullException.ThrowIfNull(elements);

        int midpoint = taskbarBounds.Left + (taskbarBounds.Width / 2);

        List<TrayElement> candidates = [.. elements
            .Where(e => !e.Bounds.IsEmpty && e.Bounds.Left >= midpoint && e.Bounds.Right <= taskbarBounds.Right + 1)
            .OrderBy(e => e.Bounds.Left)];

        if (candidates.Count == 0)
        {
            return null;
        }

        TrayElement? byId = candidates.FirstOrDefault(
            e => e.AutomationId.Contains("Clock", StringComparison.OrdinalIgnoreCase)
                 || e.ClassName.Contains("Clock", StringComparison.OrdinalIgnoreCase));

        if (byId is not null)
        {
            return byId.Bounds.Left;
        }

        TrayElement? byName = candidates.FirstOrDefault(e => TimePattern().IsMatch(e.Name));
        return byName?.Bounds.Left;
    }

    /// <summary>
    /// Finds where the notification area starts, which is the left crop boundary.
    /// </summary>
    /// <param name="elements">The primary taskbar's elements.</param>
    /// <param name="taskbarBounds">The taskbar rectangle.</param>
    /// <param name="clockLeft">The already-resolved left edge of the clock.</param>
    /// <param name="hardFloor">An optional minimum screen x boundary below which icons cannot belong to the notification area.</param>
    /// <returns>The boundary and the name of the strategy that produced it.</returns>
    /// <remarks>
    /// <para>
    /// The general strategy walks left from the clock across the contiguous run of icon-sized
    /// elements. That run is what the notification area is: on the reference machine the six
    /// missing items sit at a 32px pitch with no gaps, and the first real gap going left is the
    /// space before the centred task buttons. Stopping at that gap is what separates the strip
    /// Windows omits from the task buttons it already draws correctly on every monitor.
    /// </para>
    /// </remarks>
    internal static (int? Left, string Method) FindNotificationAreaLeft(
        IReadOnlyList<TrayElement> elements,
        PixelRect taskbarBounds,
        int clockLeft,
        int? hardFloor = null)
    {
        ArgumentNullException.ThrowIfNull(elements);

        int height = Math.Max(taskbarBounds.Height, 1);
        int maxIconWidth = height * 4;
        int maxGap = Math.Max(height / 2, 12);
        int minLeft = hardFloor ?? (taskbarBounds.Left + (taskbarBounds.Width / 4));

        TrayElement? container = elements.FirstOrDefault(
            e => !e.Bounds.IsEmpty
                 && e.Bounds.Left < clockLeft
                 && e.Bounds.Left >= minLeft
                 && e.Bounds.Width > maxIconWidth
                 && (e.AutomationId.Contains("NotifyItems", StringComparison.OrdinalIgnoreCase)
                     || e.Name.Contains("Notification Area", StringComparison.OrdinalIgnoreCase)
                     || e.Name.Contains("User Promoted", StringComparison.OrdinalIgnoreCase)));

        if (container is not null)
        {
            return (container.Bounds.Left, "UIA container");
        }

        List<TrayElement> run = [.. elements
            .Where(e => !e.Bounds.IsEmpty
                        && e.Bounds.Right <= clockLeft + 1
                        && e.Bounds.Left >= taskbarBounds.Left
                        && e.Bounds.Width > 0
                        && e.Bounds.Width <= maxIconWidth)
            .OrderBy(e => e.Bounds.Left)];

        if (run.Count == 0)
        {
            return (null, "none");
        }

        int left = run[^1].Bounds.Left;
        int rightOfPrevious = run[^1].Bounds.Left;

        for (int i = run.Count - 2; i >= 0; i--)
        {
            PixelRect bounds = run[i].Bounds;
            if (bounds.Left < minLeft || bounds.Right < rightOfPrevious - maxGap)
            {
                break;
            }

            left = Math.Min(left, bounds.Left);
            rightOfPrevious = Math.Min(rightOfPrevious, bounds.Left);
        }

        return left < clockLeft ? (left, "UIA contiguous run") : (null, "none");
    }

    private static TrayElement? Describe(AutomationElement element)
    {
        try
        {
            var bounds = (System.Windows.Rect)element.GetCachedPropertyValue(
                AutomationElement.BoundingRectangleProperty);

            if (bounds.IsEmpty || double.IsInfinity(bounds.Width) || double.IsInfinity(bounds.Height))
            {
                return null;
            }

            var controlType = element.GetCachedPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;

            return new TrayElement(
                element.GetCachedPropertyValue(AutomationElement.NameProperty) as string ?? string.Empty,
                element.GetCachedPropertyValue(AutomationElement.AutomationIdProperty) as string ?? string.Empty,
                element.GetCachedPropertyValue(AutomationElement.ClassNameProperty) as string ?? string.Empty,
                controlType?.ProgrammaticName ?? string.Empty,
                new PixelRect(
                    (int)Math.Round(bounds.Left, MidpointRounding.AwayFromZero),
                    (int)Math.Round(bounds.Top, MidpointRounding.AwayFromZero),
                    (int)Math.Round(bounds.Right, MidpointRounding.AwayFromZero),
                    (int)Math.Round(bounds.Bottom, MidpointRounding.AwayFromZero)));
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // A cached property was not requested or the element does not publish it.
            return null;
        }
    }

    [GeneratedRegex(@"\d{1,2}[:.꞉]\d{2}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex TimePattern();
}

/// <summary>
/// The two crop boundaries on the primary taskbar.
/// </summary>
/// <param name="NotificationAreaLeft">Screen x of the left edge of the first tray element.</param>
/// <param name="ClockLeft">Screen x of the left edge of the clock.</param>
/// <param name="Method">
/// Which resolution strategy produced the notification-area boundary. Recorded so a bug report
/// distinguishes a wrong reading from a missing one.
/// </param>
internal sealed record TrayBoundaries(int NotificationAreaLeft, int ClockLeft, string Method);
