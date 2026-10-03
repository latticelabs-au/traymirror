using System.ComponentModel;
using System.Globalization;
using TrayMirror.Core.Diagnostics;
using TrayMirror.Core.Geometry;
using TrayMirror.Interop;

namespace TrayMirror.Mirroring;

/// <summary>
/// Owns every DWM thumbnail registration in the process, one per mirror window.
/// </summary>
/// <remarks>
/// <para>
/// A thumbnail handle is released on four paths and there is no fifth: the monitor goes away, the
/// taskbar is recreated, a display or DPI change forces a rebuild, and the application shuts down.
/// Every one of those funnels through <see cref="Release"/> or <see cref="ReleaseAll"/>, so the
/// bookkeeping lives in one place rather than being repeated at each call site with slightly
/// different coverage.
/// </para>
/// <para>
/// On an Explorer restart the old handles are already dead, because DWM destroyed them along with
/// <c>Shell_TrayWnd</c>. They are still unregistered explicitly. Calling
/// <c>DwmUnregisterThumbnail</c> on a stale handle is harmless and returns a non-fatal HRESULT;
/// skipping it leaves this dictionary describing thumbnails that no longer exist, and the next
/// rebuild then quietly does nothing.
/// </para>
/// <para>All members must be called on the UI thread.</para>
/// </remarks>
internal sealed class ThumbnailHost(IDiagnosticsSink log) : IDisposable
{
    private readonly Dictionary<string, SafeDwmThumbnailHandle> _handles =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IDiagnosticsSink _log = log;

    /// <summary>Gets the number of live registrations.</summary>
    internal int Count => _handles.Count;

    /// <summary>
    /// Registers a thumbnail for one mirror, replacing any registration already held for it.
    /// </summary>
    /// <param name="key">The monitor device name this mirror belongs to.</param>
    /// <param name="destination">The mirror window handle.</param>
    /// <param name="source">The source window handle, normally <c>Shell_TrayWnd</c>.</param>
    /// <returns><see langword="true"/> when DWM accepted the registration.</returns>
    internal bool Register(string key, nint destination, nint source)
    {
        ArgumentNullException.ThrowIfNull(key);

        Release(key);

        if (destination == 0 || source == 0)
        {
            return false;
        }

        try
        {
            _handles[key] = SafeDwmThumbnailHandle.Register(destination, source);
            return true;
        }
        catch (Win32Exception ex)
        {
            // The usual cause is a source window destroyed between being found and being
            // registered, which is exactly what an Explorer restart looks like from here.
            _log.Write("dwm", $"DwmRegisterThumbnail failed for {key}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Applies the crop, placement, opacity and visibility of one mirror's thumbnail.
    /// </summary>
    /// <param name="key">The monitor device name.</param>
    /// <param name="destinationClient">
    /// The mirror's client rectangle, in screen coordinates. It is converted to client-relative
    /// coordinates here.
    /// </param>
    /// <param name="source">
    /// The crop rectangle, already expressed relative to the source window's origin by
    /// <see cref="TrayGeometry"/>.
    /// </param>
    /// <param name="opacity">The mirror opacity, from 0.0 to 1.0.</param>
    /// <returns><see langword="true"/> when DWM accepted the update.</returns>
    internal bool Update(string key, PixelRect destinationClient, PixelRect source, double opacity)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!_handles.TryGetValue(key, out SafeDwmThumbnailHandle? handle) || destinationClient.IsEmpty)
        {
            return false;
        }

        var properties = new DwmApi.DwmThumbnailProperties
        {
            DwFlags = DwmApi.DwmTnpRectDestination
                      | DwmApi.DwmTnpRectSource
                      | DwmApi.DwmTnpOpacity
                      | DwmApi.DwmTnpVisible
                      | DwmApi.DwmTnpSourceClientAreaOnly,
            RcDestination = NativeRect.From(
                PixelRect.FromSize(0, 0, destinationClient.Width, destinationClient.Height)),
            RcSource = NativeRect.From(source),
            Opacity = (byte)Math.Clamp(Math.Round(opacity * 255.0, MidpointRounding.AwayFromZero), 0, 255),
            FVisible = true,

            // The whole window, not just its client area. Shell_TrayWnd's client origin and window
            // origin are not the same point, and the crop rectangle is computed against the window
            // rectangle, so asking DWM for client-area-only pixels would shift the strip.
            FSourceClientAreaOnly = false,
        };

        try
        {
            handle.Update(ref properties);

            if (_log.IsEnabled)
            {
                _log.Write("dwm", string.Create(
                    CultureInfo.InvariantCulture,
                    $"Updated {key}: source {source}, destination {destinationClient.Width}x{destinationClient.Height}, opacity {opacity:0.00}."));
            }

            return true;
        }
        catch (Win32Exception ex)
        {
            _log.Write("dwm", $"DwmUpdateThumbnailProperties failed for {key}: {ex.Message}");
            Release(key);
            return false;
        }
    }

    /// <summary>Releases the registration for one mirror, if there is one.</summary>
    /// <param name="key">The monitor device name.</param>
    internal void Release(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!_handles.Remove(key, out SafeDwmThumbnailHandle? handle))
        {
            return;
        }

        handle.Dispose();
        _log.Write("dwm", $"Unregistered the thumbnail for {key}.");
    }

    /// <summary>
    /// Releases every registration. Called before any rebuild and again on shutdown.
    /// </summary>
    internal void ReleaseAll()
    {
        foreach (SafeDwmThumbnailHandle handle in _handles.Values)
        {
            handle.Dispose();
        }

        if (_handles.Count > 0)
        {
            _log.Write("dwm", $"Unregistered {_handles.Count} thumbnail handles.");
        }

        _handles.Clear();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        ReleaseAll();
    }
}
