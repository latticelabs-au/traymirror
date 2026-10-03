using System.Runtime.InteropServices;

namespace TrayMirror.Interop;

/// <summary>
/// The Desktop Window Manager thumbnail API.
/// </summary>
/// <remarks>
/// <para>
/// Every entry point here returns a raw <c>HRESULT</c> as an <see cref="int"/>. None of them is
/// declared <c>void</c>, and none of them is called without checking the result. A <c>void</c>
/// declaration discards the status at the declaration site, where no caller and no reviewer can
/// recover it, and DWM failures are silent: the mirror simply shows nothing and there is no
/// exception to trace.
/// </para>
/// <para>
/// All three calls are apartment-threaded and must run on the UI thread.
/// </para>
/// </remarks>
internal static class DwmApi
{
    /// <summary>The <c>rcDestination</c> field of the properties struct is valid.</summary>
    internal const int DwmTnpRectDestination = 0x00000001;

    /// <summary>The <c>rcSource</c> field of the properties struct is valid.</summary>
    internal const int DwmTnpRectSource = 0x00000002;

    /// <summary>The <c>opacity</c> field of the properties struct is valid.</summary>
    internal const int DwmTnpOpacity = 0x00000004;

    /// <summary>The <c>fVisible</c> field of the properties struct is valid.</summary>
    internal const int DwmTnpVisible = 0x00000008;

    /// <summary>The <c>fSourceClientAreaOnly</c> field of the properties struct is valid.</summary>
    internal const int DwmTnpSourceClientAreaOnly = 0x00000010;

    /// <summary>
    /// Whether the window is composed but not shown: a suspended packaged application, or a window
    /// belonging to another virtual desktop.
    /// </summary>
    internal const int DwmwaCloaked = 14;

    /// <summary>
    /// Reads a window attribute.
    /// </summary>
    /// <param name="window">The window to query.</param>
    /// <param name="attribute">The attribute identifier.</param>
    /// <param name="value">The value read.</param>
    /// <param name="size">The size of <paramref name="value"/> in bytes.</param>
    /// <returns>The HRESULT.</returns>
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    internal static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    /// <summary>
    /// Registers a live thumbnail relationship between a destination window this process owns and
    /// any source window on the desktop.
    /// </summary>
    /// <param name="destination">The window the thumbnail is composited into.</param>
    /// <param name="source">The window whose pixels are copied.</param>
    /// <param name="thumbnail">The resulting thumbnail handle.</param>
    /// <returns>The HRESULT.</returns>
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    internal static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);

    /// <summary>
    /// Releases a thumbnail relationship.
    /// </summary>
    /// <param name="thumbnail">The handle from <see cref="DwmRegisterThumbnail"/>.</param>
    /// <returns>The HRESULT.</returns>
    /// <remarks>
    /// Calling this on a handle whose source window has already been destroyed is safe and returns
    /// a non-fatal HRESULT. Not calling it leaks a session-global composition resource that is not
    /// reclaimed until the user logs off.
    /// </remarks>
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    internal static extern int DwmUnregisterThumbnail(nint thumbnail);

    /// <summary>
    /// Sets the crop, placement, opacity and visibility of a registered thumbnail.
    /// </summary>
    /// <param name="thumbnail">The handle from <see cref="DwmRegisterThumbnail"/>.</param>
    /// <param name="properties">The properties to apply.</param>
    /// <returns>The HRESULT.</returns>
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    internal static extern int DwmUpdateThumbnailProperties(
        nint thumbnail,
        ref DwmThumbnailProperties properties);

    /// <summary>
    /// Reads the source window's full size, in physical pixels.
    /// </summary>
    /// <param name="thumbnail">The handle from <see cref="DwmRegisterThumbnail"/>.</param>
    /// <param name="size">The source size.</param>
    /// <returns>The HRESULT.</returns>
    /// <remarks>
    /// Used only by the diagnostics dump, as a cross-check that the crop rectangle really does sit
    /// inside the source window rather than beyond its right edge.
    /// </remarks>
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    internal static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out NativeSize size);

    /// <summary>
    /// Throws when an HRESULT indicates failure, naming the call that produced it.
    /// </summary>
    /// <param name="hr">The HRESULT returned by a <c>dwmapi.dll</c> entry point.</param>
    /// <param name="call">The name of the call, supplied automatically.</param>
    /// <exception cref="System.ComponentModel.Win32Exception">The HRESULT indicates failure.</exception>
    internal static void ThrowIfFailed(
        int hr,
        [System.Runtime.CompilerServices.CallerMemberName] string call = "")
    {
        if (hr >= 0)
        {
            return;
        }

        string message = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{call} failed with HRESULT 0x{hr:X8}.");

        throw new System.ComponentModel.Win32Exception(hr, message);
    }

    /// <summary>
    /// The properties of a registered thumbnail.
    /// </summary>
    /// <remarks>
    /// <c>rcSource</c> is expressed in the coordinate space of the source window, not in screen
    /// coordinates. The source window here is <c>Shell_TrayWnd</c>, whose origin is only at (0, 0)
    /// on a single-monitor unscaled desktop. <c>TrayGeometry</c> does the subtraction so this
    /// struct is always handed an already-rebased rectangle.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DwmThumbnailProperties
    {
        public int DwFlags;
        public NativeRect RcDestination;
        public NativeRect RcSource;
        public byte Opacity;

        [MarshalAs(UnmanagedType.Bool)]
        public bool FVisible;

        [MarshalAs(UnmanagedType.Bool)]
        public bool FSourceClientAreaOnly;
    }

    /// <summary>The Win32 <c>SIZE</c> structure.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSize
    {
        public int Cx;
        public int Cy;
    }
}
