using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using TrayMirror.Core.Geometry;
using TrayMirror.Interop;

namespace TrayMirror.Mirroring;

/// <summary>
/// The borderless, topmost, non-activating window that a DWM thumbnail is composited into.
/// </summary>
/// <remarks>
/// <para>
/// The window has no WPF content and never will. DWM draws the mirrored strip over it on the
/// compositor's own thread, so anything rendered here would sit underneath and be invisible. The
/// black background exists only for the frame between the window appearing and the first composited
/// frame arriving.
/// </para>
/// <para>
/// The window is deliberately not transparent to input. A DWM thumbnail does not hit test, so this
/// window is the only thing that can receive a click on a mirrored icon, and click routing depends
/// on it doing so.
/// </para>
/// <para>
/// <strong>Why the mirrored strip does not take the taskbar's tint.</strong> With transparency
/// effects on, the real taskbar reads as a tinted colour (rgb(25,45,53) on the reference machine)
/// while the mirrored strip reads as flat dark grey (rgb(35,35,35)). That difference cannot be
/// closed from here, and it is worth writing down why so nobody spends another afternoon on it.
/// A thumbnail carries the source window's own surface. The tint is not in that surface: Windows
/// produces it during composition, from an acrylic backdrop drawn behind the taskbar, and DWM does
/// not include it in a thumbnail. Painting this window a different colour does not help either,
/// because the surface is close to opaque: with a red background the strip moved only from
/// rgb(35,35,35) to rgb(39,34,34), about three percent. The one thing that does close the gap is
/// turning off Settings, Personalisation, Colours, Transparency effects, which makes both bars a
/// flat colour that the mirror then matches exactly.
/// </para>
/// </remarks>
internal sealed class MirrorWindow : Window
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsVisible = 0x10000000;
    private const int WsClipSiblings = 0x04000000;
    private const int WsClipChildren = 0x02000000;

    private HwndSource? _source;

    /// <summary>
    /// Initialises a new instance of the <see cref="MirrorWindow"/> class.
    /// </summary>
    /// <param name="deviceName">The device name of the monitor this mirror belongs to.</param>
    internal MirrorWindow(string deviceName)
    {
        DeviceName = deviceName;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        // A solid colour, and it is very nearly invisible. Measured on build 26200: painting this
        // window red moved the mirrored strip's background from rgb(35,35,35) to rgb(39,34,34), so
        // the taskbar surface a thumbnail carries is about 97% opaque and what sits behind it
        // contributes almost nothing. See the class remarks for why the mirrored strip therefore
        // cannot be tinted to match the secondary taskbar.
        Background = Brushes.Black;
        Title = "traymirror";

        // Off screen until the controller places it, so no frame is ever shown at (0, 0).
        Left = -32000;
        Top = -32000;
        Width = 1;
        Height = 1;
    }

    /// <summary>Raised when a mouse button is pressed or the wheel is turned over the mirror.</summary>
    internal event EventHandler<MirrorInputEventArgs>? MirrorInput;

    /// <summary>
    /// Raised when a mouse button goes down on the mirror, before anything is routed.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="MirrorInput"/>, which fires on the release, because some tray
    /// flyouts close themselves the instant a click lands anywhere outside them. Anything the
    /// controller needs to know about the state before the click had an effect has to be sampled
    /// here.
    /// </remarks>
    internal event EventHandler? MirrorPressed;

    /// <summary>Raised when Windows reports a DPI change for this window's monitor.</summary>
    internal event EventHandler? MonitorDpiChanged;

    /// <summary>Gets the device name of the monitor this mirror belongs to.</summary>
    internal string DeviceName { get; }

    /// <summary>Gets this window's handle, or zero before it has been shown.</summary>
    internal nint Handle => _source?.Handle ?? 0;

    /// <summary>Gets the window rectangle in screen coordinates.</summary>
    internal PixelRect Bounds { get; private set; } = PixelRect.Empty;

    /// <summary>Gets a value indicating whether the mirror is currently on screen.</summary>
    internal bool IsMirrorVisible { get; private set; }

    /// <summary>Gets the client rectangle in screen coordinates.</summary>
    /// <remarks>
    /// DWM expresses <c>rcDestination</c> in client coordinates. The window is forced to
    /// <c>WS_POPUP</c> so client and window rectangles coincide, but the client rectangle is read
    /// back rather than assumed, because a mismatch would offset the whole mirrored strip and the
    /// cause would not be obvious from looking at it.
    /// </remarks>
    internal PixelRect ClientBounds { get; private set; } = PixelRect.Empty;

    /// <summary>
    /// Moves and resizes the mirror, keeping it topmost and never activating it.
    /// </summary>
    /// <param name="bounds">The target window rectangle in screen coordinates.</param>
    /// <returns><see langword="true"/> when the window ended up where it was asked to go.</returns>
    /// <remarks>Must be called on the UI thread.</remarks>
    internal bool PlaceAt(PixelRect bounds)
    {
        if (Handle == 0 || bounds.IsEmpty)
        {
            return false;
        }

        _ = NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HwndTopmost,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow | NativeMethods.SwpNoOwnerZOrder);

        Bounds = NativeMethods.GetWindowBounds(Handle);
        ClientBounds = ReadClientBounds();
        IsMirrorVisible = !Bounds.IsEmpty;

        return IsMirrorVisible;
    }

    /// <summary>
    /// Takes the mirror off screen without destroying it or its thumbnail registration.
    /// </summary>
    /// <remarks>
    /// Used when the taskbar the mirror is anchored to is hidden, which happens whenever an
    /// application goes full screen. Hiding rather than tearing down matters: the window and its
    /// DWM registration survive, so coming back is a <see cref="PlaceAt"/> call rather than a
    /// rebuild that has to find the taskbar and re-read the tray first.
    /// </remarks>
    internal void HideMirror()
    {
        if (Handle == 0 || !IsMirrorVisible)
        {
            return;
        }

        _ = NativeMethods.SetWindowPos(
            Handle,
            0,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder
                | NativeMethods.SwpNoActivate | NativeMethods.SwpHideWindow);

        IsMirrorVisible = false;
    }

    /// <inheritdoc/>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _source = (HwndSource)PresentationSource.FromVisual(this)!;
        _source.AddHook(WndProc);

        nint handle = _source.Handle;

        // WS_POPUP guarantees the client rectangle equals the window rectangle, which keeps the
        // DWM destination rectangle and the click translation working in the same coordinates.
        _ = NativeMethods.SetWindowLongPtr(
            handle,
            NativeMethods.GwlStyle,
            WsPopup | WsVisible | WsClipSiblings | WsClipChildren);

        // WS_EX_TOOLWINDOW keeps the mirror out of Alt-Tab. WS_EX_NOACTIVATE stops a click on a
        // mirrored icon from stealing focus from whatever the user was actually working in.
        nint exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle);
        _ = NativeMethods.SetWindowLongPtr(
            handle,
            NativeMethods.GwlExStyle,
            exStyle | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate);

    }

    /// <summary>Extracts the high-order word of a message parameter, which carries the wheel delta.</summary>
    /// <param name="value">The raw <c>wParam</c>.</param>
    /// <returns>The signed high-order word.</returns>
    private static int HighWord(nint value)
    {
        return (short)((value >> 16) & 0xFFFF);
    }

    private PixelRect ReadClientBounds()
    {
        if (Handle == 0 || !NativeMethods.GetClientRect(Handle, out NativeRect client))
        {
            return PixelRect.Empty;
        }

        var origin = new NativePoint { X = 0, Y = 0 };
        if (!NativeMethods.ClientToScreen(Handle, ref origin))
        {
            return PixelRect.Empty;
        }

        return PixelRect.FromSize(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        switch (message)
        {
            case NativeMethods.WmMouseActivate:
                // Never take activation. The click is going to be replayed on the primary anyway,
                // and stealing focus here would close whatever menu the user already had open.
                handled = true;
                return NativeMethods.MaNoActivate;

            case NativeMethods.WmLButtonDown:
            case NativeMethods.WmRButtonDown:
                // Swallowed, not routed. Routing here would synthesise a full press and release on
                // the primary while the user is still physically holding the button down, and the
                // real release then arrives at whatever is under the warped cursor. Waiting for the
                // matching release keeps the two input streams from interleaving.
                MirrorPressed?.Invoke(this, EventArgs.Empty);
                handled = true;
                return 0;

            case NativeMethods.WmLButtonUp:
                RaiseInput(MirrorInputKind.LeftClick, 0);
                handled = true;
                return 0;

            case NativeMethods.WmRButtonUp:
                RaiseInput(MirrorInputKind.RightClick, 0);
                handled = true;
                return 0;

            case NativeMethods.WmMButtonUp:
                RaiseInput(MirrorInputKind.MiddleClick, 0);
                handled = true;
                return 0;

            case NativeMethods.WmMouseWheel:
                RaiseInput(MirrorInputKind.Wheel, HighWord(wParam));
                handled = true;
                return 0;

            case NativeMethods.WmDpiChanged:
                MonitorDpiChanged?.Invoke(this, EventArgs.Empty);
                handled = true;
                return 0;

            default:
                return 0;
        }
    }

    private void RaiseInput(MirrorInputKind kind, int wheelDelta)
    {
        if (MirrorInput is null || !NativeMethods.GetCursorPos(out NativePoint cursor))
        {
            return;
        }

        PixelRect client = ClientBounds.IsEmpty ? Bounds : ClientBounds;
        if (client.IsEmpty)
        {
            return;
        }

        MirrorInput.Invoke(
            this,
            new MirrorInputEventArgs(kind, cursor.X - client.Left, cursor.Y - client.Top, wheelDelta));
    }
}

