using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

public class Wsm3dCliInvariantsTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var buildProject = Path.Combine(dir.FullName, "WorldSphereMod.csproj");
            if (File.Exists(buildProject))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("repo root with WorldSphereMod.csproj must be locatable from test cwd");
    }

    private static string ReadWsm3dScript()
    {
        var path = Path.Combine(FindRepoRoot(), "Tools", "wsm3d.ps1");
        File.Exists(path).Should().BeTrue($"Tools/wsm3d.ps1 must exist at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Wsm3d_help_documents_phases_enable_all_and_preset_safe_min()
    {
        var script = ReadWsm3dScript();

        script.Should().Contain("phases enable-all");
        script.Should().Contain("phases preset safe-min");
        script.Should().Contain("Invoke-PhasesEnableAll");
        script.Should().Contain("Invoke-PhasesPreset");
    }

    [Fact]
    public void Wsm3d_phases_dispatcher_wires_enable_all_and_preset_subcommands()
    {
        var script = ReadWsm3dScript();

        script.Should().Contain(@"""enable-all""");
        script.Should().MatchRegex(@"""preset""\s*\{");
        script.Should().Contain("Invoke-PhasesEnableAll @params");
        script.Should().Contain("Invoke-PhasesPreset @params");
    }

    [Fact]
    public void Wsm3d_safe_min_preset_matches_saved_settings_factory_defaults()
    {
        var script = ReadWsm3dScript();

        script.Should().Contain("$script:PhaseDefaults = @{");
        script.Should().Contain(@"""VoxelEntities""       = $true");
        script.Should().Contain(@"""CrossedQuadFoliage""  = $false");
        script.Should().Contain(@"""WorldspaceUI""        = $false");
        script.Should().Contain(@"""ParticleEffects""     = $false");

        var presetBlock = Regex.Match(
            script,
            @"function Invoke-PhasesPreset[\s\S]*?switch \(\$Preset\.ToLowerInvariant\(\)\) \{([\s\S]*?)\s+default \{");
        presetBlock.Success.Should().BeTrue("Invoke-PhasesPreset must exist with a switch on preset name");
        presetBlock.Groups[1].Value.Should().Contain("safe-min");
        presetBlock.Groups[1].Value.Should().Contain("$script:PhaseDefaults[$phaseName]");
    }

    [Fact]
    public void Wsm3d_completion_offers_phases_enable_all_and_preset()
    {
        var path = Path.Combine(FindRepoRoot(), "Tools", "wsm3d.completion.ps1");
        var completion = File.ReadAllText(path);

        completion.Should().Contain(@"""list"", ""enable-all"", ""preset""");
        completion.Should().Contain(@"""safe-min""");
    }

    [Fact]
    public void Wsm3d_help_documents_journey_capture_and_verify()
    {
        var script = ReadWsm3dScript();

        script.Should().Contain("journey capture -Id");
        script.Should().Contain("-NonInteractive");
        script.Should().Contain("journey verify -Id");
        script.Should().Contain("function Invoke-JourneyCapture");
        script.Should().Contain("function Invoke-JourneyVerify");
    }

    [Fact]
    public void Wsm3d_journey_dispatcher_wires_capture_and_verify_subcommands()
    {
        var script = ReadWsm3dScript();

        script.Should().MatchRegex(@"""capture""\s*\{");
        script.Should().MatchRegex(@"""verify""\s*\{");
        script.Should().Contain("Invoke-JourneyCapture @params");
        script.Should().Contain("Invoke-JourneyVerify @params");
        script.Should().Contain("journey capture requires a manifest ID (-Id)");
    }

    [Fact]
    public void Wsm3d_completion_offers_journey_capture_and_verify()
    {
        var path = Path.Combine(FindRepoRoot(), "Tools", "wsm3d.completion.ps1");
        var completion = File.ReadAllText(path);

        completion.Should().Contain(@"""verify"", ""capture""");
        completion.Should().Contain("-NonInteractive");
    }

    [Fact]
    public void Wsm3d_help_documents_playcua_run_all()
    {
        var script = ReadWsm3dScript();

        script.Should().Contain("playcua run-all");
        script.Should().Contain("-VisionBackend");
        script.Should().Contain("function Invoke-PlaycuaRunAll");
        script.Should().Contain("sample-scenarios");
    }

    [Fact]
    public void Wsm3d_playcua_dispatcher_wires_run_all_subcommand()
    {
        var script = ReadWsm3dScript();

        script.Should().MatchRegex(@"""run-all""\s*\{");
        script.Should().Contain("Invoke-PlaycuaRunAll @params");
        script.Should().Contain("playcua requires 'run-all' or 'run-bridge' subcommand");
        script.Should().MatchRegex(@"""run-bridge""\s*\{");
        script.Should().Contain("Invoke-PlaycuaRunBridge @params");
    }

    [Fact]
    public void Wsm3d_completion_offers_playcua_run_all()
    {
        var path = Path.Combine(FindRepoRoot(), "Tools", "wsm3d.completion.ps1");
        var completion = File.ReadAllText(path);

        completion.Should().Contain(@"""run-all""");
        completion.Should().Contain("-VisionBackend");
        completion.Should().Contain(@"""fireworks"", ""omniroute"", ""anthropic"", ""off""");
    }

    [Fact]
    public void Wsm3d_help_documents_screenshot_phase_smoke_test_paths()
    {
        var script = ReadWsm3dScript();

        script.Should().Contain("screenshot phase");
        script.Should().Contain("docs/screenshots/phase-N-");
        script.Should().Contain("function Invoke-ScreenshotPhase");
        script.Should().Contain("function Get-PhaseScreenshotPath");
        script.Should().Contain("docs/smoke-test-phase");
    }

    [Fact]
    public void Wsm3d_screenshot_dispatcher_wires_phase_subcommand()
    {
        var script = ReadWsm3dScript();

        script.Should().Contain(@"""phase""");
        script.Should().Contain("Invoke-ScreenshotPhase @params");
        script.Should().Contain("screenshot phase requires -Name");
        script.Should().Contain("phase-$Phase-$Name.png");
    }

    [Theory]
    [InlineData(1, "docs/smoke-test-phase1.md", "phase-1-before.png", "phase-1-after.png", "phase-1-buildings.png", "buildings")]
    [InlineData(2, "docs/smoke-test-phase2.md", "phase-2-before.png", "phase-2-after.png", "phase-2-buildings.png", "buildings")]
    [InlineData(3, "docs/smoke-test-phase3.md", "phase-3-before.png", "phase-3-after.png", "phase-3-foliage.png", "foliage")]
    [InlineData(4, "docs/smoke-test-phase4.md", "phase-4-before.png", "phase-4-after.png", "phase-4-water.png", "water")]
    [InlineData(5, "docs/smoke-test-phase5.md", "phase-5-before.png", "phase-5-after.png", "phase-5-shadows-sky.png", "shadows-sky")]
    [InlineData(6, "docs/smoke-test-phase6.md", "phase-6-before.png", "phase-6-after.png", "phase-6-actors-rig.png", "actors-rig")]
    [InlineData(7, "docs/smoke-test-phase7.md", "phase-7-before.png", "phase-7-after.png", "phase-7-nameplates.png", "nameplates")]
    [InlineData(8, "docs/smoke-test-phase8.md", "phase-8-before.png", "phase-8-after.png", "phase-8-sky-cycle.png", "sky-cycle")]
    [InlineData(9, "docs/smoke-test-phase9.md", "phase-9-before.png", "phase-9-after.png", "phase-9-effects.png", "effects")]
    [InlineData(10, "docs/smoke-test-phase10.md", "phase-10-before.png", "phase-10-after.png", "phase-10-lod-ladder.png", "lod-ladder")]
    public void Wsm3d_phase_screenshot_names_match_smoke_test_docs(
        int phase,
        string smokeDocRelative,
        string beforePng,
        string afterPng,
        string closeupPng,
        string closeupSlug)
    {
        var root = FindRepoRoot();
        var smokeTest = File.ReadAllText(Path.Combine(root, smokeDocRelative.Replace('/', Path.DirectorySeparatorChar)));

        var script = ReadWsm3dScript();
        script.Should().Contain($@"{phase} = @(""before"", ""after"", ""{closeupSlug}"")");

        smokeTest.Should().Contain(beforePng);
        smokeTest.Should().Contain(afterPng);
        smokeTest.Should().Contain(closeupPng);
    }

    [Fact]
    public void Wsm3d_completion_offers_screenshot_phase_and_smoke_test_slugs()
    {
        var path = Path.Combine(FindRepoRoot(), "Tools", "wsm3d.completion.ps1");
        var completion = File.ReadAllText(path);

        completion.Should().Contain(@"""phase""");
        completion.Should().Contain("-Name");
        completion.Should().Contain("before");
        completion.Should().Contain("after");
        completion.Should().Contain("buildings");
        completion.Should().Contain("foliage");
        completion.Should().Contain("water");
        completion.Should().Contain("shadows-sky");
        completion.Should().Contain("actors-rig");
        completion.Should().Contain("nameplates");
        completion.Should().Contain("sky-cycle");
        completion.Should().Contain("effects");
        completion.Should().Contain("lod-ladder");
    }

    [Fact]
    public void Smoke_test_phase1_journey_manifest_exists_and_links_checklist_doc()
    {
        var root = FindRepoRoot();
        var manifestPath = Path.Combine(root, "docs", "journeys", "manifests", "smoke-test-phase1", "manifest.json");
        File.Exists(manifestPath).Should().BeTrue($"journey verify -Id smoke-test-phase1 requires {manifestPath}");

        var manifest = File.ReadAllText(manifestPath);
        manifest.Should().Contain("docs/smoke-test-phase1.md");

        var indexPath = Path.Combine(root, "docs", "journeys", "manifests", "index.json");
        var index = File.ReadAllText(indexPath);
        index.Should().Contain("smoke-test-phase1");
        index.Should().Contain("docs/smoke-test-phase1.md");
    }

    [Fact]
    public void Smoke_test_phase2_journey_manifest_indexed_with_checklist_doc()
    {
        var root = FindRepoRoot();
        var manifestPath = Path.Combine(root, "docs", "journeys", "manifests", "smoke-test-phase2", "manifest.json");
        File.Exists(manifestPath).Should().BeTrue();

        File.ReadAllText(manifestPath).Should().Contain("docs/smoke-test-phase2.md");
        File.ReadAllText(Path.Combine(root, "docs", "journeys", "manifests", "index.json"))
            .Should().Contain("smoke-test-phase2");
    }

    [Fact]
    public void Install_script_failure_paths_suggest_wsm3d_doctor()
    {
        var path = Path.Combine(FindRepoRoot(), "Tools", "install.ps1");
        var script = File.ReadAllText(path);

        script.Should().Contain("function Write-InstallFailureHint");
        script.Should().Contain("wsm3d.ps1 doctor");
        script.Should().Contain("Write-InstallFailureHint");
    }

    [Fact]
    public void Wsm3d_install_and_relaunch_forward_NoBuild_as_named_SkipBuild()
    {
        var script = ReadWsm3dScript();

        var installBlock = Regex.Match(
            script,
            @"function Invoke-Install[\s\S]*?function Invoke-Launch");
        installBlock.Success.Should().BeTrue("Invoke-Install must exist before Invoke-Launch");
        installBlock.Groups[0].Value.Should().Contain("$installParams[\"SkipBuild\"] = $true");
        installBlock.Groups[0].Value.Should().Contain("& (Join-Path $ToolsDir \"install.ps1\") @installParams");

        var relaunchBlock = Regex.Match(
            script,
            @"function Invoke-Relaunch[\s\S]*?function Invoke-Log");
        relaunchBlock.Success.Should().BeTrue("Invoke-Relaunch must exist before Invoke-Log");
        relaunchBlock.Groups[0].Value.Should().Contain("$installParams = @{");
        relaunchBlock.Groups[0].Value.Should().Contain("Launch = $true");
        relaunchBlock.Groups[0].Value.Should().Contain("$installParams[\"NoBuild\"] = $true");
        relaunchBlock.Groups[0].Value.Should().Contain("Invoke-Install @installParams");
    }

    [Fact]
    public void Wsm3d_help_documents_doctor_subcommand()
    {
        var script = ReadWsm3dScript();

        script.Should().Contain("doctor [-Json]");
        script.Should().Contain("BridgeRPC :8766");
        script.Should().Contain("OmniRoute :20128");
        script.Should().Contain("function Invoke-Doctor");
        script.Should().Contain("git_submodules");
        script.Should().Contain("phenotype_journey");
    }

    [Fact]
    public void Wsm3d_doctor_dispatcher_wires_json_flag()
    {
        var script = ReadWsm3dScript();

        script.Should().MatchRegex(@"""doctor""\s*\{");
        script.Should().Contain("Invoke-Doctor @params");
    }

    [Fact]
    public void Wsm3d_completion_offers_doctor_and_json_flag()
    {
        var path = Path.Combine(FindRepoRoot(), "Tools", "wsm3d.completion.ps1");
        var completion = File.ReadAllText(path);

        completion.Should().Contain(@"""doctor""");
        completion.Should().Contain("\"doctor\" {");
        completion.Should().Contain("-Json");
    }

    [Fact]
    public void Wsm3d_help_documents_submodule_init()
    {
        var script = ReadWsm3dScript();

        script.Should().Contain("submodule init");
        script.Should().Contain("External/Compound-Spheres");
        script.Should().Contain("function Invoke-SubmoduleInit");
        script.Should().Contain("git submodule update --init --recursive");
        script.Should().Contain("wsm3d submodule init");
    }

    [Fact]
    public void Wsm3d_submodule_dispatcher_wires_init_subcommand()
    {
        var script = ReadWsm3dScript();

        script.Should().MatchRegex(@"""init""\s*\{");
        script.Should().Contain("Invoke-SubmoduleInit");
        script.Should().Contain("submodule requires 'init' subcommand");
        script.Should().Contain("$script:GitSubmodulePaths");
    }

    [Fact]
    public void Wsm3d_doctor_git_submodules_fail_recommends_submodule_init()
    {
        var script = ReadWsm3dScript();

        script.Should().Contain("remediation = \"Run: wsm3d submodule init\"");
        script.Should().Contain("not initialized — run: wsm3d submodule init");
    }

    [Fact]
    public void Wsm3d_completion_offers_submodule_init()
    {
        var path = Path.Combine(FindRepoRoot(), "Tools", "wsm3d.completion.ps1");
        var completion = File.ReadAllText(path);

        completion.Should().Contain(@"""submodule""");
        completion.Should().Contain("\"submodule\" {");
        completion.Should().Contain(@"""init""");
    }

    [Fact]
    public void Setup_compound_spheres_3d_script_documents_fork_and_links_phase5_prep()
    {
        var path = Path.Combine(FindRepoRoot(), "Tools", "setup-compound-spheres-3d.ps1");
        var script = File.ReadAllText(path);

        script.Should().Contain("KooshaPari/Compound-Spheres-3D");
        script.Should().Contain("docs/phase5-prep.md");
        script.Should().Contain("# git submodule add");
        script.Should().Contain("PLACEHOLDER");
    }

    [Fact]
    public void Wsm3d_help_documents_setup_phase5()
    {
        var script = ReadWsm3dScript();

        script.Should().Contain("setup phase5");
        script.Should().Contain("setup-compound-spheres-3d.ps1");
        script.Should().Contain("docs/phase5-prep.md");
        script.Should().Contain("function Invoke-SetupPhase5");
    }

    [Fact]
    public void Wsm3d_setup_dispatcher_wires_phase5_subcommand()
    {
        var script = ReadWsm3dScript();

        script.Should().MatchRegex(@"""setup""\s*\{");
        script.Should().Contain("Invoke-SetupPhase5");
        script.Should().Contain("setup requires 'phase5' subcommand");
    }

    [Fact]
    public void Wsm3d_completion_offers_setup_phase5()
    {
        var path = Path.Combine(FindRepoRoot(), "Tools", "wsm3d.completion.ps1");
        var completion = File.ReadAllText(path);

        completion.Should().Contain(@"""setup""");
        completion.Should().Contain("\"setup\" {");
        completion.Should().Contain(@"""phase5""");
    }

    [Fact]
    public void Wsm3d_status_reads_live_verify_report_test_counts()
    {
        var script = ReadWsm3dScript();

        script.Should().Contain("live-verify-latest.json");
        script.Should().Contain("function Get-LiveVerifyReportSummary");
        script.Should().Contain("dotnet-tests");
        script.Should().Contain("testCounts");
        script.Should().Contain("LiveVerify");
    }

    [Fact]
    public void Wsm3d_help_documents_status_live_verify_test_counts()
    {
        var script = ReadWsm3dScript();

        script.Should().Contain("status [-Json]");
        script.Should().Contain("live-verify test counts");
        script.Should().Contain("Tools/.reports/live-verify-latest.json");
    }

    [Fact]
    public void Wsm3d_help_documents_validate_offline_live_verify()
    {
        var script = ReadWsm3dScript();

        script.Should().Contain("validate");
        script.Should().Contain("wsm-live-verify.ps1");
        script.Should().Contain("function Invoke-Validate");
        script.Should().Contain("/wsm-validate-all");
        script.Should().Contain("live-verify-gate.yml");
        script.Should().Contain("Tools/.reports/live-verify-latest.json");
    }

    [Fact]
    public void Wsm3d_validate_dispatcher_wires_invoke_validate()
    {
        var script = ReadWsm3dScript();

        script.Should().MatchRegex(@"""validate""\s*\{");
        script.Should().Contain("Invoke-Validate");
        script.Should().Contain("Tools/wsm-live-verify.ps1");
        script.Should().Contain("offline CI gate");
    }

    [Fact]
    public void Wsm3d_completion_offers_validate()
    {
        var path = Path.Combine(FindRepoRoot(), "Tools", "wsm3d.completion.ps1");
        var completion = File.ReadAllText(path);

        completion.Should().Contain(@"""validate""");
    }
}
