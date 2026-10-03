namespace TrayMirror.Core.Monitors;

/// <summary>
/// The outcome of matching the configured monitor allow list against the displays actually
/// connected.
/// </summary>
/// <param name="Targets">The secondary monitors that should receive a mirror.</param>
/// <param name="UnmatchedNames">
/// Configured device names that matched no connected secondary monitor. Reported separately
/// rather than dropped, because a typo in a device name otherwise turns into an application that
/// starts, logs nothing and mirrors nowhere.
/// </param>
public sealed record MonitorSelection(
    IReadOnlyList<MonitorInfo> Targets,
    IReadOnlyList<string> UnmatchedNames);
