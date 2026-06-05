using System.IO;
using FluentAssertions;
using Xunit;

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
    public void HeightFieldRenderer_uses_corner_dominant_texture_for_terrain_array_path()
    {
        var source = ReadRepoFile("External/Compound-Spheres/CompoundSpheres/HeightFieldRenderer.cs");

        source.Should().Contain("int[] _cornerTexture",
            "HeightFieldRenderer must store per-corner dominant texture indices so duplicated quads can pick slices consistently");

        source.Should().Contain("DominantTexture(_cornerTexture[bl], _cornerTexture[br], _cornerTexture[tl], _cornerTexture[tr])",
            "Terrain-array path should select a dominant texture slice from surrounding corner samples");

        source.Should().Contain("_mesh.SetUVs(1, _uvsSlice",
            "HeightFieldRenderer should upload texture-slice UV channel for shader sampling");

        source.Should().Contain("int vertCount = _materialSetUsesTerrainTexture ? rowCount * cols * 4",
            "Terrain-array path should use duplicated per-quad geometry");
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
