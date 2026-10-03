using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrayMirror.Core.Configuration;

/// <summary>
/// The wire shape of the config file.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="TrayMirrorConfig"/> so the public model can be immutable and
/// fully populated while the on-disk model stays entirely nullable. Every key is optional: a file
/// containing only <c>{}</c> is valid and means "all defaults", and a file that sets one key is
/// not required to restate the rest.
/// </remarks>
internal sealed class ConfigDocument
{
    public bool? StartWithWindows { get; set; }

    public List<string>? Monitors { get; set; }

    public double? Opacity { get; set; }

    public int? StripPaddingRight { get; set; }

    public bool? DiagnosticsLog { get; set; }

    public Dictionary<string, MonitorOverrideDocument>? PerMonitor { get; set; }
}

/// <summary>The wire shape of one <c>perMonitor</c> entry.</summary>
internal sealed class MonitorOverrideDocument
{
    public double? Opacity { get; set; }

    public int? StripPaddingRight { get; set; }
}

/// <summary>
/// The source-generated serialiser context for the config file.
/// </summary>
/// <remarks>
/// Source generation rather than reflection keeps the single-file publish free of trimming and
/// AOT warnings, and it makes the accepted key set visible at compile time.
/// <c>JsonCommentHandling.Skip</c> is what makes the <c>.jsonc</c> extension honest: the default
/// template ships with a comment above every key, and a parser that rejected comments would make
/// the file unloadable the moment a user edited it in place.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    WriteIndented = true)]
[JsonSerializable(typeof(ConfigDocument))]
[JsonSerializable(typeof(MonitorOverrideDocument))]
internal sealed partial class ConfigSerializerContext : JsonSerializerContext;
