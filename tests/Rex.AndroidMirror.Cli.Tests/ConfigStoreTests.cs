using System.Text.Json;
using Rex.AndroidMirror.Cli;

namespace Rex.AndroidMirror.Cli.Tests;

public sealed class ConfigStoreTests
{
    [Fact]
    public void Flatten_ExposesNestedAndTopLevelSettings()
    {
        using var package = new TempPackage();

        var rows = package.Config.Flatten();

        Assert.Contains(rows, x => x.Path == "TurnPhysicalScreenOff" && x.Value == "true");
        Assert.Contains(rows, x => x.Path == "MirrorChrome.MaxZoom" && x.Value == "4");
        Assert.Contains(rows, x => x.Path == "ScrcpySession.VideoCodec" && x.Value == "h264");
        Assert.Contains(rows, x => x.Path == "Wireless.ManualHosts" && x.Kind == JsonValueKind.Array);
    }

    [Fact]
    public void Get_ReturnsTypeAndValue()
    {
        using var package = new TempPackage();

        var leaf = package.Config.Get("MaxFps");

        Assert.Equal("60", leaf.Value);
        Assert.Equal(JsonValueKind.Number, leaf.Kind);
    }

    [Theory]
    [InlineData("TurnPhysicalScreenOff", "false", "false")]
    [InlineData("MaxFps", "90", "90")]
    [InlineData("VideoBitRate", "10M", "10M")]
    [InlineData("MirrorChrome.MaxZoom", "3.5", "3.5")]
    public void Set_PreservesExistingJsonType(string path, string value, string expected)
    {
        using var package = new TempPackage();

        package.Config.Set(path, value);

        Assert.Equal(expected, package.Config.Get(path).Value);
    }

    [Fact]
    public void EnsureBooleanDefault_AddsMissingTopLevelSettingAndPreservesExistingValues()
    {
        using var package = new TempPackage();
        var json = System.Text.Json.Nodes.JsonNode.Parse(
            File.ReadAllText(package.Paths.Config))!.AsObject();
        json.Remove("TurnPhysicalScreenOff");
        File.WriteAllText(package.Paths.Config, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        Assert.True(package.Config.EnsureBooleanDefault("TurnPhysicalScreenOff", true));
        Assert.Equal("true", package.Config.Get("TurnPhysicalScreenOff").Value);

        package.Config.Set("TurnPhysicalScreenOff", "false");
        Assert.False(package.Config.EnsureBooleanDefault("TurnPhysicalScreenOff", true));
        Assert.Equal("false", package.Config.Get("TurnPhysicalScreenOff").Value);
    }

    [Fact]
    public void Set_Array_AcceptsValidJson()
    {
        using var package = new TempPackage();

        package.Config.Set("Wireless.ManualHosts", """["192.168.1.20","10.0.0.3"]""");

        var row = package.Config.Get("Wireless.ManualHosts");
        Assert.Contains("192.168.1.20", row.Value);
        Assert.Contains("10.0.0.3", row.Value);
    }

    [Fact]
    public void Set_InvalidBoolean_IsRejectedWithoutChangingFile()
    {
        using var package = new TempPackage();
        var before = File.ReadAllText(package.Paths.Config);

        Assert.Throws<FormatException>(() =>
            package.Config.Set("TurnPhysicalScreenOff", "sometimes"));

        Assert.Equal(before, File.ReadAllText(package.Paths.Config));
    }

    [Fact]
    public void Set_InvalidNumber_IsRejected()
    {
        using var package = new TempPackage();

        Assert.Throws<FormatException>(() =>
            package.Config.Set("MaxFps", "fast"));
    }

    [Fact]
    public void Set_UnknownPath_IsRejected()
    {
        using var package = new TempPackage();

        Assert.Throws<KeyNotFoundException>(() =>
            package.Config.Set("Does.Not.Exist", "1"));
    }

    [Fact]
    public void Set_CreatesBackupOfPreviousGoodConfig()
    {
        using var package = new TempPackage();
        var original = File.ReadAllText(package.Paths.Config);

        package.Config.Set("MaxFps", "90");

        Assert.True(File.Exists(package.Config.BackupPath));
        Assert.Equal(original, File.ReadAllText(package.Config.BackupPath));
    }

    [Fact]
    public void RestoreBackup_SwapsCurrentAndPreviousConfig()
    {
        using var package = new TempPackage();

        package.Config.Set("MaxFps", "90");
        Assert.Equal("90", package.Config.Get("MaxFps").Value);

        package.Config.RestoreBackup();
        Assert.Equal("60", package.Config.Get("MaxFps").Value);

        package.Config.RestoreBackup();
        Assert.Equal("90", package.Config.Get("MaxFps").Value);
    }

    [Fact]
    public void RestoreBackup_WithoutBackup_IsRejected()
    {
        using var package = new TempPackage();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            package.Config.RestoreBackup());

        Assert.Contains("No previous REX config backup", ex.Message);
    }

    [Fact]
    public void SavedConfig_RemainsParseableJson()
    {
        using var package = new TempPackage();

        package.Config.Set("ControlCenter.OpenOnLaunch", "true");

        using var doc = JsonDocument.Parse(File.ReadAllText(package.Paths.Config));
        Assert.True(doc.RootElement.GetProperty("ControlCenter").GetProperty("OpenOnLaunch").GetBoolean());
    }
}
