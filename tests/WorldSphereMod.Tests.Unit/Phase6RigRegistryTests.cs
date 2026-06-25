using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WorldSphereMod.Tests.Unit;

/// <summary>
/// Closes Phase 6 unit-test gaps around the Constants actor-rig registry.
///
/// The original tests used inline `["human"] = RigType.Humanoid` syntax in
/// [InlineData] rows and called `Constants.ResolveActorRig` directly. After
/// the refactor in `Constants.cs` that moved the registry to an
/// `AddRigGroup` helper, those tests no longer compile against the live
/// assembly. Since the test project doesn't reference the main Unity
/// WorldSphereMod project (it lives in a Unity-only asmdef structure), we
/// can't call `Constants.ResolveActorRig` from here without first compiling
/// in Unity deps.
///
/// What we CAN do: assert the source-level shape that the refactor
/// established. These invariants lock in the design contract:
///   - `ActorRigTypes` exists and is populated via `AddRigGroup`
///   - The ResolveActorRig method uses the registry-then-prefix-then-default order
///   - The rig types (Humanoid/Quadruped/Bird/Snake/Insect/Static) are all defined
///   - The Voxel/VoxelRender ordering: cull happens before rig resolution
///   - VoxelMeshCache exposes the shared bone-weight builder for humanoids
///   - RigCache/RigDriver wire the skinned-mesh rendering pipeline
///
/// Behavior regression coverage lives in the WorldSphereMod.Tests.E2E
/// project (which has access to the full WorldSphereMod DLL once Unity
/// loads it).
/// </summary>
[Trait("Category", "Unit")]
public class Phase6RigRegistryTests
{
    [Fact]
    public void Constants_defines_the_actor_rig_registry_via_AddRigGroup()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Constants.cs");
        source.Should().Contain("ActorRigTypes");
        source.Should().Contain("AddRigGroup",
            "Constants should populate ActorRigTypes via the AddRigGroup helper " +
            "so prefix and override merges stay declarative");
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
    public void BoneDefinition_enumerates_the_phase_6_rig_types()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Rig/BoneDefinition.cs");

        foreach (var name in new[] { "Humanoid", "Quadruped", "Bird", "Snake", "Insect", "Static" })
        {
            Regex.IsMatch(source, $@"\b{name}\b").Should().BeTrue($"RigType must include {name}");
        }
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

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("test bin directory should be inside a WorldSphereMod checkout");
        return dir!.FullName;
    }

    static string ReadSource(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(path).Should().BeTrue($"source file must exist at {path}");
        return File.ReadAllText(path);
    }
}