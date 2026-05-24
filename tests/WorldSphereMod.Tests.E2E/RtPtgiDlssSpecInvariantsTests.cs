using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// Source invariants for docs/journeys/scratch/rt-ptgi-dlss-spec.md — advanced
/// lighting strategy (built-in screen-space stack vs PTGI/DLSS/RT) is research-only.
/// </summary>
public sealed class RtPtgiDlssSpecInvariantsTests
{
    const string SpecRelativePath = "docs/journeys/scratch/rt-ptgi-dlss-spec.md";

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("repo root with WorldSphereMod.sln must be locatable from test cwd");
        return dir!.FullName;
    }

    static string ReadSpec()
    {
        var path = Path.Combine(FindRepoRoot(), SpecRelativePath);
        File.Exists(path).Should().BeTrue($"RT/PTGI/DLSS spec must exist on disk at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Rt_ptgi_dlss_spec_exists_and_documents_research_deferred_status()
    {
        var spec = ReadSpec();

        spec.Should().Contain("## 7) Implementation status",
            "spec must track rollout status for RT/PTGI/DLSS lighting work");
        spec.Should().Contain("Research / deferred",
            "advanced lighting path remains decision-only until phased rollout");
        spec.Should().Contain("BuiltInLightingScreenSpace",
            "spec must name the planned feature toggle for the built-in stack");
        spec.Should().Contain("RtPtgiDlssSpecInvariantsTests",
            "spec must point maintainers at e2e guardrails");
    }
}
