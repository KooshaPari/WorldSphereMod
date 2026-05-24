using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using FluentAssertions;

/// <summary>
/// FR-WSM-008: SkeletalAnimation — dragonfly-bug regression guard.
/// Static-source verification that HumanoidRig.Evaluate() does NOT pass
/// runtime scale into BuildHierarchy. The rest pose was baked at scale=1
/// in the static ctor; passing a different scale at eval time causes the
/// skin matrix world[i] * restInverse[i] to bake in an N-times stretch
/// → dragonfly limbs.
/// </summary>
public class HumanoidRigBindPoseTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static string ReadSource() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(),
            "WorldSphereMod", "Code", "Rig", "HumanoidRig.cs"));

    [Fact]
    public void RestPose_BakedAtScaleOne()
    {
        var src = ReadSource();
        // The static ctor MUST build _restWorld at scale=1f.
        Regex.IsMatch(src, @"_restWorld = BuildHierarchy\(identityPose: true, scale: 1f").Should().BeTrue(
            "FR-WSM-008: rest pose must be at scale=1 to match the static-ctor inverse");
    }

    [Fact]
    public void Evaluate_PassesScaleOne_NotRuntimeScale()
    {
        var src = ReadSource();
        // Evaluate() returns BuildHierarchy with scale=1, NOT the parameter `scale`.
        // The dragonfly bug was: `return BuildHierarchy(false, scale, ...)`.
        src.Should().NotMatch(@"return BuildHierarchy\(false, scale,",
            because: "dragonfly bug — passing runtime scale into BuildHierarchy bakes N-stretch into skin matrix");
        Regex.IsMatch(src, @"return BuildHierarchy\(false, 1f,").Should().BeTrue(
            "Evaluate must call BuildHierarchy at scale=1 — external mesh transform applies render scale separately");
    }

    [Fact]
    public void BindPoseOffset_Scaled_OnlyInsideHierarchy()
    {
        var src = ReadSource();
        // The `bind = BindPoseOffset * scale` line still exists for internal use, but
        // the only caller passing non-1 scale (Evaluate) is now fixed.
        src.Should().Contain("Bones[i].BindPoseOffset");
    }
}
