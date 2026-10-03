using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using TrayMirror.Core.Diagnostics;
using TrayMirror.Interop;

namespace TrayMirror.Shell;

/// <summary>
/// Activates a tray icon through UI Automation instead of synthesising a mouse click on it.
/// </summary>
/// <remarks>
/// <para>
/// Every button in the Windows 11 tray exposes <c>InvokePattern</c>. Measured on build 26200, on
/// all ten elements from the overflow chevron to Show Desktop. Invoking one opens its flyout
/// exactly as a click does, and does it without moving the cursor and without taking the
/// foreground, which is the whole reason this exists.
/// </para>
/// <para>
/// <strong>What replaced what, and why.</strong> Routing a click with <c>SendInput</c> meant
/// warping the cursor to the primary monitor, clicking, and putting it back. That was visible, it
/// failed outright whenever another application had the cursor clipped (a full-screen game, a
/// remote-desktop host), and it broke the open-and-close behaviour of tray flyouts: the
/// synthesised click activated the taskbar, the owning application hid its flyout on losing
/// activation, and its icon handler then read the flyout as closed and opened it again.
/// </para>
/// <para>
/// <strong>Invoke opens, it does not close.</strong> Measured on kDrive, build 26200: from a closed
/// panel, <c>Invoke</c> opens it; a second <c>Invoke</c> leaves it open. Closing is a separate
/// mechanism, handing the foreground back to the shell, which the controller does. Do not expect
/// this method to toggle.
/// </para>
/// <para>
/// <strong>Threading.</strong> These are cross-process UI Automation calls. They must not run on
/// the UI thread. See the thread rules in CLAUDE.md.
/// </para>
/// </remarks>
internal static class TrayIconInvoker
{
    /// <summary>
    /// The widest element still treated as an icon rather than as a container.
    /// </summary>
    /// <remarks>
    /// Tray icons measure 28 to 44 physical pixels on the reference machine and the clock measures
    /// 74. The containers that also enclose the point run the full width of the taskbar, so any
    /// generous cap separates the two. Choosing the smallest match does the real work; this only
    /// stops a pathological tree from offering a 1920 pixel wide icon.
    /// </remarks>
    private const int MaxIconWidth = 200;

    /// <summary>
    /// Finds the tray element at a screen point and invokes it.
    /// </summary>
    /// <param name="taskbar">The primary taskbar window, which is the root of the search.</param>
    /// <param name="primaryX">Screen x on the primary taskbar, from the hit-test translation.</param>
    /// <param name="primaryY">Screen y on the primary taskbar.</param>
    /// <param name="log">Where to record what was invoked.</param>
    /// <returns>
    /// <see langword="true"/> when an element was found and invoked. <see langword="false"/> means
    /// the caller should fall back to synthesising a click.
    /// </returns>
    /// <remarks>Call from a background thread.</remarks>
    internal static bool TryInvokeAt(nint taskbar, int primaryX, int primaryY, IDiagnosticsSink log)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (taskbar == 0)
        {
            return false;
        }

        NativeMethods.EnsureDefaultDesktop();

        try
        {
            // A tree search, not AutomationElement.FromPoint. Measured on build 26200: FromPoint
            // over a tray icon returns only the Shell_TrayWnd pane and its desktop parent, neither
            // of which supports any pattern. The XAML tray provider does not implement hit testing
            // from a point, so the element has to be found by walking the tree and comparing
            // rectangles instead.
            var root = AutomationElement.FromHandle(taskbar);
            if (root is null)
            {
                return false;
            }

            AutomationElementCollection all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);

            AutomationElement? best = null;
            InvokePattern? bestPattern = null;
            double bestWidth = double.MaxValue;

            foreach (AutomationElement? element in all)
            {
                if (element is null)
                {
                    continue;
                }

                System.Windows.Rect bounds = element.Current.BoundingRectangle;
                if (bounds.IsEmpty
                    || bounds.Width > MaxIconWidth
                    || bounds.Width >= bestWidth
                    || !bounds.Contains(primaryX, primaryY))
                {
                    continue;
                }

                // Smallest enclosing element that can actually be invoked. Smallest alone is not
                // enough: measured on build 26200, each tray button contains a 16x16 Image child
                // that encloses the same point and supports no pattern at all, so selecting purely
                // on size lands on the image and the invoke never happens.
                if (element.TryGetCurrentPattern(InvokePattern.Pattern, out object? pattern)
                    && pattern is InvokePattern candidate)
                {
                    best = element;
                    bestPattern = candidate;
                    bestWidth = bounds.Width;
                }
            }

            if (best is null || bestPattern is null)
            {
                log.Write("uia", string.Create(
                    CultureInfo.InvariantCulture,
                    $"No invokable tray element encloses ({primaryX},{primaryY}), so the click is being synthesised instead."));
                return false;
            }

            string name = best.Current.Name ?? string.Empty;
            bestPattern.Invoke();

            log.Write("uia", string.Create(
                CultureInfo.InvariantCulture,
                $"Invoked the tray element at ({primaryX},{primaryY}): \"{Summarise(name)}\"."));

            return true;
        }
        catch (ElementNotAvailableException)
        {
            // The tray changed between the hit test and the invoke, which an Explorer restart or a
            // vanishing icon both look like. The caller falls back.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (COMException ex)
        {
            log.Write("uia", $"Invoking the tray element failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Trims a tray icon name to one readable line for the log.</summary>
    /// <param name="name">The raw automation name, which often carries several lines of status.</param>
    /// <returns>A single-line summary.</returns>
    private static string Summarise(string name)
    {
        string flattened = name.ReplaceLineEndings(" ").Trim();
        return flattened.Length <= 60 ? flattened : string.Concat(flattened.AsSpan(0, 59), "…");
    }
}
