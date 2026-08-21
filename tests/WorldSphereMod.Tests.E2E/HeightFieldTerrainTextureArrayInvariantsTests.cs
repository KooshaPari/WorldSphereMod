using System.IO;
using FluentAssertions;
using Xunit;

[Trait("Category", "E2E")]
public class HeightFieldTerrainTextureArrayInvariantsTests
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
        File.Exists(fullPath).Should().BeTrue($"repo file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void Core_exposes_terrain_texture_array_and_binds_heightfield_material()
    {
        var source = ReadRepoFile("WorldSphereMod/Code/Core.cs");

        source.Should().Contain("public static Texture2DArray TerrainTextureArray => Textures",
            "Core must expose the world texture array so terrain code can fetch tiles from one place");

        source.Should().Contain("hfMat.SetTexture(\"_TerrainTexArray\", terrainTexArray)",
            "ConfigureHeightField should bind hf material to terrain texture array");

        source.Should().Contain("hfMat.SetFloat(\"_UseTerrainTexArray\"",
            "ConfigureHeightField should enable terrain-array sampling only when a texture array is available");

        source.Should().Contain("terrainTexArrayLayers",
            "ConfigureHeightField should log terrain array depth for bridge/player diagnostics");
    }

    [Fact]
    public void HeightFieldRenderer_uses_unified_corner_mesh_for_terrain()
    {
        var fullPath = Path.Combine(FindRepoRoot(), "External/Compound-Spheres/CompoundSpheres/HeightFieldRenderer.cs");
        if (!File.Exists(fullPath))
        {
            // Submodule not initialized — skip gracefully
            return;
        }
        var source = File.ReadAllText(fullPath);

        source.Should().Contain("int vertCount = cornerRows * cornerCols",
            "HeightFieldRenderer must use a single unified corner-averaged mesh (not duplicated per-quad geometry)");

        source.Should().Contain("_mesh.SetUVs(0, _uvs, 0, vertCount)",
            "HeightFieldRenderer should upload single UV channel for the unified terrain mesh");
    }

    public static TheoryData<string> OpaqueVertexColorSources =>
        new()
        {
            "WorldSphereMod/Resources/Shaders/OpaqueVertexColor.shader",
            "WorldSphereMod/AssetBundles/Shaders/OpaqueVertexColor.shader",
            "Tools/Unity-Bake-Project/Assets/WSM3D/Shaders/OpaqueVertexColor.shader",
        };

    [Theory]
    [MemberData(nameof(OpaqueVertexColorSources))]
    public void OpaqueVertexColor_shader_supports_terrain_texarray(string relativePath)
    {
        var source = ReadRepoFile(relativePath);

        source.Should().Contain("_TerrainTexArray (\"Terrain TexArray\", 2DArray)",
            "Height terrain sampling must be backed by a texture array input");

        source.Should().Contain("_UseTerrainTexArray (\"Use Terrain TexArray\", Float)",
            "Terrain sampling must be switchable so non-terrain actors keep using _MainTex");

        source.Should().Contain("sampler2DArray _TerrainTexArray;",
            "OpaqueVertexColor must declare the terrain texture array sampler");

        source.Should().Contain("tex2D(_TerrainTexArray",
            "OpaqueVertexColor must sample from the texture array when terrain sampling is enabled");

        source.Should().Contain("texSlice",
            "Terrain shader path should route slice into UV1/TEXCOORD1");
    }
}
