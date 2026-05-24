using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

public class CiWorkflowInvariantsTests
{
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

    private static string ReadRepoFile(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(path).Should().BeTrue($"{relativePath} must exist at {path}");
        return File.ReadAllText(path);
    }

    private static IReadOnlyList<string> LoadStubManifestRelativePaths()
    {
        var manifestPath = Path.Combine(FindRepoRoot(), "Tools", "ci-worldbox-ref-dlls.manifest");
        File.Exists(manifestPath).Should().BeTrue($"stub manifest must exist at {manifestPath}");

        return File.ReadAllLines(manifestPath)
            .Select(line => line.Split('#')[0].Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    private static readonly IReadOnlyDictionary<string, string> WorldBoxHintPrefixes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["WorldBoxManaged"] = "worldbox_Data/Managed",
            ["WorldBoxNML"] = "worldbox_Data/StreamingAssets/mods/NML",
            ["WorldBoxNMLAssemblies"] = "worldbox_Data/StreamingAssets/mods/NML/Assemblies",
            ["WorldBoxPath"] = string.Empty,
        };

    private static IReadOnlySet<string> LoadCsprojWorldBoxHintRelativePaths()
    {
        var csproj = ReadRepoFile("WorldSphereMod.csproj");
        var matches = Regex.Matches(
            csproj,
            @"<HintPath>\$\((?<prefix>WorldBoxManaged|WorldBoxNML|WorldBoxNMLAssemblies|WorldBoxPath)\)/(?<suffix>[^<]+)</HintPath>",
            RegexOptions.CultureInvariant);

        matches.Count.Should().BeGreaterThan(0, "WorldSphereMod.csproj must declare WorldBox HintPath references");

        return matches
            .Cast<Match>()
            .Select(m =>
            {
                var prefix = m.Groups["prefix"].Value;
                var suffix = m.Groups["suffix"].Value.Replace('\\', '/');
                WorldBoxHintPrefixes.Should().ContainKey(prefix);
                var root = WorldBoxHintPrefixes[prefix];
                return string.IsNullOrEmpty(root) ? suffix : $"{root}/{suffix}";
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stub_manifest_covers_every_WorldBox_HintPath_in_the_mod_csproj()
    {
        var manifestPaths = LoadStubManifestRelativePaths();
        var csprojPaths = LoadCsprojWorldBoxHintRelativePaths();

        foreach (var hint in csprojPaths)
        {
            manifestPaths.Should().Contain(
                hint,
                $"Tools/ci-worldbox-ref-dlls.manifest must list {hint} so CI stubs match WorldSphereMod.csproj");
        }
    }

    [Fact]
    public void Stub_manifest_places_NeoModLoader_at_StreamingAssets_mods_root()
    {
        var manifestPaths = LoadStubManifestRelativePaths();
        manifestPaths.Should().Contain(
            "worldbox_Data/StreamingAssets/mods/NeoModLoader.dll",
            "NeoModLoader.dll lives next to the mods folder entries, not under mods/NML/");
        manifestPaths.Should().NotContain(
            "worldbox_Data/StreamingAssets/mods/NML/NeoModLoader.dll",
            "a stale NML-scoped NeoModLoader path breaks HintPath resolution in CI");
    }

    [Theory]
    [InlineData(".github/workflows/build.yml")]
    [InlineData(".github/workflows/test-gate.yml")]
    [InlineData(".github/workflows/lint-gate.yml")]
    [InlineData(".github/workflows/release.yml")]
    public void Workflows_use_the_shared_ci_stub_script(string workflowRelativePath)
    {
        var yaml = ReadRepoFile(workflowRelativePath);
        yaml.Should().Contain(
            "Tools/ci-stub-worldbox-refs.sh",
            $"{workflowRelativePath} must call the canonical stub script so DLL paths stay centralized");
    }

    [Fact]
    public void Build_workflow_gates_WorldSphereAPI_and_documents_mod_build_as_best_effort()
    {
        var yaml = ReadRepoFile(".github/workflows/build.yml");

        yaml.Should().Contain("dotnet build WorldSphereAPI.csproj",
            "CI must keep building the Unity-free API on every push/PR");
        yaml.Should().Contain("continue-on-error: true",
            "main mod build must remain explicitly non-blocking until compilable stubs or a self-hosted runner exist");
        yaml.Should().Contain("WorldSphereMod.csproj",
            "build.yml should still attempt the mod project for early signal");
        yaml.Should().Contain("ci-mod-compile-gap.md",
            "build.yml should point maintainers at the compile-gap doc");
    }

    [Fact]
    public void Ci_mod_compile_gap_doc_exists_and_links_stub_manifest()
    {
        var doc = ReadRepoFile("docs/ci-mod-compile-gap.md");
        doc.Should().Contain("ci-worldbox-ref-dlls.manifest");
        doc.Should().Contain("WorldSphereAPI");
        doc.Should().Contain("zero-byte", "the doc must explain why placeholders cannot compile the mod");
    }
}
