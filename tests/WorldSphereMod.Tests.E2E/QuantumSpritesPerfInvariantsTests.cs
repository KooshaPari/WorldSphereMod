using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Perf invariants for QuantumSprites calculateactordata3D: keep transform bookkeeping
/// unconditional while skipping expensive sprite/item work when ignore_generic_render is set.
/// </summary>
public sealed class QuantumSpritesPerfInvariantsTests
{
    const string QuantumSpritesRelativePath = "WorldSphereMod/Code/QuantumSprites.cs";

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
                    return source.Substring(brace + 1, i - brace - 1);
                }
            }
        }

        throw new InvalidOperationException($"unbalanced braces in method: {signature}");
    }

    [Fact]
    public void calculateactordata3D_hoists_ignore_generic_render_gate_without_skipping_transform_updates()
    {
        var source = ReadSource(QuantumSpritesRelativePath);
        var body = ExtractMethodBody(source, "public static bool calculateactordata3D(ActorManager __instance)");

        Regex.IsMatch(body, @"bool\s+tHasNormalRender\s*=\s*!tActor\.asset\.ignore_generic_render\s*;")
            .Should().BeTrue("renderability flag must be derived from ignore_generic_render");

        body.Should().Contain(
            "Transform updates must stay unconditional (shadow_position, cur_transform_position).",
            "perf hoist must document why updatePos/Get3DRot cannot be skipped");

        var updatePosIdx = body.IndexOf("updatePos()", StringComparison.Ordinal);
        var getRotIdx = body.IndexOf("Get3DRot()", StringComparison.Ordinal);
        var ignoreRenderIdx = body.IndexOf("ignore_generic_render", StringComparison.Ordinal);

        updatePosIdx.Should().BeGreaterThanOrEqualTo(0);
        getRotIdx.Should().BeGreaterThan(updatePosIdx, "rotation must follow position update");
        ignoreRenderIdx.Should().BeGreaterThan(getRotIdx,
            "ignore_generic_render gate must not skip transform bookkeeping");
    }

    [Fact]
    public void calculateactordata3D_skips_expensive_sprite_and_item_work_when_ignore_generic_render()
    {
        var source = ReadSource(QuantumSpritesRelativePath);
        var body = ExtractMethodBody(source, "public static bool calculateactordata3D(ActorManager __instance)");

        Regex.IsMatch(body,
                @"if\s*\(\s*tHasNormalRender\s*\)\s*\{[\s\S]*?checkHasRenderedItem\s*\(\s*\)")
            .Should().BeTrue("held-item probe must be behind tHasNormalRender gate");

        Regex.IsMatch(body,
                @"if\s*\(\s*tHasNormalRender\s*\)\s*\{[\s\S]*?DynamicSprites\.getCachedAtlasItemSprite")
            .Should().BeTrue("atlas item sprite cache must be behind tHasNormalRender gate");

        Regex.IsMatch(body,
                @"if\s*\(\s*tHasNormalRender\s*\)\s*\{[\s\S]*?calculateMainSprite\s*\(\s*\)")
            .Should().BeTrue("main sprite calculation must be behind tHasNormalRender gate");

        Regex.IsMatch(body, @"render_data\.has_normal_render\s*\[\s*tIndex\s*\]\s*=\s*tHasNormalRender")
            .Should().BeTrue("downstream voxel path must read hoisted has_normal_render flag");
    }

    [Fact]
    public void calculateactordata3D_lazily_fetches_animation_frame_data_only_when_needed()
    {
        var source = ReadSource(QuantumSpritesRelativePath);
        var body = ExtractMethodBody(source, "public static bool calculateactordata3D(ActorManager __instance)");

        Regex.IsMatch(body,
                @"bool\s+tNeedFrameData\s*=\s*\(\s*tShouldRenderUnitShadows\s*&&\s*tActor\.show_shadow\s*\)")
            .Should().BeTrue("frame data need must combine shadow and held-item requirements");

        Regex.IsMatch(body,
                @"AnimationFrameData\s+tFrameData\s*=\s*tNeedFrameData\s*\?\s*tActor\.getAnimationFrameData\s*\(\s*\)\s*:\s*null")
            .Should().BeTrue("getAnimationFrameData must not run unconditionally for every actor");
    }
}
