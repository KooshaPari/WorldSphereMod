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
        script.Should().Contain("verify-journeys.ps1");
        script.Should().Contain("live-verify-latest.json");
        script.Should().Contain("phase-previews");
        script.Should().Contain("Get-PhasePreviewDirectories");
        script.Should().Contain("after.png");
        script.ToLowerInvariant().Should().Contain("ssim");
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
    public void Live_verify_harness_reuses_journey_mock_verifier_script()
    {
        var script = HarnessScript;

        script.Should().Contain("Tools/verify-journeys.ps1");
        script.Should().MatchRegex(@"journey-mock-verify|verify-journeys");
    }
}
