namespace TrayMirror.Core.Diagnostics;

/// <summary>
/// Where diagnostic lines go.
/// </summary>
/// <remarks>
/// traymirror fails in ways the user can see but cannot explain: a mirror in the wrong place, a
/// strip that is too wide, a click that lands on the wrong icon. Every one of those is a number
/// that was read wrong somewhere, so the log records the numbers rather than a description of
/// them.
/// </remarks>
public interface IDiagnosticsSink
{
    /// <summary>Gets a value indicating whether anything is actually being recorded.</summary>
    /// <remarks>
    /// Callers check this before building an expensive message, such as a full UI Automation tree
    /// dump.
    /// </remarks>
    public bool IsEnabled { get; }

    /// <summary>Records one line.</summary>
    /// <param name="category">A short subsystem tag, for example <c>dwm</c> or <c>uia</c>.</param>
    /// <param name="message">The line to record.</param>
    public void Write(string category, string message);
}

/// <summary>
/// The sink used when <c>diagnosticsLog</c> is off. Every call is a no-op.
/// </summary>
public sealed class NullDiagnosticsSink : IDiagnosticsSink
{
    /// <summary>The shared instance.</summary>
    public static NullDiagnosticsSink Instance { get; } = new();

    private NullDiagnosticsSink()
    {
    }

    /// <inheritdoc/>
    public bool IsEnabled => false;

    /// <inheritdoc/>
    public void Write(string category, string message)
    {
        // Intentionally empty. Diagnostics are off.
    }
}
