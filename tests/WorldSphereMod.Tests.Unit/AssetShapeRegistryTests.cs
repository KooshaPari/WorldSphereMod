using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using FluentAssertions;

/// <summary>
/// FR-WSM-003: Per-Sprite Shape-Hint Routing — static-source verification.
/// Cannot instantiate AssetShapeRegistry without Unity, so we verify the
/// source file contains the expected prefix → ShapeHint pairs.
/// </summary>
public class AssetShapeRegistryTests
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
            "WorldSphereMod", "Code", "Voxel", "AssetShapeRegistry.cs"));

    [Theory]
    [InlineData("human", "Flat")]
    [InlineData("tree", "Cylinder")]
    [InlineData("bush", "Cylinder")]
    [InlineData("wolf", "Flat")]
    [InlineData("wall", "LongX")]
    [InlineData("bunker", "LongX")]
    [InlineData("tower", "Tall")]
    [InlineData("lighthouse", "Tall")]
    [InlineData("boat", "Mirror")]
    [InlineData("ship", "Mirror")]
    [InlineData("road", "Flat")]
    public void RegistryContainsExpectedMapping(string prefix, string hint)
    {
        var src = ReadSource();
        var pattern = $"\\(\"{Regex.Escape(prefix)}\", ShapeHint\\.{hint}\\)";
        Regex.IsMatch(src, pattern).Should().BeTrue(
            $"FR-WSM-003: AssetShapeRegistry should map '{prefix}' → {hint}");
    }

    [Fact]
    public void RegistryHasAuto_DefaultFallback()
    {
        var src = ReadSource();
        src.Should().Contain("ShapeHint.Auto");
        src.Should().Contain("GetShapeHint");
        src.Should().Contain("ResolveStyle");
    }

    [Fact]
    public void RegistryExposesPublicEnum()
    {
        var src = ReadSource();
        src.Should().Contain("public enum ShapeHint");
        // FR-WSM-003 acceptance: enum members
        foreach (var member in new[] { "Cylinder", "LongX", "LongZ", "Tall", "Flat", "Mirror", "Auto" })
        {
            src.Should().Contain(member, because: $"ShapeHint must include {member}");
        }
    }

    [Fact]
    public void ResolveStyle_HonorsManualOverride()
    {
        // FR-WSM-003: non-'auto' SavedSettings.VoxelInflationStyle should win
        var src = ReadSource();
        src.Should().Contain("globalOverride");
        src.Should().Contain("auto");
    }
}
