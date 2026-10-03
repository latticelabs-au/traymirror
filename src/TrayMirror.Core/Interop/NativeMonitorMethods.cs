using System.Runtime.InteropServices;

namespace TrayMirror.Core.Interop;

/// <summary>
/// The narrow slice of Win32 that monitor topology needs.
/// </summary>
/// <remarks>
/// These live in <c>TrayMirror.Core</c> rather than in the WPF shell because enumerating displays
/// is not a window operation: it touches no handle this process owns, so it is safe off the UI
/// thread and it carries no WPF dependency. Window operations stay in the shell.
/// </remarks>
internal static class NativeMonitorMethods
{
    internal const int MonitorinfofPrimary = 0x00000001;
    internal const int MonitorDefaultToNearest = 0x00000002;
    internal const int MdtEffectiveDpi = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfoEx
    {
        public int CbSize;
        public Rect RcMonitor;
        public Rect RcWork;
        public int DwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string SzDevice;
    }

    internal delegate bool MonitorEnumProc(nint monitor, nint hdc, ref Rect clip, nint data);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint MonitorFromPoint(Point point, int flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint MonitorFromWindow(nint window, int flags);

    [DllImport("shcore.dll", ExactSpelling = true)]
    internal static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }
}
