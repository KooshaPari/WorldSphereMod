using System.IO;
using System.Text.Json;
using FluentAssertions;
using Xunit;

public class ModJsonManifestIntegrationTests
{
    private static string ModJsonPath => Path.Combine(TestRepo.FindRoot(), "WorldSphereMod", "mod.json");

    private static JsonDocument LoadModJson()
    {
        File.Exists(ModJsonPath).Should().BeTrue($"mod.json must exist at {ModJsonPath}");
        return JsonDocument.Parse(File.ReadAllText(ModJsonPath));
    }

    [Fact]
    public void Mod_json_is_valid_and_declares_required_neomodloader_fields()
    {
        using var doc = LoadModJson();
        var root = doc.RootElement;

        foreach (var field in new[] { "name", "author", "version", "description", "GUID", "iconPath" })
        {
            root.TryGetProperty(field, out var prop).Should().BeTrue($"mod.json must declare '{field}'");
            prop.GetString().Should().NotBeNullOrWhiteSpace($"'{field}' must be non-empty");
        }
    }

    [Fact]
    public void Mod_json_guid_is_stable_fork_identifier()
    {
        using var doc = LoadModJson();
        doc.RootElement.GetProperty("GUID").GetString().Should().Be(
            "worldsphere3d.fork",
            "fork GUID must stay stable for co-install with upstream WorldSphereMod");
    }

    [Fact]
    public void Mod_json_version_matches_VERSION_file()
    {
        var root = TestRepo.FindRoot();
        var versionPath = Path.Combine(root, "VERSION");
        File.Exists(versionPath).Should().BeTrue();

        var versionFile = File.ReadAllText(versionPath).Trim();
        using var doc = LoadModJson();

        doc.RootElement.GetProperty("version").GetString().Should().Be(
            versionFile,
            "mod.json version must stay in lockstep with VERSION");
    }

    [Fact]
    public void Mod_json_iconPath_points_at_existing_asset()
    {
        using var doc = LoadModJson();
        var iconRelative = doc.RootElement.GetProperty("iconPath").GetString()!;
        var iconFull = Path.Combine(TestRepo.FindRoot(), "WorldSphereMod", iconRelative.Replace('/', Path.DirectorySeparatorChar));

        File.Exists(iconFull).Should().BeTrue(
            $"mod.json iconPath '{iconRelative}' must resolve to an on-disk asset");
    }

    [Fact]
    public void Mod_json_name_matches_install_folder_convention()
    {
        using var doc = LoadModJson();
        doc.RootElement.GetProperty("name").GetString().Should().Be(
            "WorldSphereMod3D",
            "mod display name must match Tools/install.ps1 InstallFolderName default");
    }
}
