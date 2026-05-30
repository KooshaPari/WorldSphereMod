using System;
using System.IO;
using FluentAssertions;
using Xunit;

public class BuildingMeshGenInvariantsTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln"))) dir = dir.Parent;
        dir.Should().NotBeNull("repo root with WorldSphereMod.sln must be locatable from test cwd");
        return dir!.FullName;
    }

    private static string ReadSourceFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(fullPath).Should().BeTrue($"source file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void BuildingMeshGen_keeps_transient_mesh_detection_and_generate_signature_stable()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/ProcGen/BuildingMeshGen.cs");
        source.Should().Contain("public static bool IsTransientMesh(Mesh? mesh)");
        source.Should().Contain("public static Mesh? Generate(BuildingAsset asset, BuildingRules rules)");
        source.Should().Contain("static RectInt DetectFootprint(Color32[] px, int w, int h)");
        source.Should().Contain("static int InferStories(Color32[] px, int w, int h, RectInt bbox, BuildingRules rules)");
        source.Should().Contain("static List<DoorSpec> InferOpenings(Color32[] px, int w, int h, RectInt bbox, BuildingRules rules)");
        source.Should().Contain("static Mesh UnitCube(string name)");
    }
}
