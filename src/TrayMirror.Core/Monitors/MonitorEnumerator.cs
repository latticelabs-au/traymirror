using System.Collections.ObjectModel;
using TrayMirror.Core.Geometry;
using TrayMirror.Core.Interop;

namespace TrayMirror.Core.Monitors;

/// <summary>
/// Reads the current display topology from Windows.
/// </summary>
/// <remarks>
/// Enumeration is cheap and always fresh. traymirror never caches a monitor set across a
/// <c>WM_DISPLAYCHANGE</c>, because a rapid unplug and replug arrives as several messages and a
/// cached set would describe a layout that no longer exists.
/// </remarks>
public static class MonitorEnumerator
{
    /// <summary>
    /// Enumerates every connected monitor.
    /// </summary>
    /// <returns>
    /// The connected monitors, primary first and then in enumeration order, which is stable enough
    /// for logging but is not a layout order and must not be treated as one.
    /// </returns>
    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var found = new List<MonitorInfo>();

        bool Callback(nint monitor, nint hdc, ref NativeMonitorMethods.Rect clip, nint data)
        {
            var info = Describe(monitor);
            if (info is not null)
            {
                found.Add(info);
            }

            return true;
        }

        // The delegate is held in a local for the duration of the call so it cannot be collected
        // while native code still holds the pointer.
        NativeMonitorMethods.MonitorEnumProc callback = Callback;
        _ = NativeMonitorMethods.EnumDisplayMonitors(0, 0, callback, 0);
        GC.KeepAlive(callback);

        return new ReadOnlyCollection<MonitorInfo>(
            [.. found.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.DeviceName, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Describes the monitor a window currently sits on.
    /// </summary>
    /// <param name="windowHandle">The window handle to locate.</param>
    /// <returns>The monitor, or <see langword="null"/> if Windows would not describe it.</returns>
    public static MonitorInfo? FromWindow(nint windowHandle)
    {
        return Describe(NativeMonitorMethods.MonitorFromWindow(windowHandle, NativeMonitorMethods.MonitorDefaultToNearest));
    }

    /// <summary>
    /// Describes the monitor containing a point in virtual-screen coordinates.
    /// </summary>
    /// <param name="x">The x coordinate in physical pixels.</param>
    /// <param name="y">The y coordinate in physical pixels.</param>
    /// <returns>The monitor, or <see langword="null"/> if Windows would not describe it.</returns>
    public static MonitorInfo? FromPoint(int x, int y)
    {
        return Describe(NativeMonitorMethods.MonitorFromPoint(
            new NativeMonitorMethods.Point { X = x, Y = y },
            NativeMonitorMethods.MonitorDefaultToNearest));
    }

    private static MonitorInfo? Describe(nint monitor)
    {
        if (monitor == 0)
        {
            return null;
        }

        var info = new NativeMonitorMethods.MonitorInfoEx
        {
            CbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMonitorMethods.MonitorInfoEx>(),
            SzDevice = string.Empty,
        };

        if (!NativeMonitorMethods.GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        return new MonitorInfo(
            info.SzDevice,
            monitor,
            ToPixelRect(info.RcMonitor),
            ToPixelRect(info.RcWork),
            (info.DwFlags & NativeMonitorMethods.MonitorinfofPrimary) != 0,
            ReadDpi(monitor));
    }

    private static DpiScale ReadDpi(nint monitor)
    {
        // GetDpiForMonitor is documented from Windows 8.1 onwards and is present on every build
        // this project supports. A non-zero HRESULT here means the shell is in a state where DPI
        // cannot be read, and 96 is the only defensible answer.
        int hr = NativeMonitorMethods.GetDpiForMonitor(
            monitor,
            NativeMonitorMethods.MdtEffectiveDpi,
            out uint dpiX,
            out uint dpiY);

        return hr != 0 || dpiX == 0 || dpiY == 0
            ? DpiScale.Identity
            : new DpiScale((int)dpiX, (int)dpiY);
    }

    private static PixelRect ToPixelRect(NativeMonitorMethods.Rect rect)
    {
        return new(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }
}
