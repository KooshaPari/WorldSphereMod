using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

public class CodeownersGovernanceTests
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

    private static IReadOnlyList<string> LoadCodeownersLines()
    {
        var path = Path.Combine(FindRepoRoot(), ".github", "CODEOWNERS");
        File.Exists(path).Should().BeTrue($".github/CODEOWNERS must exist at {path}");

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
            .ToList();
    }

    [Fact]
    public void Codeowners_covers_core_repo_paths_and_release_metadata()
    {
        var lines = LoadCodeownersLines();

        var expectedPatterns = new[]
        {
            "*",
            "/WorldSphereMod/Code/",
            "/WorldSphereAPI/",
            "/Tools/",
            "/.github/workflows/",
            "/docs/",
            "/docs/journeys/",
            "/tests/",
            "/CHANGELOG.md",
            "/RELEASING.md",
            "/VERSION",
            "/docs/release-notes/",
        };

        foreach (var pattern in expectedPatterns)
        {
            lines.Should().Contain(line => line.StartsWith(pattern, StringComparison.Ordinal),
                $"CODEOWNERS must include a rule for {pattern}");
        }
    }

    [Fact]
    public void Codeowners_uses_repo_owner_handle_for_the_main_rules()
    {
        var lines = LoadCodeownersLines();

        lines.Should().OnlyContain(line => line.Contains("@KooshaPari", StringComparison.Ordinal),
            "the repo owner handle should remain the assigned owner for the current governance layout");
    }
}
