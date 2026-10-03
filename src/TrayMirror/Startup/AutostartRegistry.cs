using Microsoft.Win32;
using TrayMirror.Core.Diagnostics;

namespace TrayMirror.Startup;

/// <summary>
/// Adds and removes traymirror's entry under the current user's <c>Run</c> key.
/// </summary>
/// <remarks>
/// <c>HKEY_CURRENT_USER</c> only. A machine-wide entry would need elevation, and traymirror runs
/// unelevated on purpose so that its synthesised clicks can reach Explorer.
/// </remarks>
internal static class AutostartRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "traymirror";

    /// <summary>Reads whether traymirror is registered to start with Windows.</summary>
    /// <returns><see langword="true"/> when an entry exists.</returns>
    internal static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is not null;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Adds or removes the entry so it matches the requested state.
    /// </summary>
    /// <param name="enabled">Whether traymirror should start with Windows.</param>
    /// <param name="executablePath">The full path of the executable to register.</param>
    /// <param name="log">Where to record a failure.</param>
    /// <returns><see langword="true"/> when the registry now matches the request.</returns>
    internal static bool Apply(bool enabled, string executablePath, IDiagnosticsSink log)
    {
        ArgumentNullException.ThrowIfNull(log);

        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);

            if (enabled)
            {
                // Quoted, because a path containing a space is otherwise parsed as a command plus
                // arguments and the entry silently fails at the next sign-in.
                key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (System.Security.SecurityException ex)
        {
            log.Write("startup", $"The Run key could not be updated: {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            log.Write("startup", $"The Run key could not be updated: {ex.Message}");
            return false;
        }
    }
}
