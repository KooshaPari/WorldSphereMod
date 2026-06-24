using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Source-level checks for the phase 6 rig registry surface.
/// The unit test project cannot reference the Unity-facing mod assembly directly,
/// so it verifies the committed source text instead.
/// </summary>
[Trait("Category", "Unit")]
public sealed class Phase6RigRegistryTests
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
    public void BoneDefinition_enumerates_the_phase_6_rig_types()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Rig/BoneDefinition.cs");

        foreach (var name in new[] { "Humanoid", "Quadruped", "Bird", "Snake", "Insect", "Static" })
        {
            Regex.IsMatch(source, $@"\b{name}\b").Should().BeTrue($"RigType must include {name}");
        }
    }

    [Fact]
    public void Constants_defines_the_actor_rig_registry_entries()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Constants.cs");

        source.Should().Contain("ActorRigTypes");
        foreach (var id in new[] { "human", "dwarf", "elf", "orc", "goblin", "wolf", "bear", "snake", "butterfly", "bee", "bird", "eagle", "sand_spider", "dragon", "crabzilla" })
        {
            source.Should().Contain($"[\"{id}\"]", $"ActorRigTypes should include {id}");
        }
    }

    [Fact]
    public void Constants_resolve_actor_rig_uses_registry_then_prefix_then_humanoid_default()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Constants.cs");

        source.Should().Contain("ActorRigTypes.TryGetValue");
        source.Should().Contain("RegisterActorRig");
        source.Should().Contain("return RigType.Humanoid");
        source.Should().Contain("VehicleShapeHints.IsVehicleAssetId");
        source.Should().Contain("MatchesAnyPrefix");
        source.Should().Contain("_humanoidPrefixes");
        source.Should().Contain("_quadrupedPrefixes");
        source.Should().Contain("_birdPrefixes");
        source.Should().Contain("_insectPrefixes");
    }

    [Fact]
    public void Constants_registry_omits_unconfirmed_catalog_asset_ids()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Constants.cs");

        source.Should().NotContain("[\"spider\"]");
    }

    [Fact]
    public void VoxelRender_resolves_rig_type_after_cull_and_lod()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Voxel/VoxelRender.cs");

        var cullIndex = source.IndexOf("FrustumCuller.IsVisible", StringComparison.Ordinal);
        var rigIndex = source.IndexOf("ResolveRigType(a.asset.id)", StringComparison.Ordinal);
        cullIndex.Should().BeGreaterThan(-1);
        rigIndex.Should().BeGreaterThan(cullIndex, "rig type should be resolved after frustum cull");
        source.Should().Contain("tier != WorldSphereMod.LOD.LodTier.Cull");
        source.Should().Contain("Constants.ResolveActorRig(assetId)");
    }

    [Fact]
    public void VoxelMeshCache_exposes_the_rig_weight_builder()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Voxel/VoxelMeshCache.cs");

        source.Should().Contain("BuildWithBoneWeights");
        source.Should().Contain("HumanoidRig.SegmentVoxels");
        source.Should().Contain("SkinnedVoxelMesh");
    }

    [Fact]
    public void RigCache_uses_the_shared_bone_weight_builder_for_humanoids()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Rig/RigCache.cs");

        source.Should().Contain("VoxelMeshCache.BuildWithBoneWeights(sprite, rigType)");
    }

    [Fact]
    public void RigDriver_uses_skinned_mesh_renderers_for_humanoids()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Rig/RigDriver.cs");

        source.Should().Contain("SkinnedMeshRenderer");
        source.Should().Contain("HumanoidRig.FillLocalRotations");
        source.Should().Contain("public static void Update()");
    }
}
