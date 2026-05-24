using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

public class InstallScriptStructureTests
{
    private static string ReadInstallScript() =>
        TestRepo.ReadRelative("Tools/install.ps1");

    [Fact]
    public void Install_script_has_cmdlet_param_block_and_strict_error_handling()
    {
        var script = ReadInstallScript();

        script.Should().Contain("[CmdletBinding()]");
        script.Should().Contain("param(");
        script.Should().Contain("$ErrorActionPreference = \"Stop\"");
    }

    [Fact]
    public void Install_script_defaults_match_mod_packaging_conventions()
    {
        var script = ReadInstallScript();

        script.Should().Contain("$InstallFolderName = \"WorldSphereMod3D\"");
        script.Should().MatchRegex(@"\$Tfm\s*=\s*""net48""");
        script.Should().Contain("$env:WORLDBOX_PATH");
        script.Should().Contain("worldbox_Data");
    }

    [Fact]
    public void Install_script_runs_dotnet_build_then_copies_six_mod_items()
    {
        var script = ReadInstallScript();

        script.Should().Contain("dotnet build WorldSphereMod.csproj");
        script.Should().Contain("Copy-Item");

        var itemsPattern = @"\$items\s*=\s*@\(\s*""([^""]*)""\s*,\s*""([^""]*)""\s*,\s*""([^""]*)""\s*,\s*""([^""]*)""\s*,\s*""([^""]*)""\s*,\s*""([^""]*)""\s*\)";
        var match = Regex.Match(script, itemsPattern);
        match.Success.Should().BeTrue("install.ps1 must declare the six packaged mod folders/files");

        var entries = Enumerable.Range(1, 6).Select(i => match.Groups[i].Value).ToArray();
        entries.Should().Equal("Code", "Assemblies", "AssetBundles", "GameResources", "Locales", "mod.json");
    }

    [Fact]
    public void Install_script_documents_nml_roslyn_compile_and_skips_dll_double_load()
    {
        var script = ReadInstallScript();

        Regex.IsMatch(script, @"NeoModLoader.*compil|Roslyn", RegexOptions.IgnoreCase)
            .Should().BeTrue("install.ps1 must document that NML compiles Code/ via Roslyn");
        Regex.IsMatch(script, @"skipping.*AssemblyName.*\.dll copy", RegexOptions.IgnoreCase)
            .Should().BeTrue("install.ps1 must skip shipping the prebuilt DLL to avoid NML double-load");
    }
}
