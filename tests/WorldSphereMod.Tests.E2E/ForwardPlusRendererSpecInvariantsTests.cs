using System;
using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// Source invariants for docs/specs/forward-plus-renderer-spec.md — Tier 5 Forward+
/// CommandBuffer renderer scaffold and dimension constants.
/// </summary>
public sealed class ForwardPlusRendererSpecInvariantsTests
{
    const string SpecRelativePath = "docs/specs/forward-plus-renderer-spec.md";
    const string RendererRelativePath = "WorldSphereMod/Code/Renderer/WSM3DRenderer.cs";

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
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"method signature not found: {signature}");
        var brace = source.IndexOf('{', start);
        brace.Should().BeGreaterThanOrEqualTo(0);

        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(brace, i - brace + 1);
                }
            }
        }

        throw new InvalidOperationException($"unbalanced braces in method: {signature}");
    }

    [Fact]
    public void Spec_documents_implementation_status_and_tile_grid_constants()
    {
        var spec = ReadSource(SpecRelativePath);

        spec.Should().Contain("## Implementation status",
            "spec must record shipped vs target so Tier-5 work is traceable");
        spec.Should().Contain("AllocateTargets",
            "status table must name RT allocation stub");
        spec.Should().Contain("Depth prepass",
            "status table must name depth prepass stub");
        spec.Should().Contain("**Stub**",
            "status table must distinguish shipped vs stub targets");
        spec.Should().Contain("16×16",
            "spec must document tile size for light culling");
        spec.Should().Contain("8160 tiles",
            "spec must document reference tile count at 1080p");
        spec.Should().Contain("ForwardPlusRendererSpecInvariantsTests",
            "spec must point maintainers at e2e guardrails");
    }

    [Fact]
    public void WSM3DRenderer_exposes_forward_plus_dimension_constants_and_allocate_targets_stub()
    {
        var renderer = ReadSource(RendererRelativePath);

        renderer.Should().Contain("public const int TileSizePx = 16");
        renderer.Should().Contain("public const int ReferenceWidthPx = 1920");
        renderer.Should().Contain("public const int ReferenceHeightPx = 1080");
        renderer.Should().Contain("public const int ReferenceTileCountX = 120");
        renderer.Should().Contain("public const int ReferenceTileCountY = 68");
        renderer.Should().Contain("public const int ReferenceTileCount = 8160");
        renderer.Should().Contain("public const int MaxLightsPerTile = 32");
        renderer.Should().Contain("public const int MaxDynamicLights = 256");
        renderer.Should().Contain("public static int TileCountX(int screenWidthPx)");
        renderer.Should().Contain("public static int TileCountY(int screenHeightPx)");

        var allocateBody = ExtractMethodBody(renderer, "void AllocateTargets()");
        allocateBody.Should().Contain("GetTemporaryRT(DepthRtId",
            "depth prepass target must use spec depth RT id");
        allocateBody.Should().Contain("GetTemporaryRT(ColorRtId");
        allocateBody.Should().Contain("GetTemporaryRT(AoRtId");
        allocateBody.Should().Contain("RenderTextureFormat.RFloat",
            "depth RT must match spec R32_SFloat intent");
        allocateBody.Should().Contain("_camera.pixelWidth");
        allocateBody.Should().Contain("_camera.pixelHeight");

        var executeBody = ExtractMethodBody(renderer, "public void Execute()");
        executeBody.Should().Contain("AllocateTargets()",
            "Execute must invoke AllocateTargets after Clear each frame");
        executeBody.Should().Contain("DepthPrepass()",
            "Execute must invoke DepthPrepass after AllocateTargets each frame");

        var depthPrepassBody = ExtractMethodBody(renderer, "void DepthPrepass()");
        depthPrepassBody.Should().Contain("DepthRtId",
            "DepthPrepass stub must reference depth RT id for future mesh submit");
    }
}
