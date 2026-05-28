using System;
using System.IO;
using FluentAssertions;
using Xunit;

public class ToolsInvariantsTests
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
    public void Tools_keeps_key_coordinate_and_camera_helpers_publicly_declared()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Tools.cs");
        source.Should().Contain("public static float GetHeight(this Actor Actor)");
        source.Should().Contain("public static Vector3 RotateLocalPointAroundPivot(ref Vector3 point, ref Vector3 pivot, ref Vector3 angles)");
        source.Should().Contain("public static Vector2Int AsInt(this Vector3 Vector)");
        source.Should().Contain("public static bool ViewPortToRay(this Camera Camera, Vector2 viewportPos, out Ray Ray)");
        source.Should().Contain("public static float GetTileHeightSmooth(this Vector2 Pos)");
        source.Should().Contain("public static class MathStuff");
        source.Should().Contain("public static float WrappedDist(float a, float b)");
    }
}
