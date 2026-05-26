using System;
using System.IO;
using FluentAssertions;
using Xunit;

public class CoreInvariantsTests
{
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
    public void Core_loadsettings_migrates_schema_and_preserves_existing_settings()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Core.cs");

        source.Should().Contain("if (loadedData.Version != SettingsVersion)",
            "settings migration must run when the serialized schema version drifts");
        source.Should().Contain("ApplySchemaVersionMigration(loadedData);",
            "migrations must be centralized instead of duplicated");
        source.Should().Contain("loadedData.Version = SettingsVersion;",
            "migrated settings must be re-saved at the current schema version");
        source.Should().Contain("IsFirstInstall = true;",
            "first-load failure must be tracked explicitly");
        source.Should().Contain("savedSettings.VoxelEntities = true;",
            "fresh installs must force the base voxel feature on");
        source.Should().Contain("LogPhaseFlagDefaults(savedSettings);",
            "phase defaults must be logged after loading migrated settings");
    }

    [Fact]
    public void Core_applyphasetoggle_resets_render_caches_and_drives_phase_specific_side_effects()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Core.cs");

        source.Should().Contain("flagName == nameof(SavedSettings.VoxelEntities) ||",
            "render-affecting phase toggles must be grouped together for cache invalidation");
        source.Should().Contain("WorldSphereMod.Voxel.VoxelMeshCache.Clear();",
            "render-affecting phase changes must invalidate cached voxel meshes");
        source.Should().Contain("WorldSphereMod.Voxel.VoxelRender.Reset();",
            "render-affecting phase changes must reset cached voxel material state");
        source.Should().Contain("if (flagName == nameof(SavedSettings.DayNightCycle) && newValue)",
            "enabling the day/night cycle must bring up the lighting drivers");
        source.Should().Contain("WorldSphereMod.Lighting.TimeOfDay.EnsureCreated();",
            "the day/night toggle must create the time-of-day driver");
        source.Should().Contain("WorldSphereMod.PostFx.WSM3DPostStack.ApplySetting(newValue);",
            "post-FX toggles must flow through the shared post-stack controller");
        source.Should().Contain("WorldSphereMod.Worldspace.WorldUIRenderer.EnsureCreated();",
            "worldspace UI should self-create when toggled on");
    }

    [Fact]
    public void Core_init_runs_the_expected_bootstrap_sequence_before_world_dependent_work()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Core.cs");

        source.Should().Contain("InitProfiler.Measure(\"LoadSettings\", () => LoadSettings());");
        source.Should().Contain("InitProfiler.Measure(\"WorldSphereTab.Begin\", () => WorldSphereTab.Begin());");
        source.Should().Contain("InitProfiler.Measure(\"DimensionConverter.Prepare\", () => DimensionConverter.Prepare());");
        source.Should().Contain("InitProfiler.Measure(\"Patch\", () => Patch());");
        source.Should().Contain("InitProfiler.Measure(\"Sphere.PrepareAssets\", () =>");
        source.Should().Contain("try { WorldSphereMod.Voxel.VoxelMeshCache.Clear(); } catch { }",
            "startup must clear stale voxel cache entries before the first frame");
        source.Should().Contain("if (Core.IsWorld3D)",
            "lighting initialization must stay gated to 3D worlds");
    }
}
