using Microsoft.Win32.SafeHandles;

namespace TrayMirror.Interop;

/// <summary>
/// Owns one DWM thumbnail registration and guarantees it is unregistered exactly once.
/// </summary>
/// <remarks>
/// <para>
/// A thumbnail handle is a session-global composition resource. Leaking one does not leak process
/// memory that a restart reclaims: it stays leaked in the DWM session until the user logs off. The
/// four paths that must release a handle are monitor removal, taskbar recreation, DPI or display
/// rebuild, and application shutdown, and an exception thrown between registering and updating
/// would skip an explicit unregister on every one of them.
/// </para>
/// <para>
/// Wrapping the handle removes that whole class of mistake. The explicit release calls still exist
/// and are still the primary mechanism, because relying on finalisation would leave the resource
/// held for an unbounded time; this type is the backstop, not the plan.
/// </para>
/// </remarks>
internal sealed class SafeDwmThumbnailHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeDwmThumbnailHandle()
        : base(ownsHandle: true)
    {
    }

    /// <summary>
    /// Registers a thumbnail relationship and wraps the resulting handle.
    /// </summary>
    /// <param name="destination">The mirror window, which this process owns.</param>
    /// <param name="source">The window whose pixels are copied, normally <c>Shell_TrayWnd</c>.</param>
    /// <returns>The wrapped handle.</returns>
    /// <exception cref="System.ComponentModel.Win32Exception">
    /// DWM refused the registration. The usual cause is a source window that has just been
    /// destroyed, which is exactly what happens when Explorer restarts mid-rebuild.
    /// </exception>
    /// <remarks>Must be called on the UI thread.</remarks>
    internal static SafeDwmThumbnailHandle Register(nint destination, nint source)
    {
        int hr = DwmApi.DwmRegisterThumbnail(destination, source, out nint raw);
        DwmApi.ThrowIfFailed(hr);

        var wrapper = new SafeDwmThumbnailHandle();
        wrapper.SetHandle(raw);
        return wrapper;
    }

    /// <summary>
    /// Applies crop, placement, opacity and visibility to this thumbnail.
    /// </summary>
    /// <param name="properties">The properties to apply.</param>
    /// <exception cref="System.ComponentModel.Win32Exception">DWM rejected the update.</exception>
    /// <remarks>Must be called on the UI thread.</remarks>
    internal void Update(ref DwmApi.DwmThumbnailProperties properties)
    {
        int hr = DwmApi.DwmUpdateThumbnailProperties(handle, ref properties);
        DwmApi.ThrowIfFailed(hr);
    }

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        // Deliberately unchecked. This runs during teardown, including finalisation, and by then a
        // failure carries no information the caller can act on: the source window is usually gone,
        // which is precisely why the handle is being released.
        return DwmApi.DwmUnregisterThumbnail(handle) >= 0;
    }
}
