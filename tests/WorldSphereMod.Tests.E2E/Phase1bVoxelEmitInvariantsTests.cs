using System;
using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// Phase 1b drop/projectile voxel emit invariants: Harmony postfix wiring on Drop.updatePosition
/// and QuantumSpriteLibrary.drawProjectiles, shared VoxelMeshCache + Submit path, and sprite
/// suppression when voxel submission succeeds (phase1b-drops-projectiles-spec.md).
/// </summary>
public class Phase1bVoxelEmitInvariantsTests
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

    static string ExtractClassSection(string source, string className)
    {
        int classIndex = source.IndexOf($"class {className}", StringComparison.Ordinal);
        classIndex.Should().BeGreaterThanOrEqualTo(0, $"{className} must exist in VoxelRender.cs");

        int sectionStart = Math.Max(0, classIndex - 512);
        var prior = source.Substring(sectionStart, classIndex - sectionStart);
        int commentIndex = prior.LastIndexOf("// Phase 1b:", StringComparison.Ordinal);
        if (commentIndex >= 0)
        {
            sectionStart = sectionStart + commentIndex;
        }
        else
        {
            int attrIndex = prior.LastIndexOf("[Phase(", StringComparison.Ordinal);
            if (attrIndex >= 0)
            {
                sectionStart = sectionStart + attrIndex;
            }
        }

        int openBrace = source.IndexOf('{', classIndex);
        openBrace.Should().BeGreaterThanOrEqualTo(0, $"{className} must open with a '{{'");

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
                return source.Substring(sectionStart, i - sectionStart + 1);
            }
        }

        throw new InvalidOperationException($"Unbalanced braces while extracting {className}");
    }

    [Fact]
    public void VoxelRender_exposes_phase1b_DropVoxelEmit_and_ProjectileVoxelEmit_patch_classes()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        voxelRender.Should().Contain("public static class DropVoxelEmit",
            "drops must use a dedicated phase-gated Harmony patch class");
        voxelRender.Should().Contain("public static class ProjectileVoxelEmit",
            "projectiles must use a dedicated phase-gated Harmony patch class");
        voxelRender.Should().Contain("// Phase 1b: dropped items",
            "drop emit path must be documented in source");
        voxelRender.Should().Contain("// Phase 1b: projectiles. Postfix on drawProjectiles",
            "projectile emit must document drawProjectiles postfix wiring in source");
    }

    [Fact]
    public void DropVoxelEmit_harmony_postfix_wires_Drop_updatePosition_with_VoxelEntities_gate()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        var dropSection = ExtractClassSection(voxelRender, "DropVoxelEmit");

        dropSection.Should().Contain("[Phase(nameof(SavedSettings.VoxelEntities))]",
            "drop voxel emit must respect the VoxelEntities saved setting");
        dropSection.Should().Contain("[HarmonyPatch(typeof(Drop), nameof(Drop.updatePosition))]",
            "Phase 1b must postfix Drop.updatePosition after the 3D transform gate runs");
        dropSection.Should().Contain("[HarmonyPostfix]",
            "drop emit must use Harmony postfix, not a transpiler or prefix-only hook");
        dropSection.Should().Contain("public static void EmitVoxel(Drop __instance)",
            "drop postfix entry point must receive the live Drop instance");

        var emitBody = ExtractMethodBody(voxelRender, "public static void EmitVoxel(Drop __instance)");
        emitBody.Should().Contain("!Core.IsWorld3D");
        emitBody.Should().Contain("!Core.savedSettings.VoxelEntities");
        emitBody.Should().Contain("sr.enabled = true",
            "VoxelEntities off or cull miss must restore the vanilla sprite");
        emitBody.Should().Contain("EnsureMaterial()");
        emitBody.Should().Contain("VoxelMeshCache.Get(sp");
        emitBody.Should().Contain("Submit(mesh, trs, tint)");
        emitBody.Should().NotContain("Thread.",
            "drop voxel submission must stay on the main-thread update path");
    }

    [Fact]
    public void ProjectileVoxelEmit_harmony_postfix_wires_drawProjectiles_not_SetProjectile_helper()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        var projectileSection = ExtractClassSection(voxelRender, "ProjectileVoxelEmit");

        projectileSection.Should().Contain("[Phase(nameof(SavedSettings.VoxelEntities))]");
        projectileSection.Should().Contain(
            "[HarmonyPatch(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawProjectiles))]",
            "Phase 1b must postfix QuantumSpriteLibrary.drawProjectiles at the game draw boundary");
        projectileSection.Should().Contain("[HarmonyPostfix]");
        projectileSection.Should().Contain("public static void EmitVoxels(QuantumSpriteAsset pAsset)");

        var emitBody = ExtractMethodBody(voxelRender, "public static void EmitVoxels(QuantumSpriteAsset pAsset)");
        emitBody.Should().Contain("!Core.savedSettings.VoxelEntities");
        emitBody.Should().Contain("RestoreProjectileSprites(pAsset)",
            "VoxelEntities off must re-enable projectile billboard sprites");
        emitBody.Should().Contain("World.world.projectiles.list",
            "projectile voxel emit must iterate the same projectile list as vanilla drawProjectiles");
        emitBody.Should().Contain("ResolveProjectileSprite(projectile)");
        emitBody.Should().Contain("BuildProjectileWorldPosition(projectile)");
        emitBody.Should().Contain("SuppressProjectileSprite(pAsset, pos)",
            "successful voxel submit must hide the matching GroupSpriteObject billboard");
        emitBody.Should().Contain("VoxelMeshCache.Get(sprite");
        emitBody.Should().Contain("Submit(mesh, trs, tint)");
        emitBody.Should().NotContain("Thread.",
            "projectile voxel submission must stay on the main-thread draw path");
    }

    [Fact]
    public void Phase1b_emit_paths_share_actor_LOD_voxel_or_cull_pipeline()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        foreach (var emitSignature in new[]
                 {
                     "public static void EmitVoxel(Drop __instance)",
                     "public static void EmitVoxels(QuantumSpriteAsset pAsset)"
                 })
        {
            var body = ExtractMethodBody(voxelRender, emitSignature);
            body.Should().Contain("FrustumCuller.IsVisible",
                $"{emitSignature} must cull before mesh work");
            body.Should().Contain("LodSelector.Select",
                $"{emitSignature} must select LOD tier before mesh submission");
            // VOXEL-OR-INVISIBLE: far tier = Cull (draw nothing), never a billboard.
            body.Should().Contain("LodTier.Cull",
                $"{emitSignature} must cull (draw nothing) at far distance");
            body.Should().NotContain("ImpostorBillboard",
                $"{emitSignature} must not fall back to an impostor billboard");
            body.Should().Contain("halfHeight",
                $"{emitSignature} must lift mesh center like actor voxel emit");
            body.Should().Contain("Core.savedSettings.VoxelScaleMultiplier",
                $"{emitSignature} must honor the shared voxel scale multiplier");
        }
    }

    [Fact]
    public void Phase1b_spec_documents_postfix_wiring_and_no_render_data_mirror()
    {
        var spec = ReadSourceFile("docs/journeys/scratch/phase1b-drops-projectiles-spec.md");

        spec.Should().Contain("Drop.updatePosition",
            "spec must name the drop Harmony postfix target");
        spec.Should().Contain("QuantumSpriteLibrary.drawProjectiles",
            "spec must name the projectile Harmony postfix target");
        spec.Should().Contain("not on `Manager.SetProjectile`",
            "spec must document why drawProjectiles is the correct hook boundary");
        spec.Should().Contain("There is no actor-style `render_data` array",
            "spec must document that drops/projectiles read instance state directly");
        spec.Should().Contain("VoxelMeshCache.Get(sprite)",
            "spec must require the shared actor voxel mesh cache path");
    }
}
