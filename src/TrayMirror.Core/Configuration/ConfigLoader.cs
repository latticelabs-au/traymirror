using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace TrayMirror.Core.Configuration;

/// <summary>
/// Reads, validates and creates <c>%APPDATA%\TrayMirror\traymirror.jsonc</c>.
/// </summary>
/// <remarks>
/// A config file is edited by hand, in a text editor, usually while something is already not
/// working. So loading never throws on bad content. An unparseable file, an opacity of 12 or a
/// monitor name with a typo all produce defaults plus a warning, and the warnings go to the
/// diagnostics log and the tray tooltip. Refusing to start because a number is out of range would
/// leave the user with no mirrors and no way to see why.
/// </remarks>
public static class ConfigLoader
{
    /// <summary>The file name traymirror looks for.</summary>
    public const string FileName = "traymirror.jsonc";

    private const double MinimumOpacity = 0.05;
    private const int PaddingLimit = 512;

    /// <summary>
    /// Gets the full path of the config file for the current user.
    /// </summary>
    /// <returns>The path under <c>%APPDATA%\TrayMirror</c>.</returns>
    public static string DefaultPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TrayMirror",
            FileName);
    }

    /// <summary>
    /// Creates the config file with commented defaults if it does not already exist.
    /// </summary>
    /// <param name="path">The full path to create.</param>
    /// <returns>
    /// <see langword="true"/> if a file was written, <see langword="false"/> if one was already
    /// there or the write failed.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static bool EnsureExists(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (File.Exists(path))
        {
            return false;
        }

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, DefaultTemplate);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Loads and validates the config file.
    /// </summary>
    /// <param name="path">The full path to read.</param>
    /// <returns>
    /// The validated configuration, plus every correction that had to be made to get there.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static ConfigLoadResult Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return new ConfigLoadResult(TrayMirrorConfig.Default, []);
        }
        catch (DirectoryNotFoundException)
        {
            return new ConfigLoadResult(TrayMirrorConfig.Default, []);
        }
        catch (IOException ex)
        {
            return Failed($"The config file could not be read ({ex.Message}). Defaults are in use.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Failed($"The config file could not be read ({ex.Message}). Defaults are in use.");
        }

        return Parse(text);
    }

    /// <summary>
    /// Validates config text that has already been read.
    /// </summary>
    /// <param name="text">The raw file contents, comments and all.</param>
    /// <returns>The validated configuration and any warnings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static ConfigLoadResult Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrWhiteSpace(text))
        {
            return new ConfigLoadResult(TrayMirrorConfig.Default, []);
        }

        ConfigDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(text, ConfigSerializerContext.Default.ConfigDocument);
        }
        catch (JsonException ex)
        {
            return Failed($"The config file is not valid JSON ({ex.Message}). Defaults are in use.");
        }

        if (document is null)
        {
            return new ConfigLoadResult(TrayMirrorConfig.Default, []);
        }

        var warnings = new List<string>();

        var config = new TrayMirrorConfig
        {
            StartWithWindows = document.StartWithWindows ?? TrayMirrorConfig.Default.StartWithWindows,
            DiagnosticsLog = document.DiagnosticsLog ?? TrayMirrorConfig.Default.DiagnosticsLog,
            Monitors = ReadMonitors(document.Monitors, warnings),
            Opacity = ClampOpacity(document.Opacity, "opacity", warnings) ?? TrayMirrorConfig.Default.Opacity,
            StripPaddingRight = ClampPadding(document.StripPaddingRight, "stripPaddingRight", warnings) ?? 0,
            PerMonitor = ReadOverrides(document.PerMonitor, warnings),
        };

        return new ConfigLoadResult(config, new ReadOnlyCollection<string>(warnings));
    }

    private static ConfigLoadResult Failed(string warning)
    {
        return new(TrayMirrorConfig.Default, new ReadOnlyCollection<string>([warning]));
    }

    private static ReadOnlyCollection<string> ReadMonitors(List<string>? names, List<string> warnings)
    {
        if (names is null || names.Count == 0)
        {
            return ReadOnlyCollection<string>.Empty;
        }

        var kept = new List<string>();
        foreach (string name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                warnings.Add("An empty entry in \"monitors\" was ignored.");
                continue;
            }

            kept.Add(name.Trim());
        }

        return new ReadOnlyCollection<string>(kept);
    }

    private static ReadOnlyDictionary<string, MonitorOverride> ReadOverrides(
        Dictionary<string, MonitorOverrideDocument>? overrides,
        List<string> warnings)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return ReadOnlyDictionary<string, MonitorOverride>.Empty;
        }

        var built = new Dictionary<string, MonitorOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrides)
        {
            if (string.IsNullOrWhiteSpace(key) || value is null)
            {
                warnings.Add("An empty entry in \"perMonitor\" was ignored.");
                continue;
            }

            built[key.Trim()] = new MonitorOverride(
                ClampOpacity(value.Opacity, $"perMonitor[\"{key}\"].opacity", warnings),
                ClampPadding(value.StripPaddingRight, $"perMonitor[\"{key}\"].stripPaddingRight", warnings));
        }

        return new ReadOnlyDictionary<string, MonitorOverride>(built);
    }

    private static double? ClampOpacity(double? value, string key, List<string> warnings)
    {
        if (value is not { } opacity)
        {
            return null;
        }

        if (double.IsNaN(opacity))
        {
            warnings.Add($"\"{key}\" was not a number and was ignored.");
            return null;
        }

        // A mirror at zero opacity is invisible and indistinguishable from a broken mirror, so the
        // floor is low enough to be clearly translucent and high enough to still be findable.
        double clamped = Math.Clamp(opacity, MinimumOpacity, 1.0);
        if (Math.Abs(clamped - opacity) > double.Epsilon)
        {
            warnings.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"\"{key}\" was {opacity} and has been clamped to {clamped}."));
        }

        return clamped;
    }

    private static int? ClampPadding(int? value, string key, List<string> warnings)
    {
        if (value is not { } padding)
        {
            return null;
        }

        int clamped = Math.Clamp(padding, -PaddingLimit, PaddingLimit);
        if (clamped != padding)
        {
            warnings.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"\"{key}\" was {padding} and has been clamped to {clamped}."));
        }

        return clamped;
    }

    /// <summary>
    /// The file written on first run. Every key is present and commented, so the file itself is
    /// the reference rather than something to look up.
    /// </summary>
    internal const string DefaultTemplate =
        """
        // traymirror configuration.
        //
        // Comments and trailing commas are allowed. traymirror re-reads this file whenever it
        // changes on disk, and "Reload config" in the tray menu forces a re-read.
        {
          // Register traymirror under HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
          "startWithWindows": true,

          // Secondary monitors to mirror onto, by GDI device name. An empty list means all of
          // them. Run traymirror with --probe to print the device names of this machine.
          // Naming the primary monitor here does nothing: it is the monitor being copied from.
          "monitors": [],

          // Mirror opacity, 0.05 to 1.0. Lower it slightly if the mirrored strip looks visually
          // detached from the secondary taskbar under a custom or high-contrast theme.
          "opacity": 1.0,

          // Extra pixels of gap between the mirror and the secondary clock. Increase it if the
          // detected clock boundary leaves the strip overlapping the time.
          "stripPaddingRight": 0,

          // Write a diagnostic log to %TEMP%\traymirror.log. Turn this on before filing a bug.
          "diagnosticsLog": false,

          // Per-monitor overrides for "opacity" and "stripPaddingRight". Keys are device names.
          // Anything not listed here falls back to the values above.
          "perMonitor": {
            // "\\\\.\\DISPLAY1": { "opacity": 0.92, "stripPaddingRight": 2 }
          }
        }

        """;
}

/// <summary>
/// The result of loading the config file.
/// </summary>
/// <param name="Config">The validated configuration, which is always usable.</param>
/// <param name="Warnings">
/// Human-readable descriptions of anything that had to be corrected or ignored.
/// </param>
public sealed record ConfigLoadResult(TrayMirrorConfig Config, IReadOnlyList<string> Warnings);
