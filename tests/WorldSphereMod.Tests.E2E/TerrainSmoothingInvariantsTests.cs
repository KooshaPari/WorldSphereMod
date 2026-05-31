using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Source invariants for terrain polish: biome color blending (TileMapToSphere + Core.Sphere).
///
/// NOTE (post crossed-quad removal + fork pivot, 2026-05-30): the former main-mod
/// MountainSlopeSurface overlay (Terrain/TerrainSmoothing.cs) is DELETED. Mountain
/// slope smoothing is now intrinsic to the Compound-Spheres fork's height-field mesh
/// (corner-averaged heights + analytic normals + Perlin micro-displacement) — there is
/// no main-mod overlay class to render or tear down. The MountainSlopeSmoothing saved
/// setting is retained as a fork-facing toggle, so default + tab + ApplyPhaseToggle
/// wiring are still asserted; the deleted render-class invariants are removed.
/// </summary>
public sealed class TerrainSmoothingInvariantsTests
{
    const string SavedSettingsRelative = "WorldSphereMod/Code/SavedSettings.cs";
    const string TileMapToSphereRelative = "WorldSphereMod/Code/TileMapToSphere.cs";
    const string CoreRelative = "WorldSphereMod/Code/Core.cs";
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

        blendBody.Should().Contain("const int radius = 3;",
            "biome blending must use a fixed three-tile sampling radius");
        blendBody.Should().Contain("for (int dy = -radius; dy <= radius; dy++)",
            "biome blend must scan rows inside the sampling radius");
        blendBody.Should().Contain("for (int dx = -radius; dx <= radius; dx++)",
            "biome blend must scan columns inside the sampling radius");
        blendBody.Should().Contain("float distance = Mathf.Sqrt((dx * dx) + (dy * dy));",
            "sample weights must be distance-based, not cardinal-only");
        blendBody.Should().Contain("if (distance > radius)",
            "samples outside the circular radius must be skipped");
        blendBody.Should().Contain("float weight = 1f - (distance / (radius + 1f));",
            "blend strength must decay smoothly as distance increases");
        blendBody.Should().Contain("if (sample.data.tile_id != center.data.tile_id)",
            "different-biome samples must be boosted to soften boundaries");
        blendBody.Should().Contain("totalWeight += weight;",
            "neighbor contributions must accumulate into a normalized weighted average");

        var sampleBody = ExtractMethodBody(core, "static bool TrySampleBaseColor(int x, int y, out Color32 color, out WorldTile tile)");
        sampleBody.Should().Contain("Core.Sphere.IsWrapped",
            "wrapped worlds must wrap horizontal neighbor coordinates");
    }

    [Fact]
    public void MountainSlopeSurface_overlay_class_is_deleted_after_fork_pivot()
    {
        // The former main-mod overlay file is gone; slope smoothing is intrinsic to the
        // fork's height-field mesh. Guard against the overlay class sneaking back into the
        // main mod (which would re-introduce the crossed-quad-era 2.5D overlay regression).
        var slopeFile = Path.Combine(FindRepoRoot(), "WorldSphereMod/Code/Terrain/TerrainSmoothing.cs");
        File.Exists(slopeFile).Should().BeFalse(
            "Terrain/TerrainSmoothing.cs must stay deleted — slope smoothing lives in the fork height-field renderer");

        var core = ReadSource(CoreRelative);
        core.Should().NotContain("MountainSlopeSurface",
            "no main-mod MountainSlopeSurface overlay may be referenced — slope is intrinsic to the fork mesh");
    }

    [Fact]
    public void Core_ApplyPhaseToggle_treats_MountainSlopeSmoothing_as_fork_intrinsic_no_overlay()
    {
        var core = ReadSource(CoreRelative);
        var applyBody = ExtractMethodBody(core, "public static void ApplyPhaseToggle(string flagName, bool newValue)");

        // The setting survives as a fork-facing toggle, but there is no main-mod overlay to
        // create/destroy: ApplyPhaseToggle must NOT wire MountainSlopeSurface lifecycle.
        applyBody.Should().NotContain("MountainSlopeSurface",
            "ApplyPhaseToggle must not create/destroy a main-mod slope overlay — slope is intrinsic to the fork mesh");
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
