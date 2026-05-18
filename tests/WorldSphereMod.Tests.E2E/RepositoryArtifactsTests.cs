using System.IO;
using System.Text.Json;
using Xunit;
using FluentAssertions;

public class RepositoryArtifactsTests
{
    // Walk up from the test binary's output dir until we find the repo root
    // (identified by WorldSphereMod.sln). Avoids hard-coding absolute paths
    // so this test runs identically on CI runners and dev machines.
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("repo root with WorldSphereMod.sln must be locatable from test cwd");
        return dir!.FullName;
    }

    [Fact]
    public void Install_script_exists_and_mod_json_is_valid()
    {
        var root = FindRepoRoot();

        var installScript = Path.Combine(root, "Tools", "install.ps1");
        File.Exists(installScript).Should().BeTrue($"Tools/install.ps1 must exist at {installScript}");

        var modJsonPath = Path.Combine(root, "WorldSphereMod", "mod.json");
        File.Exists(modJsonPath).Should().BeTrue($"WorldSphereMod/mod.json must exist at {modJsonPath}");

        var modJsonText = File.ReadAllText(modJsonPath);
        var parse = () => JsonDocument.Parse(modJsonText);
        parse.Should().NotThrow("mod.json must be valid JSON");

        using var doc = JsonDocument.Parse(modJsonText);
        doc.RootElement.TryGetProperty("GUID", out var guid).Should().BeTrue("mod.json must declare GUID");
        guid.GetString().Should().NotBeNullOrWhiteSpace();
    }
}
