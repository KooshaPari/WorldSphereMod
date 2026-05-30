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
    private const string Phase3ScenarioRelative =
        "Tools/wsm3d-playcua/sample-scenarios/phase-3-crossed-quad-foliage.yaml";
    private const string Phase3bCloudScenarioRelative =
        "Tools/wsm3d-playcua/sample-scenarios/phase-3b-cloud-crossed-quad.yaml";
    private const string Phase4ScenarioRelative =
        "Tools/wsm3d-playcua/sample-scenarios/phase-4-mesh-water.yaml";
    private const string Phase5ScenarioRelative =
        "Tools/wsm3d-playcua/sample-scenarios/phase-5-high-shadows.yaml";
    private const string Phase6ScenarioRelative =
        "Tools/wsm3d-playcua/sample-scenarios/phase-6-skeletal-animation.yaml";
    private const string Phase7ScenarioRelative =
        "Tools/wsm3d-playcua/sample-scenarios/phase-7-worldspace-ui.yaml";
    private const string Phase8ScenarioRelative =
        "Tools/wsm3d-playcua/sample-scenarios/phase-8-day-night.yaml";
    private const string Phase9ScenarioRelative =
        "Tools/wsm3d-playcua/sample-scenarios/phase-9-postfx-particles.yaml";
    private const string Phase10ScenarioRelative =
        "Tools/wsm3d-playcua/sample-scenarios/phase-10-lod.yaml";
    private const string BridgeSaveLoadSmokeScenarioRelative =
        "Tools/wsm3d-playcua/sample-scenarios/bridge-save-load-smoke.yaml";
    private const string LiveVerificationDocRelative = "docs/live-verification.md";
    private const string SmokeTestPhase2Relative = "docs/smoke-test-phase2.md";
    private const string SmokeTestPhase3Relative = "docs/smoke-test-phase3.md";
    private const string SmokeTestPhase4Relative = "docs/smoke-test-phase4.md";
    private const string SmokeTestPhase5Relative = "docs/smoke-test-phase5.md";
    private const string SmokeTestPhase6Relative = "docs/smoke-test-phase6.md";
    private const string SmokeTestPhase7Relative = "docs/smoke-test-phase7.md";
    private const string SmokeTestPhase8Relative = "docs/smoke-test-phase8.md";
    private const string SmokeTestPhase9Relative = "docs/smoke-test-phase9.md";
    private const string SmokeTestPhase10Relative = "docs/smoke-test-phase10.md";

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
    public void Smoke_test_phase2_doc_exists_and_links_playcua_and_live_verification()
    {
        var doc = ReadRepoFile(SmokeTestPhase2Relative);

        doc.Should().Contain(Phase2ScenarioRelative);
        doc.Should().Contain(LiveVerificationDocRelative);
        doc.Should().Contain("ProceduralBuildings");
    }

    [Theory]
    [InlineData(SmokeTestPhase3Relative, Phase3ScenarioRelative, "CrossedQuadFoliage", "phase-3-before.png", "phase-3-after.png", "phase-3-foliage.png")]
    [InlineData(SmokeTestPhase4Relative, Phase4ScenarioRelative, "MeshWater", "phase-4-before.png", "phase-4-after.png", "phase-4-water.png")]
    [InlineData(SmokeTestPhase5Relative, Phase5ScenarioRelative, "HighShadows", "phase-5-before.png", "phase-5-after.png", "phase-5-shadows-sky.png")]
    [InlineData(SmokeTestPhase6Relative, Phase6ScenarioRelative, "SkeletalAnimation", "phase-6-before.png", "phase-6-after.png", "phase-6-actors-rig.png")]
    [InlineData(SmokeTestPhase7Relative, Phase7ScenarioRelative, "WorldspaceUI", "phase-7-before.png", "phase-7-after.png", "phase-7-nameplates.png")]
    [InlineData(SmokeTestPhase8Relative, Phase8ScenarioRelative, "DayNightCycle", "phase-8-before.png", "phase-8-after.png", "phase-8-sky-cycle.png")]
    [InlineData(SmokeTestPhase9Relative, Phase9ScenarioRelative, "PostFX", "phase-9-before.png", "phase-9-after.png", "phase-9-effects.png")]
    [InlineData(SmokeTestPhase10Relative, Phase10ScenarioRelative, "LODScale", "phase-10-before.png", "phase-10-after.png", "phase-10-lod-ladder.png")]
    public void Smoke_test_phase_docs_exist_and_link_playcua_screenshots_and_flags(
        string smokeDocRelative,
        string scenarioRelative,
        string savedSettingKey,
        string beforePng,
        string afterPng,
        string closeupPng)
    {
        var doc = ReadRepoFile(smokeDocRelative);

        doc.Should().Contain(scenarioRelative);
        doc.Should().Contain(LiveVerificationDocRelative);
        doc.Should().Contain(savedSettingKey);
        doc.Should().Contain(beforePng);
        doc.Should().Contain(afterPng);
        doc.Should().Contain(closeupPng);
    }

    [Fact]
    public void Smoke_test_phase5_doc_documents_hdr_skybox_toggle()
    {
        var doc = ReadRepoFile(SmokeTestPhase5Relative);

        doc.Should().Contain("HdrSkybox");
    }

    [Fact]
    public void Smoke_test_phase7_doc_documents_worldspace_label3d_toggle()
    {
        var doc = ReadRepoFile(SmokeTestPhase7Relative);

        doc.Should().Contain("WorldspaceLabel3D");
    }

    [Fact]
    public void Smoke_test_phase9_doc_documents_particle_effects_toggle()
    {
        var doc = ReadRepoFile(SmokeTestPhase9Relative);

        doc.Should().Contain("ParticleEffects");
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
    public void Phase_1_through_10_playcua_scenarios_are_documented_for_agentic_gate()
    {
        var doc = ReadRepoFile(LiveVerificationDocRelative);

        doc.Should().Contain(Phase1ScenarioRelative);
        doc.Should().Contain(Phase2ScenarioRelative);
        doc.Should().Contain(Phase3ScenarioRelative);
        doc.Should().Contain(Phase3bCloudScenarioRelative);
        doc.Should().Contain(Phase4ScenarioRelative);
        doc.Should().Contain(Phase5ScenarioRelative);
        doc.Should().Contain(Phase6ScenarioRelative);
        doc.Should().Contain(Phase7ScenarioRelative);
        doc.Should().Contain(Phase8ScenarioRelative);
        doc.Should().Contain(Phase9ScenarioRelative);
        doc.Should().Contain(Phase10ScenarioRelative);
        doc.Should().Contain(SmokeTestPhase3Relative);
        doc.Should().Contain(SmokeTestPhase4Relative);
        doc.Should().Contain(SmokeTestPhase5Relative);
        doc.Should().Contain(SmokeTestPhase6Relative);
        doc.Should().Contain(SmokeTestPhase7Relative);
        doc.Should().Contain(SmokeTestPhase8Relative);
        doc.Should().Contain(SmokeTestPhase9Relative);
        doc.Should().Contain(SmokeTestPhase10Relative);
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
    public void Phase_3_crossed_quad_foliage_scenario_exists_and_toggles_saved_setting()
    {
        var yaml = ReadRepoFile(Phase3ScenarioRelative);

        yaml.Should().Contain("name: phase-3-crossed-quad-foliage");
        yaml.Should().Contain("action: toggle_flag");
        yaml.Should().Contain("key: CrossedQuadFoliage");
        yaml.Should().Contain("must_show_crossed_quad_foliage: true");
        yaml.Should().Contain("action: assert_telemetry");
    }

    [Fact]
    public void Phase_4_mesh_water_scenario_exists_and_toggles_saved_setting()
    {
        var yaml = ReadRepoFile(Phase4ScenarioRelative);

        yaml.Should().Contain("name: phase-4-mesh-water");
        yaml.Should().Contain("action: toggle_flag");
        yaml.Should().Contain("key: MeshWater");
        yaml.Should().Contain("must_show_mesh_water: true");
        yaml.Should().Contain("action: assert_telemetry");
    }

    [Fact]
    public void Phase_5_high_shadows_scenario_exists_and_toggles_saved_settings()
    {
        var yaml = ReadRepoFile(Phase5ScenarioRelative);

        yaml.Should().Contain("name: phase-5-high-shadows");
        yaml.Should().Contain("action: toggle_flag");
        yaml.Should().Contain("key: HighShadows");
        yaml.Should().Contain("key: HdrSkybox");
        yaml.Should().Contain("must_show_high_shadows: true");
        yaml.Should().Contain("must_show_hdr_skybox: true");
        yaml.Should().Contain("action: assert_telemetry");
    }

    [Fact]
    public void Phase_6_skeletal_animation_scenario_exists_and_toggles_saved_setting()
    {
        var yaml = ReadRepoFile(Phase6ScenarioRelative);

        yaml.Should().Contain("name: phase-6-skeletal-animation");
        yaml.Should().Contain("action: toggle_flag");
        yaml.Should().Contain("key: SkeletalAnimation");
        yaml.Should().Contain("must_show_skeletal_animation: true");
        yaml.Should().Contain("action: assert_telemetry");
    }

    [Fact]
    public void Phase_7_worldspace_ui_scenario_exists_and_toggles_saved_settings()
    {
        var yaml = ReadRepoFile(Phase7ScenarioRelative);

        yaml.Should().Contain("name: phase-7-worldspace-ui");
        yaml.Should().Contain("action: toggle_flag");
        yaml.Should().Contain("key: WorldspaceUI");
        yaml.Should().Contain("key: WorldspaceLabel3D");
        yaml.Should().Contain("must_show_worldspace_ui: true");
        yaml.Should().Contain("action: assert_telemetry");
    }

    [Fact]
    public void Phase_8_day_night_scenario_exists_and_toggles_saved_setting()
    {
        var yaml = ReadRepoFile(Phase8ScenarioRelative);

        yaml.Should().Contain("name: phase-8-day-night");
        yaml.Should().Contain("action: toggle_flag");
        yaml.Should().Contain("key: DayNightCycle");
        yaml.Should().Contain("must_show_day_night_cycle: true");
        yaml.Should().Contain("action: assert_telemetry");
    }

    [Fact]
    public void Phase_9_postfx_particles_scenario_exists_and_toggles_saved_settings()
    {
        var yaml = ReadRepoFile(Phase9ScenarioRelative);

        yaml.Should().Contain("name: phase-9-postfx-particles");
        yaml.Should().Contain("action: toggle_flag");
        yaml.Should().Contain("key: PostFX");
        yaml.Should().Contain("key: ParticleEffects");
        yaml.Should().Contain("must_show_postfx: true");
        yaml.Should().Contain("must_show_particle_effects: true");
        yaml.Should().Contain("action: assert_telemetry");
    }

    [Fact]
    public void Phase_10_lod_scenario_exists_and_sets_lod_scale()
    {
        var yaml = ReadRepoFile(Phase10ScenarioRelative);

        yaml.Should().Contain("name: phase-10-lod");
        yaml.Should().Contain("action: set_setting");
        yaml.Should().Contain("key: LODScale");
        yaml.Should().Contain("must_show_lod_ladder: true");
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
        yaml.Should().Contain("settle_frames:");
        yaml.Should().Contain("bridge_wait_seconds:");
        yaml.Should().Contain("action: assert_telemetry");
        yaml.Should().Contain("Post-reload");
    }

    [Fact]
    public void Bridge_save_load_smoke_scenario_is_documented_for_agentic_gate()
    {
        var doc = ReadRepoFile(LiveVerificationDocRelative);

        doc.Should().Contain(BridgeSaveLoadSmokeScenarioRelative);
        doc.Should().Contain("bridge-scene-transition-known-issue.md");
    }
}
