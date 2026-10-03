using System.Windows.Interop;

namespace TrayMirror.Shell;

/// <summary>
/// A hidden top-level window that exists only to receive messages.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately <em>not</em> a message-only window. A window parented to <c>HWND_MESSAGE</c> is
/// cheaper and tidier, and it does not receive broadcasts: neither the registered
/// <c>TaskbarCreated</c> message nor <c>WM_DISPLAYCHANGE</c> would ever arrive, and traymirror
/// would silently stop recovering from an Explorer restart. So this is a real top-level window,
/// sized 1x1 and parked off screen.
/// </para>
/// <para>
/// It also hosts the notification icon's callback message, so the tray icon and the shell watcher
/// share one handle rather than each creating their own.
/// </para>
/// </remarks>
internal sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;

    /// <summary>
    /// Initialises a new instance of the <see cref="MessageWindow"/> class.
    /// </summary>
    /// <param name="title">The window title, which appears only in debugging tools.</param>
    /// <remarks>Must be constructed on the UI thread.</remarks>
    internal MessageWindow(string title)
    {
        var parameters = new HwndSourceParameters(title)
        {
            Width = 1,
            Height = 1,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = 0,
        };

        _source = new HwndSource(parameters);
    }

    /// <summary>Gets the window handle.</summary>
    internal nint Handle => _source.Handle;

    /// <summary>Adds a message hook.</summary>
    /// <param name="hook">The hook to add.</param>
    internal void AddHook(HwndSourceHook hook)
    {
        _source.AddHook(hook);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _source.Dispose();
    }
}
