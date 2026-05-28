using System;
using System.IO;
using FluentAssertions;
using Xunit;

public class MeshInstanceBatcherInvariantsTests
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
    public void MeshInstanceBatcher_exposes_submit_flush_and_reset_controls()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs");
        source.Should().Contain("public static void SetMainThread()");
        source.Should().Contain("public static void Submit(Mesh mesh, Material mat, Matrix4x4 matrix, Color tint)");
        source.Should().Contain("public static void Flush(int layer = 0, ShadowCastingMode shadows = ShadowCastingMode.On, bool receive = true)");
        source.Should().Contain("public static void Reset()");
        source.Should().Contain("public static void ForceFallbackPath()");
        source.Should().Contain("public static bool HasPendingSubmissions");
    }
}
