using System.Collections.ObjectModel;

namespace TrayMirror.Core.Monitors;

/// <summary>
/// Decides which monitors get a mirror.
/// </summary>
/// <remarks>
/// Pure set logic over an already-enumerated topology, so every rule below is unit tested against
/// arrangements that would otherwise need physical hardware to reproduce.
/// </remarks>
public static class MonitorTargets
{
    /// <summary>
    /// Selects the monitors to mirror onto.
    /// </summary>
    /// <param name="monitors">Every connected monitor.</param>
    /// <param name="allowList">
    /// Device names from the <c>monitors</c> config key. An empty list means every secondary
    /// monitor.
    /// </param>
    /// <returns>The selection, including any configured name that matched nothing.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="monitors"/> or <paramref name="allowList"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The primary monitor is never a target. It is the monitor being copied from, so mirroring
    /// onto it would draw a live copy of a strip that is already there.
    /// </remarks>
    public static MonitorSelection Select(IReadOnlyList<MonitorInfo> monitors, IReadOnlyList<string> allowList)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(allowList);

        var secondaries = monitors.Where(m => !m.IsPrimary).ToList();

        if (allowList.Count == 0)
        {
            return new MonitorSelection(
                new ReadOnlyCollection<MonitorInfo>(secondaries),
                []);
        }

        var targets = new List<MonitorInfo>();
        var unmatched = new List<string>();

        foreach (string name in allowList)
        {
            var match = secondaries.FirstOrDefault(
                m => string.Equals(m.DeviceName, name, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                unmatched.Add(name);
            }
            else if (!targets.Contains(match))
            {
                targets.Add(match);
            }
        }

        return new MonitorSelection(
            new ReadOnlyCollection<MonitorInfo>(targets),
            new ReadOnlyCollection<string>(unmatched));
    }
}
