using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// Integration-style checks for Tools/wsm-live-verify.ps1 wiring against SSIM, PlayCUA, and live-verification.md.
/// </summary>
public class LiveVerifyScriptIntegrationTests
{
    private const string LiveVerifyScriptRelative = "Tools/wsm-live-verify.ps1";
    private const string SsimCompareRelative = "Tools/wsm-ssim-compare.py";
    private const string PlaycuaMainRelative = "Tools/wsm3d-playcua/main.py";
    private const string LiveVerificationDocRelative = "docs/live-verification.md";

    [Fact]
    public void Wsm_live_verify_script_exists_and_documents_ssim_threshold_contract()
    {
        var script = TestRepo.ReadRelative(LiveVerifyScriptRelative);

        script.Should().Contain("wsm-ssim-compare.py");
        script.Should().Contain("--threshold");
        script.Should().Contain("0.95");
    }

    [Fact]
    public void Wsm_live_verify_invokes_playcua_with_omniroute_vision_backend()
    {
        var script = TestRepo.ReadRelative(LiveVerifyScriptRelative);
        var main = TestRepo.ReadRelative(PlaycuaMainRelative);

        script.Should().Contain("main.py");
        script.Should().Contain("--vision-backend");
        script.Should().Contain("omniroute");

        main.Should().Contain("--vision-backend");
        main.Should().Contain("omniroute");
    }

    [Fact]
    public void Live_verification_doc_points_at_orchestrator_and_ssim_helper()
    {
        var doc = TestRepo.ReadRelative(LiveVerificationDocRelative);
        var script = TestRepo.ReadRelative(LiveVerifyScriptRelative);

        doc.Should().Contain("wsm-live-verify.ps1");
        doc.Should().Contain("wsm-ssim-compare.py");
        doc.Should().Contain("0.95");

        script.Should().Contain("live-verify-latest.json");
    }

    [Fact]
    public void Ssim_compare_helper_exists_for_live_verify_json_contract()
    {
        var root = TestRepo.FindRoot();
        var ssimPath = Path.Combine(root, SsimCompareRelative.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(ssimPath).Should().BeTrue(SsimCompareRelative);

        var ssim = File.ReadAllText(ssimPath);
        ssim.Should().Contain("\"ok\"");
        ssim.Should().Contain("\"ssim\"");
        ssim.Should().Contain("\"threshold\"");
        ssim.Should().Contain("default=0.95");
    }

    [Fact]
    public void Wsm_live_verify_live_stage_discovers_all_sample_scenarios_yaml()
    {
        var root = TestRepo.FindRoot();
        var scenarioDir = Path.Combine(root, "Tools", "wsm3d-playcua", "sample-scenarios");
        Directory.Exists(scenarioDir).Should().BeTrue();

        var yamlCount = Directory.GetFiles(scenarioDir, "*.yaml").Length;
        yamlCount.Should().BeGreaterThan(0);

        var script = TestRepo.ReadRelative(LiveVerifyScriptRelative);
        script.Should().Contain("Get-PlaycuaScenarios");
        script.Should().Contain("foreach ($scenario in $scenarios)");
        script.Should().MatchRegex(
            @"function Get-PlaycuaScenarios\s*\{[\s\S]*Get-ChildItem[\s\S]*\.yaml");
    }

    [Fact]
    public void Wsm_live_verify_live_stage_ssim_loops_all_phase_previews_with_after_png_when_capture_succeeds()
    {
        var script = TestRepo.ReadRelative(LiveVerifyScriptRelative);

        script.Should().Contain("Get-PhasePreviewDirectories");
        script.Should().Contain("docs/journeys/phase-previews");
        script.Should().Contain("after.png");
        script.Should().Contain("Invoke-SsimCompare");
        script.Should().Contain("Invoke-WindowCapture");
        script.Should().Contain("skipped_no_fixture");
        script.Should().Contain("skipped_capture_failed");
        script.Should().Contain("ssim-captures");
    }
}
