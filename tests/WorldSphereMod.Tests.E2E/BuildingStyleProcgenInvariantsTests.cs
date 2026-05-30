using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// E2E source invariants for the BuildingStyleProcgen opt-in branch inside
/// BuildingProcRender: default off while ProceduralBuildings is on, secondary
/// toggle selects ProcGenCache vs VoxelMeshCache (procgen-path-precedence-analysis.md).
/// </summary>
public sealed class BuildingStyleProcgenInvariantsTests
{
    const string BuildingProcRenderPath = "WorldSphereMod/Code/ProcGen/BuildingProcRender.cs";
    const string SavedSettingsPath = "WorldSphereMod/Code/SavedSettings.cs";
    const string WorldSphereTabPath = "WorldSphereMod/Code/WorldSphereTab.cs";

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

    static string ExtractMethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"method signature not found: {signature}");
        var brace = source.IndexOf('{', start);
        brace.Should().BeGreaterThanOrEqualTo(0);

        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(brace + 1, i - brace - 1);
                }
            }
        }

        throw new InvalidOperationException($"unbalanced braces in method: {signature}");
    }

    [Fact]
    public void SavedSettings_ProceduralBuildings_defaults_off_while_BuildingStyleProcgen_opt_in_off()
    {
        var settings = ReadSource(SavedSettingsPath);

        Regex.IsMatch(settings, @"public\s+bool\s+ProceduralBuildings\s*=\s*false")
            .Should().BeTrue("Phase 2 proc buildings must default OFF");
        Regex.IsMatch(settings, @"public\s+bool\s+BuildingStyleProcgen\s*=\s*false")
            .Should().BeTrue("stylized procgen architecture path stays opt-in OFF by default");
    }

    [Fact]
    public void BuildingProcRender_patch_gates_on_ProceduralBuildings_not_style_toggle()
    {
        var buildingProc = ReadSource(BuildingProcRenderPath);

        buildingProc.Should().Contain("[Phase(nameof(SavedSettings.ProceduralBuildings))]",
            "Harmony postfix must be gated by the Phase 2 flag");
        buildingProc.Should().NotContain("[Phase(nameof(SavedSettings.BuildingStyleProcgen))]",
            "style procgen is a runtime branch, not a patch gate");

        var emitBody = ExtractMethodBody(buildingProc, "public static void EmitMeshes(BuildingManager __instance)");
        emitBody.Should().Contain("!Core.savedSettings.ProceduralBuildings) return",
            "EmitMeshes must exit when Phase 2 proc buildings are disabled");
        emitBody.Should().NotContain("!Core.savedSettings.BuildingStyleProcgen) return",
            "style toggle must not short-circuit the entire emit loop");
    }

    [Fact]
    public void BuildingProcRender_style_procgen_branch_selects_architecture_vs_voxel_mesh_cache()
    {
        var buildingProc = ReadSource(BuildingProcRenderPath);
        var emitBody = ExtractMethodBody(buildingProc, "public static void EmitMeshes(BuildingManager __instance)");

        emitBody.Should().Contain("if (Core.savedSettings.BuildingStyleProcgen)",
            "proc building emit must branch on the style opt-in flag");
        emitBody.Should().Contain("ProcGenCache.GetOrGenerate(b.asset, rules)",
            "style ON must use legacy stylized procgen architecture meshes");
        emitBody.Should().Contain("VoxelMeshCache.Get(sp)",
            "style OFF must voxelize building sprites via VoxelMeshCache");
        emitBody.Should().Contain("VoxelRender.Submit(m, trs, Color.white)",
            "both branches must submit through the shared voxel render path");
    }

    [Fact]
    public void BuildingStyleProcgen_branch_applies_only_outside_foliage_shape_paths()
    {
        var buildingProc = ReadSource(BuildingProcRenderPath);
        var emitBody = ExtractMethodBody(buildingProc, "public static void EmitMeshes(BuildingManager __instance)");

        var foliageGuardIndex = emitBody.IndexOf(
            "rules.Shape == BuildingShape.CrossedQuad || rules.Shape == BuildingShape.Single",
            StringComparison.Ordinal);
        var styleBranchIndex = emitBody.IndexOf(
            "if (Core.savedSettings.BuildingStyleProcgen)",
            StringComparison.Ordinal);

        foliageGuardIndex.Should().BeGreaterThanOrEqualTo(0);
        styleBranchIndex.Should().BeGreaterThan(foliageGuardIndex,
            "style procgen must be nested in the non-foliage else branch after CrossedQuad/Single");
    }

    [Fact]
    public void WorldSphereTab_exposes_style_procgen_toggle_in_sprite_settings_with_persist()
    {
        var tab = ReadSource(WorldSphereTabPath);

        tab.Should().Contain("CreateWindowButton(\"Sprite Settings\"",
            "style procgen belongs in Sprite Settings, not the Phases window");
        tab.Should().Contain("Core.savedSettings.BuildingStyleProcgen, ToggleBuildingStyleProcgen",
            "toggle must bind to SavedSettings.BuildingStyleProcgen");

        var toggleBody = ExtractMethodBody(tab, "static void ToggleBuildingStyleProcgen(string _)");
        toggleBody.Should().Contain("BuildingStyleProcgen = !Core.savedSettings.BuildingStyleProcgen");
        toggleBody.Should().Contain("Core.SaveSettings()",
            "style procgen changes must persist immediately");

        var phasesWindowMarker = "CreateWindowButton(PhasesWindowId";
        var phasesStart = tab.IndexOf(phasesWindowMarker, StringComparison.Ordinal);
        phasesStart.Should().BeGreaterThanOrEqualTo(0, "Phases window must exist");
        var phasesEnd = tab.IndexOf("CreateButton(\"Open Sprites\"", phasesStart, StringComparison.Ordinal);
        phasesEnd.Should().BeGreaterThan(phasesStart);
        var phasesWindow = tab.Substring(phasesStart, phasesEnd - phasesStart);

        phasesWindow.Should().Contain("new ButtonData(\"procedural_buildings\"",
            "Phase 2 proc buildings toggle remains in the Phases window");
        phasesWindow.Should().NotContain("building_style_procgen",
            "style procgen is a secondary opt-in and must not appear in Phases");
    }
}
