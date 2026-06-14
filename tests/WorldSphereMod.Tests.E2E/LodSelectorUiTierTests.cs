using System.IO;
using FluentAssertions;
using Xunit;

namespace WorldSphereMod.Tests.E2E;

public sealed class LodSelectorUiTierTests
{
    static string ReadSourceFile(string relativePath)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var fullPath = Path.Combine(dir!.FullName, relativePath);
        File.Exists(fullPath).Should().BeTrue();
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void LodSelector_maps_lod_tier_to_ui_tier_for_worldspace_culling()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/LOD/LodSelector.cs");
        source.Should().Contain("public enum UiTier { None, HealthOnly, Full }");
        source.Should().Contain("ClassifyUiTier(LodTier tier)");
        source.Should().Contain("case LodTier.Voxel: return UiTier.Full");
        source.Should().Contain("case LodTier.Cull: return UiTier.HealthOnly");
        source.Should().Contain("GetUiTier(int instanceId)");
    }

    [Fact]
    public void WorldUIRenderer_applies_ui_tier_to_nameplate_health_and_badge()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Worldspace/WorldUIRenderer.cs");
        source.Should().Contain("LodSelector.GetUiTier(a.GetHashCode())");
        source.Should().Contain("ApplyUiTier(a, rig, uiTier)");
        source.Should().Contain("FactionBadge.Attach(a, rig)");
        source.Should().Contain("VoxelRender.NotifyActorDamaged");
    }

    [Fact]
    public void VoxelRender_exposes_OnActorDamaged_hook_for_worldspace_ui()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        source.Should().Contain("public static event Action<Actor, int> OnActorDamaged");
        source.Should().Contain("public static void NotifyActorDamaged(Actor a, int damage)");
    }
}
