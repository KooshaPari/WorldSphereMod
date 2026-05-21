using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using FluentAssertions;
using Newtonsoft.Json.Linq;

public class SourceContentInvariantsTests
{
    // Locate the repo root from test output directory.
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("repo root with WorldSphereMod.sln must be locatable from test cwd");
        return dir!.FullName;
    }

    private static string ReadSourceFile(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        File.Exists(fullPath).Should().BeTrue($"source file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void VoxelRender_cs_cull_skip_path_does_not_set_has_normal_render_false()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        // The recent fix ensures that after checking FrustumCuller.IsVisible,
        // we do NOT naively set rd.has_normal_render[i] = false. That would
        // disable rendering for culled entities, breaking the voxel pipeline.
        // We should skip the entire entity, not set has_normal_render false.

        // Find all occurrences of the culling check.
        var cullPattern = @"if\s*\(\s*!WorldSphereMod\.LOD\.FrustumCuller\.IsVisible\s*\([^)]+\)\s*\)";
        var matches = Regex.Matches(voxelRender, cullPattern);

        matches.Should().NotBeEmpty("VoxelRender must check FrustumCuller.IsVisible for frustum culling");

        // For each cull check, verify that the immediate next occurrence of has_normal_render
        // does NOT set it to false right after the IsVisible check.
        foreach (Match cullMatch in matches)
        {
            var cullEndPos = cullMatch.Index + cullMatch.Length;
            var nextSectionStart = cullEndPos;
            var nextSectionEnd = Math.Min(nextSectionStart + 200, voxelRender.Length);
            var nextSection = voxelRender.Substring(nextSectionStart, nextSectionEnd - nextSectionStart);

            // Within the next 200 chars after IsVisible, we should NOT see has_normal_render[i] = false
            // (that pattern would indicate the buggy code).
            var hasNormalRenderFalsePattern = @"has_normal_render\s*\[\s*\w+\s*\]\s*=\s*false";
            var hasBuggyCode = Regex.IsMatch(nextSection, hasNormalRenderFalsePattern);

            hasBuggyCode.Should().BeFalse(
                "VoxelRender must NOT set rd.has_normal_render[i] = false immediately after IsVisible check. " +
                "This would disable rendering for culled entities. Skip the entity instead.");
        }
    }

    [Fact]
    public void VoxelRender_cs_EnsureMaterial_excludes_default_sprites_and_hidden_colored()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        // EnsureMaterial must NOT include "Sprites/Default" or "Hidden/Internal-Colored"
        // because those are not real shader assets.
        voxelRender.Should().NotContain("\"Sprites/Default\"",
            "VoxelRender.EnsureMaterial must exclude Sprites/Default — it's a dummy fallback");

        voxelRender.Should().NotContain("\"Hidden/Internal-Colored\"",
            "VoxelRender.EnsureMaterial must exclude Hidden/Internal-Colored — it's internal-only");
    }

    [Fact]
    public void VoxelRender_cs_EnsureMaterial_reads_enableInstancing_after_setting()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        // Post-set validation: after assigning enableInstancing, the code must read it back
        // to verify it took effect (some materials may not support it).
        var ensureMethodPattern = @"(?:public\s+)?static\s+bool\s+EnsureMaterial\s*\([^)]*\)[\s\S]*?(?=\n\s{0,4}(?:public|static|private|\}))";
        var match = Regex.Match(voxelRender, ensureMethodPattern);

        match.Success.Should().BeTrue("VoxelRender.EnsureMaterial method must exist");

        var methodBody = match.Value;
        // Look for enableInstancing assignment followed by a read/check.
        var setThenReadPattern = @"enableInstancing\s*=\s*true[\s\S]*?enableInstancing";
        var hasSetAndRead = Regex.IsMatch(methodBody, setThenReadPattern);

        hasSetAndRead.Should().BeTrue(
            "VoxelRender.EnsureMaterial must verify enableInstancing was accepted after setting it");
    }

    [Fact]
    public void MeshInstanceBatcher_cs_Flush_wraps_DrawMeshInstanced_in_try_catch()
    {
        var batcher = ReadSourceFile("WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs");

        // The Flush method must wrap DrawMeshInstanced in try/catch to handle GPU exhaustion.
        batcher.Should().Contain("catch (System.InvalidOperationException",
            "MeshInstanceBatcher.Flush must catch InvalidOperationException from DrawMeshInstanced " +
            "in case the GPU runs out of instancing slots");
    }

    [Fact]
    public void WorldSphereTab_cs_CreateButtons_contains_3D_Phases_window()
    {
        var tab = ReadSourceFile("WorldSphereMod/Code/WorldSphereTab.cs");

        // The CreateButtons method must reference "3D Phases" — the new window containing all phase toggles.
        tab.Should().Contain("3D Phases",
            "WorldSphereTab.CreateButtons must define the '3D Phases' window button");
    }

    [Fact]
    public void WorldSphereTab_cs_contains_all_eleven_TogglePhase_handlers()
    {
        var tab = ReadSourceFile("WorldSphereMod/Code/WorldSphereTab.cs");

        var expectedHandlers = new[]
        {
            "TogglePhase_VoxelEntities",
            "TogglePhase_ProceduralBuildings",
            "TogglePhase_CrossedQuadFoliage",
            "ToggleBiomeBlending",
            "TogglePhase_MeshWater",
            "TogglePhase_HighShadows",
            "TogglePhase_SkeletalAnimation",
            "TogglePhase_WorldspaceUI",
            "TogglePhase_DayNightCycle",
            "TogglePhase_PostFX",
            "TogglePhase_ParticleEffects"
        };

        foreach (var handler in expectedHandlers)
        {
            tab.Should().Contain(handler,
                $"WorldSphereTab must define {handler} to wire the phase toggle");
        }
    }

    [Fact]
    public void EnJson_parses_as_valid_JSON()
    {
        var enJson = ReadSourceFile("WorldSphereMod/Locales/en.json");

        Action parseAction = () =>
        {
            var obj = JObject.Parse(enJson);
            obj.Should().NotBeNull();
        };

        parseAction.Should().NotThrow("en.json must be valid JSON");
    }

    [Fact]
    public void EnJson_contains_all_eleven_phase_toggle_keys()
    {
        var enJsonText = ReadSourceFile("WorldSphereMod/Locales/en.json");
        var enJson = JObject.Parse(enJsonText);

        var phaseKeys = new[]
        {
            "voxel_entities",
            "procedural_buildings",
            "crossed_quad_foliage",
            "biome_blending",
            "mesh_water",
            "high_shadows",
            "skeletal_animation",
            "worldspace_ui",
            "day_night_cycle",
            "post_fx",
            "particle_effects"
        };

        foreach (var key in phaseKeys)
        {
            enJson.ContainsKey(key).Should().BeTrue(
                $"en.json must contain locale key '{key}'");
        }
    }

    [Fact]
    public void EnJson_contains_descriptions_for_all_eleven_phases()
    {
        var enJsonText = ReadSourceFile("WorldSphereMod/Locales/en.json");
        var enJson = JObject.Parse(enJsonText);

        var phaseKeys = new[]
        {
            "voxel_entities",
            "procedural_buildings",
            "crossed_quad_foliage",
            "biome_blending",
            "mesh_water",
            "high_shadows",
            "skeletal_animation",
            "worldspace_ui",
            "day_night_cycle",
            "post_fx",
            "particle_effects"
        };

        foreach (var key in phaseKeys)
        {
            var descKey = $"{key}_description";
            enJson.ContainsKey(descKey).Should().BeTrue(
                $"en.json must contain locale description key '{descKey}'");
        }
    }

    [Fact]
    public void EnJson_phase_keys_and_descriptions_are_non_empty()
    {
        var enJsonText = ReadSourceFile("WorldSphereMod/Locales/en.json");
        var enJson = JObject.Parse(enJsonText);

        var phaseKeys = new[]
        {
            "voxel_entities",
            "procedural_buildings",
            "crossed_quad_foliage",
            "biome_blending",
            "mesh_water",
            "high_shadows",
            "skeletal_animation",
            "worldspace_ui",
            "day_night_cycle",
            "post_fx",
            "particle_effects"
        };

        foreach (var key in phaseKeys)
        {
            var mainText = enJson[key]?.ToString();
            mainText.Should().NotBeNullOrWhiteSpace(
                $"en.json key '{key}' must have non-empty string value");

            var descKey = $"{key}_description";
            var descText = enJson[descKey]?.ToString();
            descText.Should().NotBeNullOrWhiteSpace(
                $"en.json key '{descKey}' must have non-empty string value");
        }
    }
}
