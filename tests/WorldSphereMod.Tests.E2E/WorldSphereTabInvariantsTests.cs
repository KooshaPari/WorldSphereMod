using System;
using System.IO;
using FluentAssertions;
using Xunit;

public class WorldSphereTabInvariantsTests
{
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

    private static string ReadSourceFile(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        File.Exists(fullPath).Should().BeTrue($"source file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void WorldSphereTab_begin_builds_the_tab_and_hides_the_phases_window()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/WorldSphereTab.cs");

        source.Should().Contain("public static void Begin()");
        source.Should().Contain("CreateTab();");
        source.Should().Contain("CreateButtons();");
        source.Should().Contain("SuppressPhasesWindow();");
        source.Should().Contain("EnsurePhasesWindowAutoCloseHook();");
        source.Should().Contain("const string PhasesWindowId = \"3D Phases\";");
        source.Should().Contain("const string PhasesWindowTitle = \"phases_window\";");
    }

    [Fact]
    public void WorldSphereTab_phase_window_includes_the_phase_toggle_matrix_and_profile_toggle()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/WorldSphereTab.cs");

        source.Should().Contain("CreateWindowButton(PhasesWindowId, \"WorldSphereMod/ModIcon\", PhasesWindowTitle",
            "the phases UI must be created as a dedicated window");
        source.Should().Contain("\"voxel_entities\"");
        source.Should().Contain("\"procedural_buildings\"");
        source.Should().Contain("\"crossed_quad_foliage\"");
        source.Should().Contain("\"mesh_water\"");
        source.Should().Contain("\"day_night_cycle\"");
        source.Should().Contain("\"post_fx\"");
        source.Should().Contain("\"sanity_cube\"");
        source.Should().Contain("CreateToggleButton(\"ProfileMode\", \"WorldSphereMod/ModIcon\", \"profile_mode\", \"profile_mode_description\", ToggleProfileMode, Core.savedSettings.ProfilerDump);",
            "profiling must remain user-visible from the tab");
    }
}
