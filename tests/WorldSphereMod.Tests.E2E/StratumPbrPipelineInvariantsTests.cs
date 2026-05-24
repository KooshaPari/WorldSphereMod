using System.IO;
using FluentAssertions;
using Xunit;

public class StratumPbrPipelineInvariantsTests
{
    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("repo root with WorldSphereMod.sln must be locatable from test cwd");
        return dir!.FullName;
    }

    static string ReadRepoFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(fullPath).Should().BeTrue($"repo file must exist on disk at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    public static TheoryData<string> StratumPbrShaderSources =>
        new()
        {
            "WorldSphereMod/AssetBundles/Shaders/StratumVoxelPBR.shader",
            "Tools/Unity-Bake-Project/Assets/WSM3D/Shaders/StratumVoxelPBR.shader",
        };

    [Theory]
    [MemberData(nameof(StratumPbrShaderSources))]
    public void StratumPbr_shader_source_exists_on_disk(string relativePath)
    {
        var shader = ReadRepoFile(relativePath);

        shader.Should().Contain("Shader \"WSM3D/StratumVoxelPBR\"",
            "Phase 1 BRP pseudo-PBR shader must register under WSM3D/StratumVoxelPBR");
        shader.Should().Contain("_BaseMap",
            "Stratum PBR material contract requires _BaseMap");
        shader.Should().Contain("_NormalMap",
            "Stratum PBR material contract requires _NormalMap");
        shader.Should().Contain("_OcclusionMap",
            "Stratum PBR material contract requires _OcclusionMap");
        shader.Should().Contain("_MetallicGlossMap",
            "Stratum PBR material contract requires _MetallicGlossMap");
        shader.Should().Contain("_HeightMap",
            "optional height map slot must be declared for phase-2 parallax work");
        shader.Should().Contain("Fallback \"WSM3D/OpaqueVertexColor\"",
            "non-PBR packs must fall back to the existing vertex-color path");
    }

    [Fact]
    public void StratumPbr_legacy_fallback_shader_exists_on_disk()
    {
        ReadRepoFile("WorldSphereMod/AssetBundles/Shaders/OpaqueVertexColor.shader")
            .Should().Contain("Shader \"WSM3D/OpaqueVertexColor\"");
    }

    [Fact]
    public void StratumPbr_spec_documents_implementation_status()
    {
        var spec = ReadRepoFile("docs/journeys/scratch/stratum-pbr-pipeline-spec.md");

        spec.Should().Contain("## 10) Implementation status",
            "spec must track rollout status for Stratum PBR pipeline work");
        spec.Should().Contain("StratumVoxelPBR.shader",
            "implementation status must name the Phase 1 shader scaffold");
    }
}
