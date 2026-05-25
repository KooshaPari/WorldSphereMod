using FluentAssertions;
using Xunit;

/// <summary>
/// Invariants for Tools/wsm-live-verify.ps1 (semi-deterministic live verification harness).
/// </summary>
public class LiveVerifyHarnessStructureTests
{
    private const string HarnessRelative = "Tools/wsm-live-verify.ps1";

    private static string HarnessScript => TestRepo.ReadRelative(HarnessRelative);

    [Fact]
    public void Live_verify_harness_script_exists()
    {
        HarnessScript.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Live_verify_harness_documents_pipeline_stages()
    {
        var script = HarnessScript;

        script.Should().Contain("Stage 1:");
        script.Should().Contain("Stage 2:");
        script.Should().Contain("Stage 3:");
        script.Should().Contain("Stage 4:");

        script.Should().Contain("dotnet test");
        script.Should().Contain("testCounts");
        script.Should().Contain("Get-DotnetTestResultFromOutput");
        script.Should().Contain("verify-journeys.ps1");
        script.Should().Contain("live-verify-latest.json");
        script.Should().Contain("phase-previews");
        script.Should().Contain("Get-PhasePreviewDirectories");
        script.Should().Contain("after.png");
        script.Should().Contain("Missing required live SSIM fixture");
        script.ToLowerInvariant().Should().Contain("ssim");
    }

    [Fact]
    public void Live_verify_harness_requires_after_fixture_for_live_ssim()
    {
        var script = HarnessScript;

        script.Should().Contain("if (-not (Test-Path -LiteralPath $afterFixture))");
        script.Should().Contain("throw \"Missing required live SSIM fixture:");
        script.Should().NotContain("skipped_no_fixture");
    }

    [Fact]
    public void Live_verify_harness_requires_bridge_health_json_object_with_explicit_ok_true()
    {
        var script = HarnessScript;

        script.Should().Contain("$ok = $response.ok");
        script.Should().Contain("$ok -is [bool]");
        script.Should().Contain("$ok -eq $true");
        script.Should().NotContain("$response.ok -ne $false");
    }

    [Fact]
    public void Live_verify_harness_exposes_Live_Vision_and_Phase_parameters()
    {
        var script = HarnessScript;

        script.Should().Contain("[switch]$Live");
        script.Should().Contain("[switch]$Vision");
        script.Should().MatchRegex(@"\[int\]\$Phase");
    }

    [Fact]
    public void Live_verify_harness_wires_vision_backend_omniroute_and_bridge_port_8766()
    {
        var script = HarnessScript;

        script.Should().Contain("--vision-backend");
        script.Should().Contain("omniroute");
        script.Should().Contain("8766");
        script.Should().MatchRegex("wsm3d-playcua");
        script.Should().MatchRegex(@"wsm3d-capture|wsm3d\.ps1");
    }

    [Fact]
    public void Live_verify_harness_preserves_live_report_artifacts_on_failure()
    {
        var script = HarnessScript;

        script.Should().Contain("bridgePort     = $bridgePort");
        script.Should().Contain("playcuaRuns    = @()");
        script.Should().Contain("ssimComparisons = @()");
        script.Should().Contain("Add-StageResult -Id \"live-playcua-ssim\" -Status \"failed\" -Details $liveDetails");
    }

    [Fact]
    public void Live_verify_harness_reuses_journey_mock_verifier_script()
    {
        var script = HarnessScript;

        script.Should().Contain("Tools/verify-journeys.ps1");
        script.Should().MatchRegex(@"journey-mock-verify|verify-journeys");
    }

    [Fact]
    public void Live_verify_harness_live_stage_discovers_all_sample_scenario_yaml_without_phase_filter()
    {
        var script = HarnessScript;

        script.Should().Contain("Get-PlaycuaScenarios");
        script.Should().Contain("sample-scenarios");
        script.Should().Contain("*.yaml");

        var fn = System.Text.RegularExpressions.Regex.Match(
            script,
            @"function Get-PlaycuaScenarios\s*\{([\s\S]*?)\r?\n\}");
        fn.Success.Should().BeTrue("Get-PlaycuaScenarios must exist");
        fn.Groups[1].Value.Should().NotContain(
            "$Phase",
            "PlayCUA discovery must run every sample-scenarios/*.yaml; -Phase only narrows SSIM previews");
    }

    [Fact]
    public void Live_verify_harness_passes_vision_backend_when_Vision_switch_set()
    {
        var script = HarnessScript;

        script.Should().Contain("Get-DefaultPlaycuaVisionBackend");
        script.Should().MatchRegex(
            @"if\s*\(\s*\$Vision\s*\)[\s\S]*--vision-backend",
            "-Vision must forward --vision-backend to wsm3d-playcua");
    }
}
