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
        script.Should().Contain(@"""VoxelEntities""       = $false");
        script.Should().Contain(@"""CrossedQuadFoliage""  = $true");
        script.Should().Contain(@"""WorldspaceUI""        = $true");
        script.Should().Contain(@"""ParticleEffects""     = $true");

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
        script.Should().Contain("playcua requires 'run-all' subcommand");
    }

    [Fact]
    public void Wsm3d_completion_offers_playcua_run_all()
    {
        var path = Path.Combine(FindRepoRoot(), "Tools", "wsm3d.completion.ps1");
        var completion = File.ReadAllText(path);

        completion.Should().Contain(@"""run-all""");
        completion.Should().Contain("-VisionBackend");
        completion.Should().Contain(@"""omniroute"", ""anthropic"", ""off""");
    }
}
