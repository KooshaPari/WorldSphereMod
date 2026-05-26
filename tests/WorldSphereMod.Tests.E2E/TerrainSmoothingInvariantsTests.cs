using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Source invariants for terrain polish: biome color blending (TileMapToSphere + Core.Sphere)
/// and mountain slope smoothing overlay (Terrain/TerrainSmoothing.cs).
/// </summary>
public sealed class TerrainSmoothingInvariantsTests
{
    const string SavedSettingsRelative = "WorldSphereMod/Code/SavedSettings.cs";
    const string TileMapToSphereRelative = "WorldSphereMod/Code/TileMapToSphere.cs";
    const string TerrainSmoothingRelative = "WorldSphereMod/Code/Terrain/TerrainSmoothing.cs";
    const string CoreRelative = "WorldSphereMod/Code/Core.cs";
    const string WorldUnloadPatchRelative = "WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs";
    const string WorldSphereTabRelative = "WorldSphereMod/Code/WorldSphereTab.cs";

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

    static string ExtractOnFinishMethodBody(string patchSource)
    {
        const string signature = "public static void OnFinish()";
        int headerIndex = patchSource.IndexOf(signature, StringComparison.Ordinal);
        headerIndex.Should().BeGreaterThanOrEqualTo(0, "WorldUnloadPatch.OnFinish must exist");

        int openBrace = patchSource.IndexOf('{', headerIndex);
        openBrace.Should().BeGreaterThanOrEqualTo(0, "OnFinish must open with a '{'");

        int depth = 0;
        for (int i = openBrace; i < patchSource.Length; i++)
        {
            char c = patchSource[i];
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
                return patchSource.Substring(openBrace + 1, i - openBrace - 1);
            }
        }

