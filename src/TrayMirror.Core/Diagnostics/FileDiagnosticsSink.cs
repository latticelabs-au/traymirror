using System.Globalization;
using System.Text;

namespace TrayMirror.Core.Diagnostics;

/// <summary>
/// Appends diagnostic lines to a text file, rolling it over once it gets large.
/// </summary>
/// <remarks>
/// The log is opened and closed per write rather than held open. It is low volume, it exists to be
/// read while traymirror is still running, and a user who is told to attach
/// <c>%TEMP%\traymirror.log</c> to a bug report should not have to quit the application first to
/// get a complete file.
/// </remarks>
public sealed class FileDiagnosticsSink : IDiagnosticsSink
{
    private const long MaximumBytes = 4 * 1024 * 1024;

    private readonly Lock _gate = new();
    private readonly string _path;

    /// <summary>
    /// Initialises a new instance of the <see cref="FileDiagnosticsSink"/> class.
    /// </summary>
    /// <param name="path">The full path of the log file.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public FileDiagnosticsSink(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _path = path;
    }

    /// <summary>Gets the default log path, <c>%TEMP%\traymirror.log</c>.</summary>
    /// <returns>The full path.</returns>
    public static string DefaultPath()
    {
        return Path.Combine(Path.GetTempPath(), "traymirror.log");
    }

    /// <inheritdoc/>
    public bool IsEnabled => true;

    /// <inheritdoc/>
    public void Write(string category, string message)
    {
        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{category}] {message}{Environment.NewLine}");

        lock (_gate)
        {
            try
            {
                RollIfLarge();
                File.AppendAllText(_path, line, Encoding.UTF8);
            }
            catch (IOException)
            {
                // A log that cannot be written must never take the application down with it.
            }
            catch (UnauthorizedAccessException)
            {
                // Same reasoning: diagnostics are best effort by definition.
            }
        }
    }

    private void RollIfLarge()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < MaximumBytes)
        {
            return;
        }

        string previous = _path + ".1";
        File.Delete(previous);
        File.Move(_path, previous);
    }
}
