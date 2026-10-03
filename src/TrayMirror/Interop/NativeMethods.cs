using System.Runtime.InteropServices;
using TrayMirror.Core.Geometry;

namespace TrayMirror.Interop;

/// <summary>The Win32 <c>RECT</c> structure.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    /// <summary>Converts this rectangle to the WPF-free type the rest of the code uses.</summary>
    /// <returns>The equivalent <see cref="PixelRect"/>.</returns>
    internal readonly PixelRect ToPixelRect()
    {
        return new PixelRect(Left, Top, Right, Bottom);
    }

    /// <summary>Converts a <see cref="PixelRect"/> to the Win32 layout.</summary>
    /// <param name="rect">The rectangle to convert.</param>
    /// <returns>The equivalent <see cref="NativeRect"/>.</returns>
    internal static NativeRect From(PixelRect rect)
    {
        return new NativeRect
        {
            Left = rect.Left,
            Top = rect.Top,
            Right = rect.Right,
            Bottom = rect.Bottom,
        };
    }
}

/// <summary>The Win32 <c>POINT</c> structure.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;
}

/// <summary>
/// The Win32 surface traymirror uses, grouped by the job it does rather than by header file.
/// </summary>
/// <remarks>
/// Everything here that touches a window handle owned by this process must be called on the UI
/// thread. That is a Win32 rule, not a WPF one, and it holds for the message-only helper windows
/// as much as for the visible mirrors.
/// </remarks>
internal static class NativeMethods
{
    // Window styles.
    internal const int GwlStyle = -16;
    internal const int GwlExStyle = -20;
    internal const int WsExToolWindow = 0x00000080;
    internal const int WsExNoActivate = 0x08000000;
    internal const int WsExTopmost = 0x00000008;

    // SetWindowPos flags and the special HWND values it accepts.
    internal const int SwpNoSize = 0x0001;
    internal const int SwpNoMove = 0x0002;
    internal const int SwpNoZOrder = 0x0004;
    internal const int SwpNoActivate = 0x0010;
    internal const int SwpShowWindow = 0x0040;
    internal const int SwpHideWindow = 0x0080;
    internal const int SwpNoOwnerZOrder = 0x0200;
    internal static readonly nint HwndTopmost = -1;

    // Window messages traymirror reacts to.
    internal const int WmDestroy = 0x0002;
    internal const int WmClose = 0x0010;
    internal const int WmDisplayChange = 0x007E;
    internal const int WmDpiChanged = 0x02E0;
    internal const int WmMouseActivate = 0x0021;
    internal const int WmLButtonDown = 0x0201;
    internal const int WmLButtonUp = 0x0202;
    internal const int WmRButtonDown = 0x0204;
    internal const int WmRButtonUp = 0x0205;
    internal const int WmMButtonUp = 0x0208;
    internal const int WmMouseWheel = 0x020A;
    internal const int WmCommand = 0x0111;
    internal const int WmSettingChange = 0x001A;
    internal const int MaNoActivate = 3;

    // Mouse input, for click routing.
    internal const int InputMouse = 0;
    internal const int MouseEventFAbsolute = 0x8000;
    internal const int MouseEventFVirtualDesk = 0x4000;
    internal const int MouseEventFMove = 0x0001;
    internal const int MouseEventFLeftDown = 0x0002;
    internal const int MouseEventFLeftUp = 0x0004;
    internal const int MouseEventFRightDown = 0x0008;
    internal const int MouseEventFRightUp = 0x0010;
    internal const int MouseEventFMiddleDown = 0x0020;
    internal const int MouseEventFMiddleUp = 0x0040;
    internal const int MouseEventFWheel = 0x0800;

    // Virtual screen metrics, needed to express absolute mouse coordinates.
    internal const int SmXVirtualScreen = 76;
    internal const int SmYVirtualScreen = 77;
    internal const int SmCxVirtualScreen = 78;
    internal const int SmCyVirtualScreen = 79;

    // WinEvent hooks, both registered out of process. Neither loads anything into Explorer.
    internal const int EventSystemMenuPopupStart = 0x0006;
    internal const int EventSystemForeground = 0x0003;
    internal const int EventObjectShow = 0x8002;
    internal const int EventObjectHide = 0x8003;
    internal const int EventObjectLocationChange = 0x800B;
    internal const int EventObjectCloaked = 0x8017;
    internal const int EventObjectUncloaked = 0x8018;
    internal const int WineventOutOfContext = 0x0000;
    internal const int WineventSkipOwnProcess = 0x0002;

    // Notification icon.
    internal const int NimAdd = 0x00000000;
    internal const int NimModify = 0x00000001;
    internal const int NimDelete = 0x00000002;
    internal const int NimSetVersion = 0x00000004;
    internal const int NotifyIconVersion4 = 4;
    internal const int NifMessage = 0x00000001;
    internal const int NifIcon = 0x00000002;
    internal const int NifTip = 0x00000004;
    internal const int NifShowTip = 0x00000080;

