using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// On-disk invariants for wsm3d-playcua YAML sample scenarios used in the agentic live gate.
/// </summary>
public sealed class PlaycuaSampleScenarioInvariantsTests
{
    private const string Phase1ScenarioRelative = "Tools/wsm3d-playcua/sample-scenarios/phase-1-voxel-actors.yaml";
    private const string Phase2ScenarioRelative = "Tools/wsm3d-playcua/sample-scenarios/phase-2-procedural-buildings.yaml";
    private const string Phase3ScenarioRelative = "Tools/wsm3d-playcua/sample-scenarios/phase-3-crossed-quad-foliage.yaml";
    private const string Phase3bCloudScenarioRelative =
        "Tools/wsm3d-playcua/sample-scenarios/phase-3b-cloud-crossed-quad.yaml";
    private const string Phase4ScenarioRelative = "Tools/wsm3d-playcua/sample-scenarios/phase-4-mesh-water.yaml";
    private const string Phase5ScenarioRelative = "Tools/wsm3d-playcua/sample-scenarios/phase-5-high-shadows.yaml";
    private const string BridgeSaveLoadSmokeScenarioRelative =
        "Tools/wsm3d-playcua/sample-scenarios/bridge-save-load-smoke.yaml";
    private const string LiveVerificationDocRelative = "docs/live-verification.md";

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

    private static string ReadRepoFile(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue($"{relativePath} must exist at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Phase_2_procedural_buildings_scenario_exists_and_toggles_saved_setting()
    {
        var yaml = ReadRepoFile(Phase2ScenarioRelative);

        yaml.Should().Contain("name: phase-2-procedural-buildings");
        yaml.Should().Contain("action: toggle_flag");
        yaml.Should().Contain("key: ProceduralBuildings");
        yaml.Should().Contain("action: screenshot");
        yaml.Should().Contain("must_show_procedural_buildings: true");
        yaml.Should().Contain("action: assert_telemetry");
    }

    [Fact]
    public void Phase_1_through_phase_5_playcua_scenarios_are_documented_for_agentic_gate()
    {
        var doc = ReadRepoFile(LiveVerificationDocRelative);

        doc.Should().Contain(Phase1ScenarioRelative);
        doc.Should().Contain(Phase2ScenarioRelative);
        doc.Should().Contain(Phase3ScenarioRelative);
        doc.Should().Contain(Phase3bCloudScenarioRelative);
        doc.Should().Contain(Phase4ScenarioRelative);
        doc.Should().Contain(Phase5ScenarioRelative);
        doc.Should().Contain("PlaycuaSampleScenarioInvariantsTests",
            "live verification doc must point maintainers at scenario guardrails");
    }

    [Fact]
    public void Phase_1_voxel_actors_scenario_exists_and_toggles_saved_setting()
    {
        var yaml = ReadRepoFile(Phase1ScenarioRelative);

        yaml.Should().Contain("name: phase-1-voxel-actors");
        yaml.Should().Contain("action: toggle_flag");
        yaml.Should().Contain("key: VoxelEntities");
        yaml.Should().Contain("action: screenshot");
        yaml.Should().Contain("must_contain_voxel_actors: true");
        yaml.Should().Contain("action: assert_telemetry");
    }

    [Fact]
    public void Phase_3b_cloud_crossed_quad_scenario_toggles_foliage_and_asserts_cloud_vision()
    {
        var yaml = ReadRepoFile(Phase3bCloudScenarioRelative);

        yaml.Should().Contain("name: phase-3b-cloud-crossed-quad");
        yaml.Should().Contain("action: toggle_flag");
        yaml.Should().Contain("key: CrossedQuadFoliage");
        yaml.Should().Contain("must_show_crossed_quad_foliage: true");
        yaml.Should().Contain("must_show_cloud_crossed_quad: true");
        yaml.Should().Contain("phase-3b-cloud-crossed-quad/clouds.png");
        yaml.Should().Contain("action: assert_telemetry");
    }

    [Fact]
    public void Bridge_save_load_smoke_scenario_covers_pre_post_health_and_manual_notes()
    {
        var yaml = ReadRepoFile(BridgeSaveLoadSmokeScenarioRelative);

        yaml.Should().Contain("name: bridge-save-load-smoke");
        yaml.Should().Contain("action: health", "pre- and post-reload health probes");
        yaml.Should().Contain("action: load_save");
        yaml.Should().Contain("optional: true");
        yaml.Should().Contain("MANUAL");
        yaml.Should().Contain("action: assert_telemetry");
        yaml.Should().Contain("Post-reload");
    }

    [Fact]
    public void Phase_3_crossed_quad_foliage_scenario_exists_and_toggles_saved_setting()
    {
        var yaml = ReadRepoFile(Phase3ScenarioRelative);

        yaml.Should().Contain("name: phase-3-crossed-quad-foliage");
        yaml.Should().Contain("action: health");
        yaml.Should().Contain("action: toggle_flag");
        yaml.Should().Contain("key: CrossedQuadFoliage");
        yaml.Should().Contain("action: screenshot");
        yaml.Should().Contain("must_show_crossed_quad_foliage: true");
        yaml.Should().Contain("action: assert_telemetry");
    }

    [Fact]
    public void Phase_4_mesh_water_scenario_exists_and_toggles_saved_setting()
    {
        var yaml = ReadRepoFile(Phase4ScenarioRelative);

        yaml.Should().Contain("name: phase-4-mesh-water");
        yaml.Should().Contain("action: health");
        yaml.Should().Contain("action: toggle_flag");
        yaml.Should().Contain("key: MeshWater");
        yaml.Should().Contain("action: screenshot");
        yaml.Should().Contain("must_show_mesh_water: true");
        yaml.Should().Contain("action: assert_telemetry");
    }

    [Fact]
    public void Phase_5_high_shadows_scenario_exists_and_toggles_saved_settings()
    {
        var yaml = ReadRepoFile(Phase5ScenarioRelative);

        yaml.Should().Contain("name: phase-5-high-shadows");
        yaml.Should().Contain("action: health");
        yaml.Should().Contain("key: HighShadows");
        yaml.Should().Contain("key: HdrSkybox");
        yaml.Should().Contain("action: screenshot");
        yaml.Should().Contain("must_show_high_shadows: true");
        yaml.Should().Contain("must_show_hdr_skybox: true");
        yaml.Should().Contain("action: assert_telemetry");
    }

    [Fact]
    public void Bridge_save_load_smoke_scenario_is_documented_for_agentic_gate()
    {
        var doc = ReadRepoFile(LiveVerificationDocRelative);

        doc.Should().Contain(BridgeSaveLoadSmokeScenarioRelative);
        doc.Should().Contain("bridge-scene-transition-known-issue.md");
    }
}