        throw new InvalidOperationException("Unbalanced braces while extracting OnFinish body");
    }

    [Fact]
    public void SavedSettings_terrain_polish_flags_default_false()
    {
        var settings = ReadSource(SavedSettingsRelative);

        Regex.IsMatch(settings, @"public\s+bool\s+BiomeBlending\s*=\s*true")
            .Should().BeTrue("biome color blending must default ON for smooth terrain transitions");
        Regex.IsMatch(settings, @"public\s+bool\s+MountainSlopeSmoothing\s*=\s*false")
            .Should().BeTrue("mountain slope smoothing must default OFF for new installs");

        settings.Should().Contain("Terrain polish: blend biome colors",
            "BiomeBlending must remain documented as terrain polish");
        settings.Should().Contain("Mountain slope smoothing:",
            "MountainSlopeSmoothing must remain documented as cliff/ridge overlay");
    }

    [Fact]
    public void TileMapToSphere_marks_biome_blend_dirty_on_tile_redraw_when_enabled()
    {
        var source = ReadSource(TileMapToSphereRelative);
        var queuePostfix = ExtractMethodBody(source, "static void Postfix()");

        queuePostfix.Should().Contain("Core.IsWorld3D && Core.savedSettings.BiomeBlending",
            "tile redraw queue must gate biome blend invalidation on the setting");
        queuePostfix.Should().Contain("TileMapToSphere.MarkBiomeBlendDirty()",
            "tile redraw must mark biome colors stale for deferred refresh");
    }

    [Fact]
    public void TileMapToSphere_Redraw3DTiles_refreshes_colors_when_biome_blend_dirty()
    {
        var source = ReadSource(TileMapToSphereRelative);
        var redrawBody = ExtractMethodBody(source, "public static void Redraw3DTiles()");

        redrawBody.Should().Contain("_biomeBlendDirty && Core.savedSettings.BiomeBlending",
            "color refresh must respect both dirty flag and runtime toggle");
        redrawBody.Should().Contain("_biomeBlendDirty = false",
            "dirty flag must clear after a successful refresh");
        redrawBody.Should().Contain("Core.Sphere.RefreshColors()",
            "deferred biome blend must push through Sphere color refresh");
    }

    [Fact]
    public void Core_Sphere_GetColor_gates_biome_blending_before_neighbor_blend()
    {
        var core = ReadSource(CoreRelative);
        var getColorBody = ExtractMethodBody(core, "public static Color32 GetColor(int index)");

        getColorBody.Should().Contain("GetBaseColor(index)",
            "biome path must start from the unblended tile color");
        getColorBody.Should().Contain("if (!Core.savedSettings.BiomeBlending)",
            "runtime toggle must bypass neighbor blending when disabled");
        getColorBody.Should().Contain("return BlendBiomeColor(index, baseColor)",
            "enabled path must route through weighted neighbor sampling");
    }

    [Fact]
    public void Core_Sphere_BlendBiomeColor_samples_weighted_neighbors()
    {
        var core = ReadSource(CoreRelative);
        var blendBody = ExtractMethodBody(core, "static Color32 BlendBiomeColor(int index, Color32 fallback)");

        blendBody.Should().Contain("const int radius = 3",
            "biome blend must sample a local neighborhood");
        blendBody.Should().Contain("GetBaseColor(sample.data.tile_id)",
            "neighbor samples must use base colors, not recursively blended colors");
        blendBody.Should().Contain("totalWeight",
            "blend must normalize by accumulated sample weights");
        blendBody.Should().Contain("Core.Sphere.IsWrapped",
            "wrapped worlds must wrap horizontal neighbor coordinates");
    }

    [Fact]
    public void MountainSlopeSurface_is_phase_gated_and_rebuilds_on_tile_redraw()
    {
        var source = ReadSource(TerrainSmoothingRelative);

        source.Should().Contain("[Phase(nameof(SavedSettings.MountainSlopeSmoothing))]",
            "mountain slope patches must honor the MountainSlopeSmoothing phase gate");
        source.Should().Contain("[HarmonyPatch(typeof(WorldTilemap), nameof(WorldTilemap.redrawTiles))]",
            "slope overlay must rebuild when upstream tilemap redraw runs");

        var ensureActive = ExtractMethodBody(source, "public static void EnsureActive()");
        ensureActive.Should().Contain("!Core.IsWorld3D || !Core.savedSettings.MountainSlopeSmoothing",
            "overlay must tear down outside 3D or when the toggle is off");
        ensureActive.Should().Contain("Create(capsule.parent)",
            "first activation must parent the overlay under the sphere rig");

        var redrawPostfix = ExtractMethodBody(source, "public static void OnRedraw()");
        redrawPostfix.Should().Contain("MountainSlopeSurface.EnsureActive()");
        redrawPostfix.Should().Contain("MountainSlopeSurface.RequestRebuild()");
    }

    [Fact]
    public void MountainSlopeSurface_detects_cliff_quads_above_height_threshold()
    {
        var source = ReadSource(TerrainSmoothingRelative);
        var detectBody = ExtractMethodBody(source, "List<CliffQuad> DetectCliffQuads(int width, int height)");

        detectBody.Should().Contain("tile.TileHeight()",
            "cliff detection must compare upstream tile heights");
        detectBody.Should().Contain("Mathf.Abs(tileHeight - rightHeight) > 0.1f",
            "horizontal cliff edges must exceed minimum height threshold for smooth coverage");
        detectBody.Should().Contain("Mathf.Abs(tileHeight - upHeight) > 0.1f",
            "vertical cliff edges must exceed minimum height threshold for smooth coverage");
        detectBody.Should().Contain("Core.Sphere.GetColor(tile.data.tile_id)",
            "cliff quads must carry biome-blended tile colors");
    }

    [Fact]
    public void MountainSlopeSurface_RebuildMesh_projects_cliff_quads_onto_sphere()
    {
        var source = ReadSource(TerrainSmoothingRelative);
        var rebuildBody = ExtractMethodBody(source, "void RebuildMesh()");

        rebuildBody.Should().Contain("DetectCliffQuads(width, height)",
            "mesh rebuild must derive geometry from cliff detection");
        rebuildBody.Should().Contain("Core.Sphere.SpherePos(",
            "rebuilt vertices must be projected onto the sphere surface");
    }

    [Fact]
    public void MountainSlopeSurface_EnsureMaterial_resolves_OpaqueVertexColor_with_white_tint()
    {
        var source = ReadSource(TerrainSmoothingRelative);
        var ensureBody = ExtractMethodBody(source, "static bool EnsureMaterial()");

        ensureBody.Should().Contain("LoadedShaders.TryGetValue(\"OpaqueVertexColor\"",
            "slope material must resolve from the bundle-loaded shader cache first");
        ensureBody.Should().Contain("Shader.Find(\"WSM3D/OpaqueVertexColor\")",
            "slope material must fall back to Shader.Find when cache misses");
        ensureBody.Should().Contain("Color.white",
            "tint must be white so vertex colors are the sole albedo source");
        ensureBody.Should().Contain("material.enableInstancing",
            "slope material must validate instancing before adoption");
        ensureBody.Should().Contain("[WSM3D] No mountain slope smoothing shader found; overlay disabled.",
            "missing shader path must disable overlay instead of rendering magenta fallback");
    }

    [Fact]
    public void Core_ApplyPhaseToggle_wires_MountainSlopeSmoothing_lifecycle()
    {
        var core = ReadSource(CoreRelative);
        var applyBody = ExtractMethodBody(core, "public static void ApplyPhaseToggle(string flagName, bool newValue)");

        var mountainBranch = ExtractMethodBody(applyBody,
            $"if (flagName == nameof(SavedSettings.MountainSlopeSmoothing))");
        mountainBranch.Should().Contain("MountainSlopeSurface.EnsureActive()",
            "enabling the toggle must create the overlay without world reload");
        mountainBranch.Should().Contain("MountainSlopeSurface.Destroy()",
            "disabling the toggle must tear down the overlay immediately");
    }

    [Fact]
    public void WorldUnloadPatch_OnFinish_clears_mountain_slope_overlay()
    {
        var patch = ReadSource(WorldUnloadPatchRelative);
        var onFinish = ExtractOnFinishMethodBody(patch);

        onFinish.Should().Contain("WorldSphereMod.Terrain.MountainSlopeSurface.Destroy()",
            "world unload must drop transient mountain slope mesh state");
    }

    [Fact]
    public void WorldSphereTab_runtime_toggles_refresh_biome_and_mountain_slope_state()
    {
        var tab = ReadSource(WorldSphereTabRelative);

        tab.Should().Contain("Core.savedSettings.BiomeBlending",
            "settings tab must expose biome blending toggle");
        tab.Should().Contain("Core.savedSettings.MountainSlopeSmoothing",
            "settings tab must expose mountain slope smoothing toggle");

        tab.Should().Contain(
            "if (settingField.Name == nameof(SavedSettings.BiomeBlending) && Core.IsWorld3D)",
            "immediate biome toggle must refresh colors in 3D");
        tab.Should().Contain(
            "if (previousBiomeBlending != Core.savedSettings.BiomeBlending && Core.IsWorld3D) Core.Sphere.RefreshColors()",
            "batch apply must refresh colors when biome blending changes");
        tab.Should().Contain(
            "if (previousMountainSlopeSmoothing != Core.savedSettings.MountainSlopeSmoothing)",
            "batch apply must reconcile mountain slope overlay when the toggle changes");
        tab.Should().Contain(
            "Core.ApplyPhaseToggle(nameof(SavedSettings.MountainSlopeSmoothing)",
            "mountain slope toggle must route through ApplyPhaseToggle lifecycle");
    }
}
