using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// On-disk invariants for the live verification harness: orchestrator script, PlayCUA runner, and docs.
/// </summary>
public class LiveVerificationHarnessInvariantsTests
{
    private const string LiveVerifyScriptRelative = "Tools/wsm-live-verify.ps1";
    private const string PlaycuaMainRelative = "Tools/wsm3d-playcua/main.py";
    private const string LiveVerificationDocRelative = "docs/live-verification.md";
    private const string OmnirouteVisionEnvExampleRelative = "Tools/omniroute-vision.env.example";

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
    public void Live_verification_harness_artifacts_exist_on_disk()
    {
        var root = FindRepoRoot();

        File.Exists(Path.Combine(root, LiveVerifyScriptRelative.Replace('/', Path.DirectorySeparatorChar)))
            .Should().BeTrue(LiveVerifyScriptRelative);
        File.Exists(Path.Combine(root, PlaycuaMainRelative.Replace('/', Path.DirectorySeparatorChar)))
            .Should().BeTrue(PlaycuaMainRelative);
        File.Exists(Path.Combine(root, LiveVerificationDocRelative.Replace('/', Path.DirectorySeparatorChar)))
            .Should().BeTrue(LiveVerificationDocRelative);
    }

    [Fact]
    public void Playcua_main_exposes_vision_backend_with_omniroute_choice()
    {
        var main = ReadRepoFile(PlaycuaMainRelative);

        main.Should().Contain("--vision-backend");
        main.Should().Contain("choices=[\"omniroute\", \"anthropic\", \"off\"]");
        main.Should().Contain("OmniRouteVisionValidator");
        main.Should().Contain("if backend == \"omniroute\":");
    }

    [Fact]
    public void Live_verification_doc_documents_programmatic_and_agentic_gates()
    {
        var doc = ReadRepoFile(LiveVerificationDocRelative);

        doc.Should().Contain("Programmatic gate");
        doc.Should().Contain("Agentic gate");
        doc.Should().Contain("wsm3d-playcua");
        doc.Should().Contain("OmniRouteVisionValidator");
        doc.Should().Contain("OMNROUTE_VISION_COMBO");
        doc.Should().Contain("127.0.0.1:8766");
        doc.Should().Contain("bridge-health-vision.yaml");
        doc.Should().Contain("omniroute-vision.env.example");
    }

    [Fact]
    public void Omniroute_vision_env_example_exists_with_required_keys()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, OmnirouteVisionEnvExampleRelative.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue(OmnirouteVisionEnvExampleRelative);

        var content = File.ReadAllText(path);
        content.Should().Contain("OMNROUTE_BASE_URL=http://127.0.0.1:20128/v1");
        content.Should().Contain("OMNROUTE_API_KEY=");
        content.Should().Contain("OMNROUTE_VISION_COMBO=wsm3d-vision-frontier");
    }

    [Fact]
    public void Wsm_live_verify_script_references_ssim_playcua_and_vision_backend()
    {
        var script = ReadRepoFile(LiveVerifyScriptRelative);

        script.Should().Contain("[CmdletBinding()]");
        script.Should().Contain("param(");
        script.Should().Contain("$ErrorActionPreference = \"Stop\"");
        script.Should().Contain("wsm-ssim-compare.py");
        script.Should().Contain("0.95");
        script.Should().Contain("wsm3d-playcua");
        script.Should().Contain("--vision-backend");
        script.Should().Contain("omniroute");
    }
}
