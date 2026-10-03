using TrayMirror.Core.Diagnostics;
using TrayMirror.Interop;
using TrayMirror.Shell;

namespace TrayMirror.Tray;

/// <summary>What the user picked from traymirror's own tray menu.</summary>
internal enum TrayCommand
{
    /// <summary>Nothing was picked.</summary>
    None = 0,

    /// <summary>Re-read the config file now.</summary>
    ReloadConfig = 1,

    /// <summary>Toggle the <c>Run</c> key entry.</summary>
    ToggleStartWithWindows = 2,

    /// <summary>Open the config file in the default editor.</summary>
    OpenConfigFile = 3,

    /// <summary>Open the diagnostics log in the default editor.</summary>
    OpenDiagnosticsLog = 4,

    /// <summary>Quit.</summary>
    Exit = 5,
}

/// <summary>
/// traymirror's own notification icon and its context menu.
/// </summary>
/// <remarks>
/// <para>
/// Built directly on <c>Shell_NotifyIcon</c> and <c>TrackPopupMenuEx</c> rather than on a
/// WinForms <c>NotifyIcon</c>. The alternative would pull the whole Windows Forms framework into a
/// WPF application for one icon and five menu items, and this application is already an interop
/// application everywhere else.
/// </para>
/// <para>
/// The menu is exactly five items in a fixed order: Reload config, Start with Windows (checkable),
/// Open config file, Open diagnostics log, Exit. There is no settings window. Everything
/// configurable lives in the config file, including diagnostics logging, which is why a bug report
/// asks the user to set <c>diagnosticsLog</c> there rather than to tick something here.
/// </para>
/// <para>All members must be called on the UI thread.</para>
/// </remarks>
internal sealed class TrayIconHost : IDisposable
{
    private const int CallbackMessage = 0x0400 + 1;
    private const int WmContextMenu = 0x007B;
    private const int WmLButtonUp = 0x0202;
    private const int NinSelect = 0x0400;

    private readonly IDiagnosticsSink _log;
    private readonly nint _window;
    private nint _icon;
    private bool _added;
    private bool _disposed;

    /// <summary>
    /// Initialises a new instance of the <see cref="TrayIconHost"/> class and adds the icon.
    /// </summary>
    /// <param name="messageWindow">The hidden window that receives the icon's callback message.</param>
    /// <param name="log">Where to record failures.</param>
    internal TrayIconHost(MessageWindow messageWindow, IDiagnosticsSink log)
    {
        ArgumentNullException.ThrowIfNull(messageWindow);
        ArgumentNullException.ThrowIfNull(log);

        _log = log;
        _window = messageWindow.Handle;
        _icon = LoadApplicationIcon();

        messageWindow.AddHook(OnMessage);
        Add();
    }

    /// <summary>Raised when the user picks something from the menu.</summary>
    internal event EventHandler<TrayCommand>? CommandInvoked;

    /// <summary>Gets or sets a value indicating whether the menu shows autostart as ticked.</summary>
    internal bool StartWithWindowsChecked { get; set; }

    /// <summary>Gets or sets the tooltip shown when hovering the icon.</summary>
    internal string Tooltip { get; set; } = "traymirror";

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_added)
        {
            NativeMethods.NotifyIconData data = Describe(NativeMethods.NifMessage);
            _ = NativeMethods.ShellNotifyIcon(NativeMethods.NimDelete, ref data);
            _added = false;
        }

        if (_icon != 0)
        {
            _ = NativeMethods.DestroyIcon(_icon);
            _icon = 0;
        }
    }

    /// <summary>
    /// Re-adds the icon after Explorer has recreated the taskbar.
    /// </summary>
    /// <remarks>
    /// The notification area is rebuilt from scratch on an Explorer restart and every icon in it is
    /// forgotten. An application that does not re-add its icon on <c>TaskbarCreated</c> simply
    /// disappears from the tray while still running, which is a common and confusing failure.
    /// </remarks>
    internal void Readd()
    {
        _added = false;
        Add();
    }

    private static nint LoadApplicationIcon()
    {
        // "#32512" is the resource identifier the .NET apphost gives the icon from ApplicationIcon.
        nint fromExecutable = NativeMethods.LoadImage(
            NativeMethods.GetModuleHandle(null),
            "#32512",
            NativeMethods.ImageIcon,
            0,
            0,
            NativeMethods.LrDefaultSize);

        return fromExecutable;
    }

    private void Add()
    {
        NativeMethods.NotifyIconData data = Describe(NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip | NativeMethods.NifShowTip);

        if (!NativeMethods.ShellNotifyIcon(NativeMethods.NimAdd, ref data))
        {
            _log.Write("tray", "The traymirror notification icon could not be added.");
            return;
        }

        _added = true;

        NativeMethods.NotifyIconData version = Describe(0);
        version.UVersion = NativeMethods.NotifyIconVersion4;
        _ = NativeMethods.ShellNotifyIcon(NativeMethods.NimSetVersion, ref version);
    }

    private NativeMethods.NotifyIconData Describe(int flags)
    {
        return new NativeMethods.NotifyIconData
        {
            CbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.NotifyIconData>(),
            HWnd = _window,
            UId = 1,
            UFlags = flags,
            UCallbackMessage = CallbackMessage,
            HIcon = _icon,
            SzTip = Tooltip.Length > 127 ? Tooltip[..127] : Tooltip,
            SzInfo = string.Empty,
            SzInfoTitle = string.Empty,
        };
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != CallbackMessage)
        {
            return 0;
        }

        int notification = (int)(lParam & 0xFFFF);

        if (notification is WmContextMenu or WmLButtonUp or NinSelect)
        {
            ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
            handled = true;
        }

        return 0;
    }

    private void ShowMenu(int x, int y)
    {
        nint menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MfString, (nint)TrayCommand.ReloadConfig, "Reload config");
            _ = NativeMethods.AppendMenu(
                menu,
                NativeMethods.MfString | (StartWithWindowsChecked ? NativeMethods.MfChecked : NativeMethods.MfUnchecked),
                (nint)TrayCommand.ToggleStartWithWindows,
                "Start with Windows");
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, null);
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MfString, (nint)TrayCommand.OpenConfigFile, "Open config file");
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MfString, (nint)TrayCommand.OpenDiagnosticsLog, "Open diagnostics log");
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, null);
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MfString, (nint)TrayCommand.Exit, "Exit");

            // Documented requirement: the owner window must be foreground before the menu is shown
            // and must be posted a message afterwards, or the menu never closes when the user
            // clicks elsewhere.
            _ = NativeMethods.SetForegroundWindow(_window);

            int selected = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TpmRightButton | NativeMethods.TpmReturnCmd | NativeMethods.TpmRightAlign | NativeMethods.TpmBottomAlign,
                x,
                y,
                _window,
                0);

            _ = NativeMethods.PostMessage(_window, 0x0000, 0, 0);

            if (selected != 0)
            {
                CommandInvoked?.Invoke(this, (TrayCommand)selected);
            }
        }
        finally
        {
            _ = NativeMethods.DestroyMenu(menu);
        }
    }
}
