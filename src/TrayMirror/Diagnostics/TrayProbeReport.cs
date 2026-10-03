using System.Globalization;
using System.Text;
using TrayMirror.Core.Diagnostics;
using TrayMirror.Core.Geometry;
using TrayMirror.Core.Monitors;
using TrayMirror.Interop;
using TrayMirror.Shell;

namespace TrayMirror.Diagnostics;

/// <summary>
/// Builds the report printed by <c>traymirror --probe</c>.
/// </summary>
/// <remarks>
/// <para>
/// Boundary detection has to work against a XAML tray whose element names, automation identifiers
/// and nesting have changed between Windows feature updates and change again under a different
/// display language. When it goes wrong on a particular machine, the useful question is not "what
/// did traymirror decide" but "what is actually there", and this report answers that.
/// </para>
/// <para>
/// It is also what a bug report should carry. The measured geometry in the design document was
/// produced this way, and a report from another machine is directly comparable with it.
/// </para>
/// </remarks>
internal static class TrayProbeReport
{
    /// <summary>Builds the report.</summary>
    /// <returns>The report text.</returns>
    /// <remarks>Call from a background thread: it makes UI Automation calls.</remarks>
    internal static string Build()
    {
        IDiagnosticsSink log = NullDiagnosticsSink.Instance;
        var report = new StringBuilder();

        report.AppendLine("traymirror probe");
        report.AppendLine(CultureInfo.InvariantCulture, $"Taken at {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        report.AppendLine(CultureInfo.InvariantCulture, $"Windows {Environment.OSVersion.Version}");
        report.AppendLine();

        report.AppendLine("Monitors");
        foreach (MonitorInfo monitor in MonitorEnumerator.Enumerate())
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  {monitor.DeviceName}  {(monitor.IsPrimary ? "primary  " : "secondary")}  bounds {monitor.Bounds}  work {monitor.WorkArea}  dpi {monitor.Dpi.DpiX} ({monitor.Dpi.ScaleX * 100:0}%)");
        }

        report.AppendLine();

        nint primary = TaskbarWindows.FindPrimary();
        PixelRect primaryBounds = TaskbarWindows.GetVisibleBounds(primary);

        report.AppendLine("Primary taskbar");
        report.AppendLine(CultureInfo.InvariantCulture, $"  Shell_TrayWnd  handle {primary}  bounds {primaryBounds}");

        nint notifyArea = TaskbarWindows.FindNotifyArea(primary);
        report.AppendLine(CultureInfo.InvariantCulture,
            $"  TrayNotifyWnd  handle {notifyArea}  bounds {NativeMethods.GetWindowBounds(notifyArea)}");

        TrayBoundaries? boundaries = TrayBoundaryProbe.ResolvePrimary(primary, primaryBounds, log);
        if (boundaries is null)
        {
            report.AppendLine("  Boundaries could not be resolved.");
        }
        else
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  Notification area starts at x={boundaries.NotificationAreaLeft}, clock starts at x={boundaries.ClockLeft}, resolved via {boundaries.Method}");

            if (TrayGeometry.TryResolve(boundaries.NotificationAreaLeft, boundaries.ClockLeft, primaryBounds, out TrayStrip strip))
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"  Strip on screen {strip.ScreenBounds}");
                report.AppendLine(CultureInfo.InvariantCulture, $"  Strip as rcSource {strip.SourceBounds}");
            }
        }

        report.AppendLine();
        AppendTree(report, "Primary taskbar elements", primary, log);

        foreach ((string device, nint window) in TaskbarWindows.FindSecondaries())
        {
            PixelRect bounds = TaskbarWindows.GetVisibleBounds(window);
            report.AppendLine();
            report.AppendLine(CultureInfo.InvariantCulture, $"Secondary taskbar on {device}  handle {window}  bounds {bounds}");

            int? clock = TrayBoundaryProbe.ResolveSecondaryClockLeft(window, bounds, log);
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  Clock starts at x={clock?.ToString(CultureInfo.InvariantCulture) ?? "not found"}");

            AppendTree(report, $"Elements on {device}", window, log);
        }

        return report.ToString();
    }

    private static void AppendTree(StringBuilder report, string heading, nint taskbar, IDiagnosticsSink log)
    {
        report.AppendLine(heading);

        IReadOnlyList<TrayElement> elements = TrayBoundaryProbe.Snapshot(taskbar, log);
        if (elements.Count == 0)
        {
            report.AppendLine("  (no elements)");
            return;
        }

        foreach (TrayElement element in elements.OrderBy(e => e.Bounds.Left))
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  x={element.Bounds.Left,6} w={element.Bounds.Width,4}  {Trim(element.ControlType, 28),-28}  id={Trim(element.AutomationId, 24),-24}  class={Trim(element.ClassName, 24),-24}  name={Trim(element.Name, 48)}");
        }
    }

    private static string Trim(string value, int width)
    {
        if (value.Length <= width)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, width - 1), "…");
    }
}
