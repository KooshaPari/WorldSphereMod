using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

public class BakeInfrastructureIntegrationTests
{
    private static string RepoRoot => TestRepo.FindRoot();

    [Fact]
    public void Bake_shaders_script_exists_at_Tools_bake_shaders_ps1()
    {
        var scriptPath = Path.Combine(RepoRoot, "Tools", "bake-shaders.ps1");
        File.Exists(scriptPath).Should().BeTrue(
            "shader bundle bake must be invocable via Tools/bake-shaders.ps1");
    }

    [Fact]
    public void Unity_Bake_Project_exists_with_editor_bake_entrypoint()
    {
        var projectRoot = Path.Combine(RepoRoot, "Tools", "Unity-Bake-Project");
        Directory.Exists(projectRoot).Should().BeTrue(
            "headless bake requires Tools/Unity-Bake-Project");

        var projectVersion = Path.Combine(projectRoot, "ProjectSettings", "ProjectVersion.txt");
        File.Exists(projectVersion).Should().BeTrue(
            "Unity bake project must declare editor version in ProjectVersion.txt");

        var bakeShaders = Path.Combine(projectRoot, "Assets", "Editor", "BakeShaders.cs");
        File.Exists(bakeShaders).Should().BeTrue(
            "BakeShaders.BakeAll must be present for -executeMethod in bake-shaders.ps1");
    }

    [Fact]
    public void Bake_shaders_script_targets_Unity_Bake_Project_and_BakeShaders_BakeAll()
    {
        var script = TestRepo.ReadRelative("Tools/bake-shaders.ps1");

        script.Should().Contain("Tools/Unity-Bake-Project");
        script.Should().Contain("-executeMethod");
        script.Should().Contain("BakeShaders.BakeAll");
    }

    [Fact]
    public void Bake_shaders_script_prefers_Unity_2022_3_and_requires_UnityExe_when_missing()
    {
        var script = TestRepo.ReadRelative("Tools/bake-shaders.ps1");

        script.Should().Contain("2022.3");
        script.Should().Contain("-UnityExe");
        script.Should().MatchRegex(@"2022\.3\.", "auto-detect must scan Hub Editor folders for 2022.3.x");
        script.Should().Contain("Write-BakeNextSteps");
        script.Should().Contain("Test-ProjectVersionRecommends2022");
        script.Should().Contain("unity-version-blocker.md");
    }

    [Fact]
    public void Bake_shaders_script_does_not_prefer_wrong_unity_majors_in_auto_detect()
    {
        var script = TestRepo.ReadRelative("Tools/bake-shaders.ps1");

        script.Should().NotContain("6000.3");
        var prefers2021InCandidates = Regex.IsMatch(
            script,
            @"candidates\s*=\s*@\(.*2021\.3",
            RegexOptions.Singleline);
        prefers2021InCandidates.Should().BeFalse("auto-detect must not list 2021.3 as a preferred candidate");
    }
}