    // Menus.
    internal const int MfString = 0x00000000;
    internal const int MfSeparator = 0x00000800;
    internal const int MfChecked = 0x00000008;
    internal const int MfUnchecked = 0x00000000;
    internal const int TpmRightButton = 0x0002;
    internal const int TpmRightAlign = 0x0008;
    internal const int TpmBottomAlign = 0x0020;
    internal const int TpmReturnCmd = 0x0100;

    // ATTACH_PARENT_PROCESS, so --probe can write to the console that launched it.
    internal const int AttachParentProcess = -1;

    internal const int ImageIcon = 1;
    internal const int LrLoadFromFile = 0x00000010;
    internal const int LrDefaultSize = 0x00000040;

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint data);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassName(nint window, [Out] char[] buffer, int maxCount);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint window, out NativeRect rect);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(nint window, ref NativePoint point);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int cx,
        int cy,
        int flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int RegisterWindowMessage(string message);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachConsole(int processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    internal static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint SetWinEventHook(
        int eventMin,
        int eventMax,
        nint moduleHandle,
        WinEventProc callback,
        int processId,
        int threadId,
        int flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int GetWindowThreadProcessId(nint window, out int processId);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShellNotifyIcon(int message, ref NotifyIconData data);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint LoadImage(
        nint instance,
        string name,
        int type,
        int cx,
        int cy,
        int load);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    internal static extern nint CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AppendMenu(nint menu, int flags, nint id, string? item);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int TrackPopupMenuEx(nint menu, int flags, int x, int y, nint window, nint parameters);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint window, int message, nint wParam, nint lParam);

    /// <summary>The callback shape of <c>EnumWindows</c>.</summary>
    /// <param name="window">The window being enumerated.</param>
    /// <param name="data">The caller-supplied value.</param>
    /// <returns><see langword="true"/> to continue enumerating.</returns>
    internal delegate bool EnumWindowsProc(nint window, nint data);

    /// <summary>The callback shape of <c>SetWinEventHook</c>.</summary>
    /// <param name="hook">The hook handle.</param>
    /// <param name="eventId">The event that fired.</param>
    /// <param name="window">The window the event relates to.</param>
    /// <param name="objectId">The object identifier.</param>
    /// <param name="childId">The child identifier.</param>
    /// <param name="threadId">The originating thread.</param>
    /// <param name="timestamp">The event time.</param>
    internal delegate void WinEventProc(
        nint hook,
        int eventId,
        nint window,
        int objectId,
        int childId,
        int threadId,
        int timestamp);

    /// <summary>The Win32 <c>INPUT</c> structure, restricted to its mouse variant.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public int Type;
        public MouseInput Mouse;
    }

    /// <summary>The Win32 <c>MOUSEINPUT</c> structure.</summary>
    /// <remarks>
    /// No padding. <c>INPUT</c> is a union whose largest member is <c>MOUSEINPUT</c> itself, so
    /// this struct already sizes the union and adding filler fields would inflate the
    /// <c>cbSize</c> passed to <c>SendInput</c>. That failure is silent: <c>SendInput</c> simply
    /// returns zero events accepted, which reads exactly like being blocked by a higher-integrity
    /// target rather than like a marshalling mistake.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int Dx;
        public int Dy;
        public int MouseData;
        public int DwFlags;
        public int Time;
        public nint ExtraInfo;
    }

    /// <summary>The Win32 <c>NOTIFYICONDATAW</c> structure.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        public int CbSize;
        public nint HWnd;
        public int UId;
        public int UFlags;
        public int UCallbackMessage;
        public nint HIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string SzTip;

        public int DwState;
        public int DwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string SzInfo;

        public int UVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string SzInfoTitle;

        public int DwInfoFlags;
        public Guid GuidItem;
        public nint HBalloonIcon;
    }

    /// <summary>Reads a window rectangle as a <see cref="PixelRect"/>.</summary>
    /// <param name="window">The window handle.</param>
    /// <returns>
    /// The window rectangle, or <see cref="PixelRect.Empty"/> when the handle is not a window or
    /// the call fails.
    /// </returns>
    internal static PixelRect GetWindowBounds(nint window)
    {
        if (window == 0 || !IsWindow(window) || !GetWindowRect(window, out NativeRect rect))
        {
            return PixelRect.Empty;
        }

        return rect.ToPixelRect();
    }

    /// <summary>Reads a window class name.</summary>
    /// <param name="window">The window handle.</param>
    /// <returns>The class name, or an empty string when it cannot be read.</returns>
    internal static string GetWindowClassName(nint window)
    {
        char[] buffer = new char[256];
        int length = GetClassName(window, buffer, buffer.Length);
        return length <= 0 ? string.Empty : new string(buffer, 0, length);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetThreadDesktop(nint hDesktop);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Desktop switch is best-effort fallback.")]
    internal static void EnsureDefaultDesktop()
    {
        try
        {
            nint hDesk = OpenDesktop("Default", 0, false, 0x01FF);
            if (hDesk != 0)
            {
                _ = SetThreadDesktop(hDesk);
            }
        }
        catch (Exception)
        {
        }
    }
}
