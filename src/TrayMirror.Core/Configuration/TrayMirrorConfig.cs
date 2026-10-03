using System.Collections.ObjectModel;

namespace TrayMirror.Core.Configuration;

/// <summary>
/// The validated contents of <c>%APPDATA%\TrayMirror\traymirror.jsonc</c>.
/// </summary>
/// <remarks>
/// Immutable on purpose. A reload produces a new instance and the controller swaps it in on the UI
/// thread, so no half-applied configuration can ever be observed by the rebuild logic.
/// </remarks>
public sealed record TrayMirrorConfig
{
    /// <summary>The configuration traymirror uses when no file exists yet.</summary>
    public static TrayMirrorConfig Default { get; } = new();

    /// <summary>
    /// Gets a value indicating whether traymirror registers itself under the current user's
    /// <c>Run</c> key.
    /// </summary>
    /// <remarks>
    /// This lives in the config file rather than only in the tray menu so that a managed machine
    /// can set it from a script or a policy without traymirror overwriting the decision on next
    /// launch.
    /// </remarks>
    public bool StartWithWindows { get; init; } = true;

    /// <summary>
    /// Gets the device names of the secondary monitors to mirror onto, for example
    /// <c>\\.\DISPLAY1</c>. Empty means every secondary monitor.
    /// </summary>
    public IReadOnlyList<string> Monitors { get; init; } = [];

    /// <summary>Gets the mirror window opacity, from 0.0 to 1.0.</summary>
    public double Opacity { get; init; } = 1.0;

    /// <summary>
    /// Gets the extra gap in physical pixels between the mirror and the secondary clock.
    /// </summary>
    public int StripPaddingRight { get; init; }

    /// <summary>
    /// Gets a value indicating whether traymirror writes a diagnostic log to
    /// <c>%TEMP%\traymirror.log</c>.
    /// </summary>
    public bool DiagnosticsLog { get; init; }

    /// <summary>
    /// Gets per-monitor overrides for <see cref="Opacity"/> and <see cref="StripPaddingRight"/>,
    /// keyed on device name.
    /// </summary>
    public IReadOnlyDictionary<string, MonitorOverride> PerMonitor { get; init; } =
        ReadOnlyDictionary<string, MonitorOverride>.Empty;

    /// <summary>
    /// Resolves the effective settings for one monitor by layering its override, if any, over the
    /// top-level values.
    /// </summary>
    /// <param name="deviceName">The monitor's device name.</param>
    /// <returns>The settings to apply to that monitor's mirror.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="deviceName"/> is <see langword="null"/>.</exception>
    public MonitorSettings SettingsFor(string deviceName)
    {
        ArgumentNullException.ThrowIfNull(deviceName);

        var match = PerMonitor.FirstOrDefault(
            pair => string.Equals(pair.Key, deviceName, StringComparison.OrdinalIgnoreCase)).Value;

        return new MonitorSettings(
            match?.Opacity ?? Opacity,
            match?.StripPaddingRight ?? StripPaddingRight);
    }
}

/// <summary>
/// The subset of settings that can be overridden for a single monitor.
/// </summary>
/// <param name="Opacity">The mirror opacity, or <see langword="null"/> to inherit.</param>
/// <param name="StripPaddingRight">The right-hand gap, or <see langword="null"/> to inherit.</param>
public sealed record MonitorOverride(double? Opacity, int? StripPaddingRight);

/// <summary>
/// The effective settings for one monitor after overrides are merged.
/// </summary>
/// <param name="Opacity">The mirror opacity, from 0.0 to 1.0.</param>
/// <param name="StripPaddingRight">The gap in physical pixels between the mirror and the clock.</param>
public readonly record struct MonitorSettings(double Opacity, int StripPaddingRight);