/// <summary>What the user did on a mirror.</summary>
internal enum MirrorInputKind
{
    /// <summary>A left mouse button press.</summary>
    LeftClick,

    /// <summary>A right mouse button press.</summary>
    RightClick,

    /// <summary>A middle mouse button release.</summary>
    MiddleClick,

    /// <summary>A mouse wheel turn, which the volume icon responds to.</summary>
    Wheel,
}

/// <summary>
/// A mouse event on a mirror, already expressed as an offset into the mirrored strip.
/// </summary>
/// <param name="kind">What the user did.</param>
/// <param name="dx">The x offset from the mirror's left edge, in physical pixels.</param>
/// <param name="dy">The y offset from the mirror's top edge, in physical pixels.</param>
/// <param name="wheelDelta">The wheel delta, or zero for button events.</param>
internal sealed class MirrorInputEventArgs(MirrorInputKind kind, int dx, int dy, int wheelDelta) : EventArgs
{
    /// <summary>Gets what the user did.</summary>
    internal MirrorInputKind Kind { get; } = kind;

    /// <summary>Gets the x offset from the mirror's left edge, in physical pixels.</summary>
    internal int Dx { get; } = dx;

    /// <summary>Gets the y offset from the mirror's top edge, in physical pixels.</summary>
    internal int Dy { get; } = dy;

    /// <summary>Gets the wheel delta, or zero for button events.</summary>
    internal int WheelDelta { get; } = wheelDelta;
}
