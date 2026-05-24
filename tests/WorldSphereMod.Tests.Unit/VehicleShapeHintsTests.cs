using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Vehicle rigging is deferred (see vehicle-rigging-spec.md); until VehicleRig lands,
/// vehicles use Mirror → balloon voxel inflation via AssetShapeRegistry.
/// </summary>
public sealed class VehicleShapeHintsTests
{
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

    static string ReadSource(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(path).Should().BeTrue($"source file must exist at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void VehicleShapeHints_documents_VehicleRig_deferral()
    {
        var src = ReadSource(@"WorldSphereMod/Code/Rig/VehicleShapeHints.cs");

        src.Should().Contain("vehicle-rigging-spec.md");
        src.Should().Contain("VehicleRig");
        src.Should().Contain("Deferred");
        src.Should().Contain("RegisterVehicleRig");
    }

    [Fact]
    public void VehicleShapeHints_delegates_to_AssetShapeRegistry()
    {
        var src = ReadSource(@"WorldSphereMod/Code/Rig/VehicleShapeHints.cs");

        src.Should().Contain("AssetShapeRegistry.GetShapeHint");
        src.Should().Contain("AssetShapeRegistry.ResolveStyle");
    }

    [Theory]
    [InlineData("car")]
    [InlineData("tank")]
    [InlineData("wagon")]
    [InlineData("cart")]
    [InlineData("vehicle")]
    public void VehicleShapeHints_lists_mirror_prefix(string prefix)
    {
        var src = ReadSource(@"WorldSphereMod/Code/Rig/VehicleShapeHints.cs");
        src.Should().Contain($"\"{prefix}\"");
    }

    [Theory]
    [InlineData("boat", "Mirror")]
    [InlineData("ship", "Mirror")]
    [InlineData("wagon", "Mirror")]
    [InlineData("cart", "Mirror")]
    [InlineData("car", "Mirror")]
    [InlineData("tank", "Mirror")]
    [InlineData("vehicle", "Mirror")]
    public void AssetShapeRegistry_maps_vehicle_prefixes_to_mirror(string prefix, string hint)
    {
        var src = ReadSource(@"WorldSphereMod/Code/Voxel/AssetShapeRegistry.cs");
        var pattern = $"\\(\"{Regex.Escape(prefix)}\", ShapeHint\\.{hint}\\)";
        Regex.IsMatch(src, pattern).Should().BeTrue(
            $"vehicles should map '{prefix}' → {hint} until VehicleRig replaces mirror voxelization");
    }

    [Fact]
    public void AssetShapeRegistry_resolveStyle_maps_mirror_to_balloon()
    {
        var src = ReadSource(@"WorldSphereMod/Code/Voxel/AssetShapeRegistry.cs");
        src.Should().Contain("ShapeHint.Mirror => \"balloon\"");
    }

    [Fact]
    public void VehicleShapeHints_IsVehicleAssetId_matches_substring_patterns()
    {
        var src = ReadSource(@"WorldSphereMod/Code/Rig/VehicleShapeHints.cs");

        src.Should().Contain("StartsWith(prefix)");
        src.Should().Contain("Contains(\"_\" + prefix)");
        src.Should().Contain("Contains(prefix + \"_\")");
    }
}
