using System.Globalization;
using TrayMirror.Core.Diagnostics;
using TrayMirror.Interop;
using TrayMirror.Mirroring;

namespace TrayMirror.Input;

/// <summary>
/// Replays a click that landed on a mirror at the matching point on the primary taskbar.
/// </summary>
/// <remarks>
/// <para>
/// This is the price of the DWM thumbnail approach, stated plainly: the thumbnail is a visual
/// surface with no hit testing, so the only way to make a mirrored icon respond is to move the
/// cursor to the real icon and synthesise the click there. The cursor is put back afterwards, but
/// there is a window of a few frames where it is on the primary monitor, and that is visible.
/// </para>
/// <para>
/// <c>SendInput</c> cannot deliver events to a process at a higher integrity level than the
/// sender. Explorer runs at medium integrity in the normal Windows 11 configuration and traymirror
/// declares <c>asInvoker</c> so it matches. If Explorer is elevated and traymirror is not, the
/// synthesised input is dropped silently, with no error to report.
/// </para>
/// <para>All members must be called on the UI thread.</para>
/// </remarks>
internal sealed class InputRouter(IDiagnosticsSink log)
{
    private readonly IDiagnosticsSink _log = log;

    /// <summary>
    /// Moves the cursor to a screen point and synthesises the input there.
    /// </summary>
    /// <param name="kind">Which input to synthesise.</param>
    /// <param name="screenX">The target x in virtual-screen physical pixels.</param>
    /// <param name="screenY">The target y in virtual-screen physical pixels.</param>
    /// <param name="wheelDelta">The wheel delta, used only by <see cref="MirrorInputKind.Wheel"/>.</param>
    /// <returns>
    /// The cursor position from before the move, so the caller can restore it, or
    /// <see langword="null"/> when nothing was sent.
    /// </returns>
    internal (int X, int Y)? Send(MirrorInputKind kind, int screenX, int screenY, int wheelDelta)
    {
        if (!NativeMethods.GetCursorPos(out NativePoint origin))
        {
            return null;
        }

        List<NativeMethods.Input> inputs =
        [
            Mouse(NativeMethods.MouseEventFMove | NativeMethods.MouseEventFAbsolute | NativeMethods.MouseEventFVirtualDesk, screenX, screenY, 0),
        ];

        switch (kind)
        {
            case MirrorInputKind.LeftClick:
                inputs.Add(Mouse(NativeMethods.MouseEventFLeftDown, screenX, screenY, 0));
                inputs.Add(Mouse(NativeMethods.MouseEventFLeftUp, screenX, screenY, 0));
                break;

            case MirrorInputKind.RightClick:
                inputs.Add(Mouse(NativeMethods.MouseEventFRightDown, screenX, screenY, 0));
                inputs.Add(Mouse(NativeMethods.MouseEventFRightUp, screenX, screenY, 0));
                break;

            case MirrorInputKind.MiddleClick:
                inputs.Add(Mouse(NativeMethods.MouseEventFMiddleDown, screenX, screenY, 0));
                inputs.Add(Mouse(NativeMethods.MouseEventFMiddleUp, screenX, screenY, 0));
                break;

            case MirrorInputKind.Wheel:
                inputs.Add(Mouse(NativeMethods.MouseEventFWheel, screenX, screenY, wheelDelta));
                break;

            default:
                return null;
        }

        NativeMethods.Input[] batch = [.. inputs];
        uint sent = NativeMethods.SendInput(
            (uint)batch.Length,
            batch,
            System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.Input>());

        if (sent != batch.Length)
        {
            int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            _log.Write("input", string.Create(
                CultureInfo.InvariantCulture,
                $"SendInput accepted {sent} of {batch.Length} events for a {kind} at ({screenX},{screenY}), Win32 error {error}. Error 5 means the target process runs at a higher integrity level than traymirror, which is the one cause that cannot be worked around."));
            return null;
        }

        if (_log.IsEnabled)
        {
            _log.Write("input", string.Create(
                CultureInfo.InvariantCulture,
                $"Routed a {kind} to ({screenX},{screenY}) on the primary screen."));
        }

        return (origin.X, origin.Y);
    }

    /// <summary>
    /// Puts the cursor back where it was before a synthesised click.
    /// </summary>
    /// <param name="origin">The position returned by <see cref="Send"/>.</param>
    /// <remarks>
    /// Called on a short delay rather than immediately. Some tray applications read the cursor
    /// position while handling the click in order to decide where to open their own menu, and
    /// moving it back in the same frame makes that menu appear in the wrong place.
    /// </remarks>
    internal static void RestoreCursor((int X, int Y) origin)
    {
        _ = NativeMethods.SetCursorPos(origin.X, origin.Y);
    }

    private static NativeMethods.Input Mouse(int flags, int screenX, int screenY, int data)
    {
        (int nx, int ny) = ToAbsolute(screenX, screenY);

        return new NativeMethods.Input
        {
            Type = NativeMethods.InputMouse,
            Mouse = new NativeMethods.MouseInput
            {
                Dx = nx,
                Dy = ny,
                MouseData = data,
                DwFlags = flags,
                Time = 0,
                ExtraInfo = 0,
            },
        };
    }

    /// <summary>
    /// Converts a virtual-screen pixel coordinate to the normalised 0 to 65535 space
    /// <c>SendInput</c> expects for absolute motion.
    /// </summary>
    /// <param name="screenX">The x coordinate in physical pixels.</param>
    /// <param name="screenY">The y coordinate in physical pixels.</param>
    /// <returns>The normalised coordinates.</returns>
    /// <remarks>
    /// The virtual screen origin is not (0, 0) when a monitor sits left of or above the primary,
    /// which is common. Normalising against the primary monitor's size instead of the virtual
    /// screen is the mistake that makes routed clicks land in the wrong place on exactly those
    /// layouts.
    /// </remarks>
    private static (int X, int Y) ToAbsolute(int screenX, int screenY)
    {
        int left = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        int top = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        int width = Math.Max(NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen), 1);
        int height = Math.Max(NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen), 1);

        int nx = (int)Math.Round((screenX - left) * 65535.0 / width, MidpointRounding.AwayFromZero);
        int ny = (int)Math.Round((screenY - top) * 65535.0 / height, MidpointRounding.AwayFromZero);

        return (Math.Clamp(nx, 0, 65535), Math.Clamp(ny, 0, 65535));
    }
}
