using System;
using System.IO;
using FluentAssertions;
using Xunit;

public class VoxelRenderInvariantsTests
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
    public void VoxelRender_reset_clears_material_state_and_diagnostics()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        source.Should().Contain("public static void Reset()");
        source.Should().Contain("if (_material != null) Object.Destroy(_material);");
        source.Should().Contain("_materialAttempted = false;");
        source.Should().Contain("SanityTestCube.Reset();");
        source.Should().Contain("_actorVoxelSubmitTranslations.Clear();",
            "reset must clear the per-frame translation scratch list");
        source.Should().Contain("_actorImpostorDiagnosticLogged = false;");
        source.Should().Contain("_actorSkeletalDiagnosticLogged = false;");
    }

    [Fact]
    public void VoxelRender_ensurematerial_prefers_opaque_vertex_color_and_respects_fallback_path()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        source.Should().Contain("public static bool EnsureMaterial()");
        source.Should().Contain("TryCompileInlineVoxelShader();",
            "inline opaque vertex-color shader compilation must be attempted before fallback shaders");
        source.Should().Contain("\"Particles/Standard Surface\"");
        source.Should().Contain("\"Standard\"");
        source.Should().Contain("if (MeshInstanceBatcher.UseFallbackPath && _material != null && _material.enableInstancing)");
        source.Should().Contain("_material.enableInstancing = false;",
            "fallback path must disable instancing when batching cannot use it");
        source.Should().Contain("ConfigureVertexColorShaderMode(m, name);",
            "the resolved shader must be configured for vertex colors");
        source.Should().Contain("m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 1;",
            "voxel meshes must render in the opaque queue after terrain");
    }
}
