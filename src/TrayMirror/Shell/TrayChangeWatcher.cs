using System.Runtime.InteropServices;
using System.Windows.Automation;
using TrayMirror.Core.Diagnostics;
using TrayMirror.Interop;

namespace TrayMirror.Shell;

/// <summary>
/// Notices when the primary tray gains or loses an icon, so the crop can be recomputed.
/// </summary>
/// <remarks>
/// <para>
/// This subscribes to UI Automation's structure-changed event on the primary taskbar's subtree.
/// It is a subscription, not a poll: Explorer raises the event when its own tree changes, and
/// nothing here runs in between. That distinction matters, because the alternative (checking the
/// boundaries on a timer) would burn power on a machine whose tray never changes and would still
/// miss changes that happen between ticks.
/// </para>
/// <para>
/// Events arrive on a UI Automation worker thread, never on the UI thread, so the handler does
/// nothing except hand a rebuild request to the caller's callback. The caller is responsible for
/// marshalling to the UI thread before touching a window.
/// </para>
/// </remarks>
internal sealed class TrayChangeWatcher : IDisposable
{
    private readonly IDiagnosticsSink _log;
    private readonly Action _onChanged;
    private StructureChangedEventHandler? _handler;
    private AutomationElement? _subscribedTo;

    /// <summary>
    /// Initialises a new instance of the <see cref="TrayChangeWatcher"/> class.
    /// </summary>
    /// <param name="log">Where to record subscription failures.</param>
    /// <param name="onChanged">
    /// Invoked on a UI Automation thread whenever the tray's element tree changes.
    /// </param>
    internal TrayChangeWatcher(IDiagnosticsSink log, Action onChanged)
    {
        _log = log;
        _onChanged = onChanged;
    }

    /// <summary>
    /// Subscribes to the current primary taskbar, replacing any previous subscription.
    /// </summary>
    /// <param name="primaryTaskbar">The <c>Shell_TrayWnd</c> window handle.</param>
    /// <remarks>
    /// Call from a background thread, and call it again after every Explorer restart: the old
    /// element belongs to a window that no longer exists.
    /// </remarks>
    internal void Subscribe(nint primaryTaskbar)
    {
        Unsubscribe();

        if (primaryTaskbar == 0)
        {
            return;
        }

        NativeMethods.EnsureDefaultDesktop();

        try
        {
            var root = AutomationElement.FromHandle(primaryTaskbar);
            if (root is null)
            {
                return;
            }

            _handler = OnStructureChanged;
            Automation.AddStructureChangedEventHandler(root, TreeScope.Subtree, _handler);
            _subscribedTo = root;
        }
        catch (ElementNotAvailableException)
        {
            _log.Write("uia", "The taskbar disappeared before a structure-changed subscription could be made.");
        }
        catch (COMException ex)
        {
            _log.Write("uia", $"The structure-changed subscription failed: {ex.Message}");
        }
    }

    /// <summary>Removes the current subscription, if any.</summary>
    internal void Unsubscribe()
    {
        if (_handler is null || _subscribedTo is null)
        {
            return;
        }

        try
        {
            Automation.RemoveStructureChangedEventHandler(_subscribedTo, _handler);
        }
        catch (ElementNotAvailableException)
        {
            // The element is already gone, which means the subscription is too.
        }
        catch (COMException)
        {
            // Same reasoning. There is nothing left to unsubscribe from.
        }
        finally
        {
            _handler = null;
            _subscribedTo = null;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Unsubscribe();
    }

    private void OnStructureChanged(object sender, StructureChangedEventArgs e)
    {
        _onChanged();
    }
}
