using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Source invariants for luminance-based depth complement settings
/// (docs/journeys/scratch/luminance-depth-spec.md).
/// </summary>
public sealed class LuminanceDepthInvariantsTests
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

    static string ReadSource(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(path).Should().BeTrue($"source file must exist at {path}");
        return File.ReadAllText(path);
    }

    static string ExtractMethodBody(string source, string signature)
    {
        int headerIndex = source.IndexOf(signature, StringComparison.Ordinal);
        headerIndex.Should().BeGreaterThanOrEqualTo(0, $"method signature should exist: {signature}");

        int openBrace = source.IndexOf('{', headerIndex);
        openBrace.Should().BeGreaterThanOrEqualTo(0, "method must open with a '{'");

        int depth = 0;
        for (int i = openBrace; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '{')
            {
                depth++;
                continue;
            }

            if (c != '}')
            {
                continue;
            }

            depth--;
            if (depth == 0)
            {
                return source.Substring(openBrace + 1, i - openBrace - 1);
            }
        }

        throw new InvalidOperationException("Unbalanced braces while extracting method body");
    }

    [Fact]
    public void SavedSettings_documents_luminance_depth_spec_and_phase1_defaults()
    {
        var source = ReadSource(@"WorldSphereMod/Code/SavedSettings.cs");
        source.Should().Contain("luminance-depth-spec.md");

        Regex.IsMatch(source, @"public\s+bool\s+VoxelLuminanceDepth\s*=\s*false")
            .Should().BeTrue("feature stays off until SpriteVoxelizer hybrid path ships");
        Regex.IsMatch(source, @"public\s+float\s+VoxelNeutralLuminance\s*=\s*0\.5f")
            .Should().BeTrue("spec neutral_luminance default is 0.5");
        Regex.IsMatch(source, @"public\s+float\s+VoxelShadowRecession\s*=\s*1\.0f")
            .Should().BeTrue("spec shadow_recession default is 1.0");
    }

    [Fact]
    public void Luminance_depth_spec_maps_knobs_to_SavedSettings_fields()
    {
        var spec = ReadSource(@"docs/journeys/scratch/luminance-depth-spec.md");
        spec.Should().Contain("VoxelLuminanceDepth");
        spec.Should().Contain("VoxelNeutralLuminance");
        spec.Should().Contain("VoxelShadowRecession");
        spec.Should().Contain("VoxelSpriteDepth");
        spec.Should().Contain("SpriteVoxelizer");
    }

    [Fact]
    public void Bridge_invalidates_voxel_cache_when_luminance_depth_settings_change()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Bridge/BridgeServer.cs");
        source.Should().Contain("\"VoxelLuminanceDepth\"");
        source.Should().Contain("\"VoxelNeutralLuminance\"");
        source.Should().Contain("\"VoxelShadowRecession\"");
        source.Should().Contain("invalidateVoxel");
    }

    [Fact]
    public void SpriteVoxelizer_luminance_depth_stub_logs_once_and_falls_through_to_existing_paths()
    {
        var voxelizer = ReadSource(@"WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs");

        voxelizer.Should().Contain("_luminanceDepthStubLogged",
            "luminance stub must use a once-only guard flag");
        voxelizer.Should().Contain("LogLuminanceDepthStubOnceIfEnabled()",
            "DT-based build paths must share a single luminance stub entry point");
        voxelizer.Should().Contain("VoxelLuminanceDepth",
            "stub must gate on SavedSettings.VoxelLuminanceDepth");
        voxelizer.Should().Contain("luminance-depth-spec.md",
            "stub must reference the luminance depth spec");

        var balloonBody = ExtractMethodBody(voxelizer, "public static Mesh BuildBalloon(Sprite sprite, int depth, out int[] vertexToTexel)");
        var organicBody = ExtractMethodBody(voxelizer, "public static Mesh BuildOrganicBlob(Sprite sprite, int depth, out int[] vertexToTexel)");
        var perTexelBody = ExtractMethodBody(voxelizer, "public static Mesh BuildPerTexel(Sprite sprite, int depth, out int[] vertexToTexel)");

        foreach (var body in new[] { balloonBody, organicBody, perTexelBody })
        {
            body.Should().Contain("LogLuminanceDepthStubOnceIfEnabled()",
                "each DT-based inflation path must hit the guarded stub before existing logic");
            body.Should().Contain("ResolveDepth(depth)",
                "stub must fall through to the existing depth resolution path");
        }

        balloonBody.Should().Contain("ComputeManhattanDistanceToAir",
            "BuildBalloon must still run the existing DT path after the stub");
        organicBody.Should().Contain("Tools.PerlinNoiseCached",
            "BuildOrganicBlob must still run the existing noise depth path after the stub");
        perTexelBody.Should().Contain("TryEmitTexelFace",
            "BuildPerTexel must still emit per-texel faces after the stub");
    }
}
