using TrayMirror.Core.Configuration;
using Xunit;

namespace TrayMirror.Core.Tests;

public class ConfigLoaderTests
{
    [Fact]
    public void Parse_AcceptsCommentsAndTrailingCommas()
    {
        // The file is named .jsonc and ships with a comment above every key. A parser that
        // rejected comments would make the default file unloadable as soon as a user edited it.
        const string text = """
            {
              // the mirror opacity
              "opacity": 0.5,
              /* block comments too */
              "stripPaddingRight": 4,
            }
            """;

        ConfigLoadResult result = ConfigLoader.Parse(text);

        Assert.Empty(result.Warnings);
        Assert.Equal(0.5, result.Config.Opacity);
        Assert.Equal(4, result.Config.StripPaddingRight);
    }

    [Fact]
    public void Parse_TheShippedDefaultTemplate_ProducesTheDefaults()
    {
        ConfigLoadResult result = ConfigLoader.Parse(ConfigLoader.DefaultTemplate);

        Assert.Empty(result.Warnings);
        Assert.True(result.Config.StartWithWindows);
        Assert.Empty(result.Config.Monitors);
        Assert.Equal(1.0, result.Config.Opacity);
        Assert.Equal(0, result.Config.StripPaddingRight);
        Assert.False(result.Config.DiagnosticsLog);
        Assert.Empty(result.Config.PerMonitor);
    }

    [Fact]
    public void Parse_FallsBackToDefaults_WhenTheFileIsNotValidJson()
    {
        // Never throw on a hand-edited file. Refusing to start because of a stray brace would
        // leave the user with no mirrors and no way to see why.
        ConfigLoadResult result = ConfigLoader.Parse("{ this is not json");

        Assert.Single(result.Warnings);
        Assert.Equal(TrayMirrorConfig.Default, result.Config);
    }

    [Fact]
    public void Parse_TreatsAnEmptyObjectAsAllDefaults()
    {
        ConfigLoadResult result = ConfigLoader.Parse("{}");

        Assert.Empty(result.Warnings);
        Assert.True(result.Config.StartWithWindows);
    }

    [Theory]
    [InlineData(12.0, 1.0)]
    [InlineData(-3.0, 0.05)]
    [InlineData(0.0, 0.05)]
    public void Parse_ClampsOpacityAndSaysSo(double given, double expected)
    {
        ConfigLoadResult result = ConfigLoader.Parse($$"""{ "opacity": {{given}} }""");

        Assert.Equal(expected, result.Config.Opacity);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Parse_ClampsPaddingAndSaysSo()
    {
        ConfigLoadResult result = ConfigLoader.Parse("""{ "stripPaddingRight": 99999 }""");

        Assert.Equal(512, result.Config.StripPaddingRight);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Parse_ReadsTheMonitorAllowList()
    {
        ConfigLoadResult result = ConfigLoader.Parse("""{ "monitors": ["\\\\.\\DISPLAY1"] }""");

        Assert.Equal([@"\\.\DISPLAY1"], result.Config.Monitors);
    }

    [Fact]
    public void Parse_IgnoresAnEmptyMonitorEntryAndSaysSo()
    {
        ConfigLoadResult result = ConfigLoader.Parse("""{ "monitors": ["", "\\\\.\\DISPLAY1"] }""");

        Assert.Single(result.Config.Monitors);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void SettingsFor_LayersAPerMonitorOverrideOverTheTopLevelValues()
    {
        ConfigLoadResult result = ConfigLoader.Parse("""
            {
              "opacity": 1.0,
              "stripPaddingRight": 0,
              "perMonitor": {
                "\\\\.\\DISPLAY1": { "opacity": 0.92 }
              }
            }
            """);

        MonitorSettings overridden = result.Config.SettingsFor(@"\\.\DISPLAY1");
        MonitorSettings inherited = result.Config.SettingsFor(@"\\.\DISPLAY3");

        // Only the listed key is overridden; the rest still comes from the top level.
        Assert.Equal(0.92, overridden.Opacity);
        Assert.Equal(0, overridden.StripPaddingRight);
        Assert.Equal(1.0, inherited.Opacity);
    }

    [Fact]
    public void SettingsFor_MatchesDeviceNamesCaseInsensitively()
    {
        ConfigLoadResult result = ConfigLoader.Parse("""
            { "perMonitor": { "\\\\.\\display1": { "stripPaddingRight": 6 } } }
            """);

        Assert.Equal(6, result.Config.SettingsFor(@"\\.\DISPLAY1").StripPaddingRight);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenTheFileDoesNotExist()
    {
        string path = Path.Combine(Path.GetTempPath(), $"traymirror-missing-{Guid.NewGuid():N}.jsonc");

        ConfigLoadResult result = ConfigLoader.Load(path);

        Assert.Equal(TrayMirrorConfig.Default, result.Config);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void EnsureExists_WritesATemplateThatLoadsCleanly()
    {
        string path = Path.Combine(Path.GetTempPath(), $"traymirror-{Guid.NewGuid():N}", ConfigLoader.FileName);

        try
        {
            Assert.True(ConfigLoader.EnsureExists(path));
            Assert.False(ConfigLoader.EnsureExists(path));

            ConfigLoadResult result = ConfigLoader.Load(path);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
