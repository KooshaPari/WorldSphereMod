using System.IO;
using System.Linq;
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

    private static JsonDocument LoadModJson(string root)
    {
        var modJsonPath = Path.Combine(root, "WorldSphereMod", "mod.json");
        File.Exists(modJsonPath).Should().BeTrue($"WorldSphereMod/mod.json must exist at {modJsonPath}");
        return JsonDocument.Parse(File.ReadAllText(modJsonPath));
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

    [Fact]
    public void Mod_json_has_correct_guid()
    {
        var root = FindRepoRoot();
        using var doc = LoadModJson(root);

        doc.RootElement.TryGetProperty("GUID", out var guid).Should().BeTrue();
        guid.GetString().Should().Be(
            "worldsphere3d.fork",
            "the fork's GUID must remain stable so it stays co-installable with upstream WorldSphereMod");
    }

    [Fact]
    public void Mod_json_version_matches_version_file()
    {
        var root = FindRepoRoot();

        var versionPath = Path.Combine(root, "VERSION");
        File.Exists(versionPath).Should().BeTrue($"VERSION must exist at {versionPath}");
        var versionFile = File.ReadAllText(versionPath).Trim();
        versionFile.Should().NotBeNullOrWhiteSpace("VERSION must declare a non-empty version string");

        using var doc = LoadModJson(root);
        doc.RootElement.TryGetProperty("version", out var versionProp).Should().BeTrue(
            "mod.json must declare a 'version' field");
        var modJsonVersion = versionProp.GetString();

        modJsonVersion.Should().Be(
            versionFile,
            "mod.json version must stay in lockstep with the VERSION file — both drive release artifacts");
    }

    [Fact]
    public void Mod_json_version_matches_changelog_release_section()
    {
        var root = FindRepoRoot();
        var versionFile = File.ReadAllText(Path.Combine(root, "VERSION")).Trim();
        versionFile.Should().NotBeNullOrWhiteSpace();

        var changelogPath = Path.Combine(root, "CHANGELOG.md");
        File.Exists(changelogPath).Should().BeTrue($"CHANGELOG.md must exist at {changelogPath}");
        var changelog = File.ReadAllText(changelogPath);

        changelog.Should().Contain(
            $"## [{versionFile}]",
            "CHANGELOG.md must declare a release section for the current VERSION — release.yml reads [Unreleased] or ## [<version>]");

        using var doc = LoadModJson(root);
        doc.RootElement.GetProperty("version").GetString().Should().Be(versionFile);
    }

    [Fact]
    public void Phase_architecture_docs_exist()
    {
        var root = FindRepoRoot();
        var docsDir = Path.Combine(root, "docs");
        Directory.Exists(docsDir).Should().BeTrue($"docs/ must exist at {docsDir}");

        // For each phase 1..10, accept either phase{N}-architecture.md or
        // an explicit alias (phase 1 ships its summary as phase1-review.md
        // rather than phase1-architecture.md).
        for (int phase = 1; phase <= 10; phase++)
        {
            var primary = Path.Combine(docsDir, $"phase{phase}-architecture.md");
            var aliases = new[]
            {
                primary,
                Path.Combine(docsDir, $"phase{phase}-review.md"),
                Path.Combine(docsDir, $"phase{phase}-prep.md"),
            };

            aliases.Any(File.Exists).Should().BeTrue(
                $"phase {phase} architecture doc must exist at one of: " +
                string.Join(", ", aliases.Select(Path.GetFileName)));
        }
    }

    [Fact]
    public void Adr_index_listed()
    {
        var root = FindRepoRoot();
        var adrDir = Path.Combine(root, "docs", "adr");
        Directory.Exists(adrDir).Should().BeTrue($"docs/adr/ must exist at {adrDir}");

        var indexPath = Path.Combine(adrDir, "index.md");
        File.Exists(indexPath).Should().BeTrue($"docs/adr/index.md must exist at {indexPath}");
        var indexText = File.ReadAllText(indexPath);

        var adrFiles = Directory.GetFiles(adrDir, "00*-*.md");
        adrFiles.Should().NotBeEmpty("repo must contain at least one ADR (0001-…)");

        foreach (var adrFile in adrFiles)
        {
            // Index entries reference ADRs without the .md extension, e.g.
            // [0001](/adr/0001-hybrid-sprite-to-3d-strategy). Check by stem so
            // both `…0001-foo.md` and `…0001-foo` references pass.
            var stem = Path.GetFileNameWithoutExtension(adrFile);
            indexText.Should().Contain(
                stem,
                $"ADR file '{Path.GetFileName(adrFile)}' must be listed in docs/adr/index.md");
        }
    }
}
