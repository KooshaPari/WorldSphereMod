using System;
using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// Invariants for QuantumSprites calculateactordata3D: parallel transform bookkeeping only
/// (sprite/item work lives elsewhere after perf refactor).
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
    public void calculateactordata3D_writes_parallel_transform_positions_and_rotations()
    {
        var source = ReadSource(QuantumSpritesRelativePath);
        var body = ExtractMethodBody(source, "public static void calculateactordata3D(ActorManager __instance)");

        body.Should().Contain("Parallel.For", "visible actors must be processed in parallel batches");
        body.Should().Contain("tActor.updatePos()", "each actor position must be updated");
        body.Should().Contain("tActor.Get3DRot()", "each actor rotation must be updated");
        body.Should().Contain("render_data.positions[tIndex]", "positions must be written to render_data");
        body.Should().Contain("render_data.rotations[tIndex]", "rotations must be written to render_data");
        body.Should().Contain("Core.IsWorld3D", "patch must no-op when world is not 3D");
    }

    [Fact]
    public void calculateactordata3D_does_not_call_expensive_sprite_paths_in_postfix()
    {
        var source = ReadSource(QuantumSpritesRelativePath);
        var body = ExtractMethodBody(source, "public static void calculateactordata3D(ActorManager __instance)");

        body.Should().NotContain("calculateMainSprite", "sprite work must not run in precalculate postfix");
        body.Should().NotContain("getCachedAtlasItemSprite", "atlas cache must not run in precalculate postfix");
        body.Should().NotContain("checkHasRenderedItem", "held-item probe must not run in precalculate postfix");
    }
}
