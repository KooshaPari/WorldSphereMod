using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using FluentAssertions;

public class InstallScriptInvariantsTests
{
    // Locate the repo root from test output directory.
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

    private static string ReadInstallScript()
    {
        var root = FindRepoRoot();
        var scriptPath = Path.Combine(root, "Tools", "install.ps1");
        File.Exists(scriptPath).Should().BeTrue($"Tools/install.ps1 must exist at {scriptPath}");
        return File.ReadAllText(scriptPath);
    }

    [Fact]
    public void Install_script_does_not_delete_CompoundSpheres_dll()
    {
        var script = ReadInstallScript();

        // The recent regression fix ensures CompoundSpheres.dll is NOT removed
        // because it's a real runtime dependency. Grep for the dangerous pattern.
        var removeItemPattern = @"Remove-Item\s+.*CompoundSpheres";
        var match = Regex.Match(script, removeItemPattern, RegexOptions.IgnoreCase);

        match.Success.Should().BeFalse(
            "install.ps1 must NOT delete CompoundSpheres.dll — it's a real runtime dependency. " +
            "If this test fails, the fix was reverted and the mod will fail with ~60 CS0246 compilation errors.");
    }

    [Fact]
    public void Install_script_targets_net48_and_copies_built_DLL()
    {
        var script = ReadInstallScript();

        // Post-retarget (commit 924fc3d): csproj targets net48 (Mono-loadable
        // via Compat.cs polyfills) and install.ps1 actively copies
        // bin/Release/net48/WorldSphereMod3D.dll into the installed
        // Assemblies/ folder. The earlier invariant was "skip copy because
        // net5.0 is unloadable" — that's inverted now.
        var tfmDefault = Regex.Match(script, @"\$Tfm\s*=\s*""net48""");
        var copyBlock = Regex.Match(script, @"Copy-Item.*\$builtDll", RegexOptions.IgnoreCase);

        tfmDefault.Success.Should().BeTrue(
            "install.ps1 must default $Tfm to net48 — that's the TFM whose DLL is Mono-loadable.");
        copyBlock.Success.Should().BeTrue(
            "install.ps1 must copy the built DLL ($builtDll) into the installed Assemblies/. " +
            "If missing, NML falls back to runtime Roslyn-compile which costs ~1s per launch.");
    }

    [Fact]
    public void Install_script_preserves_items_list_with_all_six_entries()
    {
        var script = ReadInstallScript();

        // The install script must copy: Code, Assemblies, AssetBundles, GameResources, Locales, mod.json
        var itemsPattern = @"\$items\s*=\s*@\(\s*""([^""]*)""\s*,\s*""([^""]*)""\s*,\s*""([^""]*)""\s*,\s*""([^""]*)""\s*,\s*""([^""]*)""\s*,\s*""([^""]*)""\s*\)";
        var match = Regex.Match(script, itemsPattern);

        match.Success.Should().BeTrue(
            "install.ps1 must define $items array with exactly 6 entries: Code, Assemblies, AssetBundles, GameResources, Locales, mod.json");

        if (match.Success)
        {
            // Extract and verify the 6 entries.
            var entries = new[]
            {
                match.Groups[1].Value.Trim(),
                match.Groups[2].Value.Trim(),
                match.Groups[3].Value.Trim(),
                match.Groups[4].Value.Trim(),
                match.Groups[5].Value.Trim(),
                match.Groups[6].Value.Trim()
            };

            entries.Should().Contain("Code", "must include Code");
            entries.Should().Contain("Assemblies", "must include Assemblies");
            entries.Should().Contain("AssetBundles", "must include AssetBundles");
            entries.Should().Contain("GameResources", "must include GameResources");
            entries.Should().Contain("Locales", "must include Locales");
            entries.Should().Contain("mod.json", "must include mod.json");
        }
    }

    [Fact]
    public void Install_script_references_worldbox_path_env_var()
    {
        var script = ReadInstallScript();

        // The script must read $env:WORLDBOX_PATH or provide a fallback to the standard Steam path.
        script.Should().Contain("$env:WORLDBOX_PATH",
            "install.ps1 must reference $env:WORLDBOX_PATH so users can override the install location");

        script.Should().Contain("WORLDBOX_PATH",
            "install.ps1 must support WORLDBOX_PATH environment variable");
    }

    [Fact]
    public void Install_script_verifies_worldbox_directory_before_installing()
    {
        var script = ReadInstallScript();

        // The script must verify that worldbox_Data exists before proceeding.
        script.Should().Contain("worldbox_Data",
            "install.ps1 must check for worldbox_Data subdirectory to confirm WorldBox installation");

        script.Should().Contain("Test-Path",
            "install.ps1 must use Test-Path to verify the WorldBox directory");
    }

    [Fact]
    public void Install_script_contains_copy_item_for_mod_contents()
    {
        var script = ReadInstallScript();

        // The script must use Copy-Item to copy the mod contents to the install destination.
        script.Should().Contain("Copy-Item",
            "install.ps1 must use Copy-Item to copy mod source to the WorldBox Mods folder");
    }

    [Fact]
    public void Install_script_references_neomodeloader_compile_step()
    {
        var script = ReadInstallScript();

        // The script must explain that NeoModLoader compiles Code/*.cs at runtime.
        var nmlCompilePattern = @"NeoModLoader.*compil|Roslyn";
        var match = Regex.Match(script, nmlCompilePattern, RegexOptions.IgnoreCase);

        match.Success.Should().BeTrue(
            "install.ps1 help or comments must mention that NeoModLoader compiles Code/ via Roslyn");
    }
}
