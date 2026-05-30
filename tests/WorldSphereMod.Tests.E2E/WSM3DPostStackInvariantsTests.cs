using System;
using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// Static source invariants for the unified WSM3DPostStack.
/// These tests intentionally inspect source text only, so they can run in E2E
/// without needing Unity or WorldBox runtime availability.
/// </summary>
public sealed class WSM3DPostStackInvariantsTests
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

    static string ReadSourceFile(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        File.Exists(fullPath).Should().BeTrue($"source file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
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
    public void WSM3DPostStack_has_all_five_pass_comments_in_order()
    {
        var src = ReadSourceFile("WorldSphereMod/Code/PostFx/WSM3DPostStack.cs");

        int ssaoIdx = src.IndexOf("// Pass 1: SSAO", StringComparison.Ordinal);
        int ssgiIdx = src.IndexOf("// Pass 2: SSGI", StringComparison.Ordinal);
        int bloomIdx = src.IndexOf("// Pass 3: Bloom", StringComparison.Ordinal);
        int acesIdx = src.IndexOf("// Pass 4: ACES", StringComparison.Ordinal);
        int lutIdx = src.IndexOf("// Pass 5: LUT", StringComparison.Ordinal);

        ssaoIdx.Should().BeGreaterThan(0, "SSAO pass comment must exist");
        ssgiIdx.Should().BeGreaterThan(ssaoIdx, "SSGI must follow SSAO");
        bloomIdx.Should().BeGreaterThan(ssgiIdx, "Bloom must follow SSGI");
        acesIdx.Should().BeGreaterThan(bloomIdx, "ACES must follow Bloom");
        lutIdx.Should().BeGreaterThan(acesIdx, "LUT must follow ACES");
    }

    [Fact]
    public void WSM3DPostStack_has_ping_pong_render_texture_management()
    {
        var src = ReadSourceFile("WorldSphereMod/Code/PostFx/WSM3DPostStack.cs");

        src.Should().Contain("RenderTexture _ping",
            "unified post stack must keep a temporary ping-pong target");
        src.Should().Contain("void EnsurePingPong(RenderTexture src)",
            "ping-pong allocation must be isolated in helper method");
        src.Should().Contain("RenderTexture.GetTemporary(src.descriptor)",
            "ping-pong target must be allocated from the source descriptor");
        src.Should().Contain("void ReleasePingPong()",
            "ping-pong target must be released on teardown");
        src.Should().Contain("RenderTexture.ReleaseTemporary(_ping)",
            "ping-pong temporary must be released back to Unity");
        src.Should().Contain("Swap(ref cur, ref next)",
            "pass chain must alternate source and destination render textures");
    }

    [Fact]
    public void WSM3DPostStack_OnRenderImage_checks_Bloom_and_ACES_settings()
    {
        var src = ReadSourceFile("WorldSphereMod/Code/PostFx/WSM3DPostStack.cs");
        var onRenderBody = ExtractMethodBody(src, "void OnRenderImage(RenderTexture src, RenderTexture dst)");

        onRenderBody.Should().Contain("Core.savedSettings.BloomEnabled",
            "OnRenderImage must respect the BloomEnabled setting");
        onRenderBody.Should().Contain("Core.savedSettings.ACESTonemapping",
            "OnRenderImage must respect the ACESTonemapping setting");
        onRenderBody.Should().Contain("Core.savedSettings.ColorGradingLut",
            "final LUT pass must remain gated by the LUT setting");
    }

    [Fact]
    public void WSM3DPostStack_has_legacy_pass_removal_method()
    {
        var src = ReadSourceFile("WorldSphereMod/Code/PostFx/WSM3DPostStack.cs");
        var removeBody = ExtractMethodBody(src, "void RemoveLegacyPasses()");

        removeBody.Should().Contain("ScreenSpaceAO",
            "legacy SSAO pass must be removed when the unified stack attaches");
        removeBody.Should().Contain("ScreenSpaceGI",
            "legacy SSGI pass must be removed when the unified stack attaches");
        removeBody.Should().Contain("ColorGradingLUT",
            "legacy LUT pass must be removed when the unified stack attaches");
    }

    [Fact]
    public void WSM3DPostStack_BRP_shader_resource_paths_match_files_on_disk()
    {
        var root = FindRepoRoot();
        var src = ReadSourceFile("WorldSphereMod/Code/PostFx/WSM3DPostStack.cs");

        src.Should().Contain("_bloomMat = TryLoadMaterial(\"Shaders/BrpBloom\"",
            "Bloom material must load from the BRP shader resource path");
        src.Should().Contain("_acesMat = TryLoadMaterial(\"Shaders/BrpACES\"",
            "ACES material must load from the BRP shader resource path");
        src.Should().Contain("Resources.Load<Shader>(resourcePath)",
            "BRP shader helper must resolve the requested resource path");

        File.Exists(Path.Combine(root, "WorldSphereMod/Resources/Shaders/BrpBloom.shader"))
            .Should().BeTrue("BRP bloom shader file must exist at the resource path used by WSM3DPostStack");
        File.Exists(Path.Combine(root, "WorldSphereMod/Resources/Shaders/BrpACES.shader"))
            .Should().BeTrue("BRP ACES shader file must exist at the resource path used by WSM3DPostStack");
    }
}
