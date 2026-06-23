using System;
using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// Closes Phase 6 E2E gaps: humanoid/quadruped rig variants, Crabzilla/Dragon
/// special cases, and static voxel fallback paths (e2e-coverage-gaps.md #3).
/// </summary>
[Trait("Category", "E2E")]
public class SkeletalRigVariantInvariantsTests
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

    static string ReadSourceFile(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        File.Exists(fullPath).Should().BeTrue($"source file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    static string ExtractMethodBody(string source, string signature)
    {
        int headerIndex = source.IndexOf(signature, StringComparison.Ordinal);
        headerIndex.Should().BeGreaterThanOrEqualTo(0, $"method signature should exist: {signature}");

        int openBrace = source.IndexOf('{', headerIndex);
        openBrace.Should().BeGreaterThanOrEqualTo(0, "method must open with a '{'");

        int depth = 0;
        for (int i = openBrace; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '{')
            {
                depth++;
                continue;
            }

            if (c != '}')
            {
                continue;
            }

            depth--;
            if (depth == 0)
            {
                return source.Substring(openBrace + 1, i - openBrace - 1);
            }
        }

        throw new InvalidOperationException("Unbalanced braces while extracting method body");
    }

    [Fact]
    public void Constants_ActorRigTypes_maps_humanoid_quadruped_and_snake_variants()
    {
        var constants = ReadSourceFile("WorldSphereMod/Code/Constants.cs");

        constants.Should().Contain("[\"human\"] = RigType.Humanoid");
        constants.Should().Contain("[\"wolf\"] = RigType.Quadruped");
        constants.Should().Contain("[\"bear\"] = RigType.Quadruped");
        constants.Should().Contain("[\"snake\"] = RigType.Snake");
        constants.Should().Contain("public static RigType ResolveActorRig(string assetId)");
        constants.Should().Contain("return RigType.Humanoid",
            "unknown actors must default to humanoid rig for static fallback");
    }

    [Fact]
    public void Constants_dragon_and_crabzilla_resolve_to_RigType_None_for_voxel_path()
    {
        var constants = ReadSourceFile("WorldSphereMod/Code/Constants.cs");

        constants.Should().Contain("[\"dragon\"] = RigType.None");
        constants.Should().Contain("[\"crabzilla\"] = RigType.None",
            "mega actors skip skeletal driver and stay on voxel/avatar patches");
    }

    [Fact]
    public void Core_registers_FixCrabzilla_and_skeletal_phase_toggle()
    {
        var core = ReadSourceFile("WorldSphereMod/Code/Core.cs");

        core.Should().Contain("Patcher.PatchAll(typeof(FixCrabzilla))",
            "Crabzilla 3D avatar patch must be registered at mod init");
        core.Should().Contain("nameof(SavedSettings.SkeletalAnimation)",
            "SkeletalAnimation must route through ApplyPhaseToggle");
    }

    [Fact]
    public void RigDriver_non_humanoid_rigs_fall_back_to_static_VoxelRender_Submit()
    {
        var rigDriver = ReadSourceFile("WorldSphereMod/Code/Rig/RigDriver.cs");
        var submitBody = ExtractMethodBody(rigDriver,
            "public static bool SubmitSkinnedActor(");

        submitBody.Should().Contain("rigType != RigType.Humanoid",
            "only humanoid rigs use SkinnedMeshRenderer hierarchy (gated by kSkinnedRigProductionReady)");
        submitBody.Should().Contain("return VoxelRender.Submit(svm.BaseMesh, Matrix4x4.TRS(pos, rot, scl), tint)",
            "quadruped/snake/unknown rigs must fall back to static voxel mesh submit");
    }

    [Fact]
    public void VoxelRender_actor_emit_skips_skeletal_when_rig_is_None_and_uses_impostor_or_voxel()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        voxelRender.Should().Contain("ResolveRigType(a.asset.id)");
        voxelRender.Should().Contain("if (rigType != WorldSphereMod.Rig.RigType.None)",
            "RigType.None actors must bypass RigDriver and continue to impostor/voxel tiers");
        voxelRender.Should().Contain("Constants.ResolveActorRig(assetId)",
            "rig resolution must delegate to Constants registry");
    }

    [Fact]
    public void FixCrabzilla_and_dragonfix_gate_3D_avatar_patches_on_IsWorld3D()
    {
        var general = ReadSourceFile("WorldSphereMod/Code/General.cs");

        general.Should().Contain("[HarmonyPatch(typeof(Crabzilla), nameof(Crabzilla.create))]",
            "Crabzilla create must be patched for 3D billboard rig");
        general.Should().Contain("[HarmonyPatch(typeof(Dragon), nameof(Dragon.create))]",
            "Dragon create must hide vanilla sprite renderer in 3D mode");
        general.Should().Contain("if (!Core.IsWorld3D)",
            "mega-actor patches must no-op outside 3D worlds");
    }
}
