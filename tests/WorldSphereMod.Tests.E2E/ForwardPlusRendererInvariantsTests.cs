using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Source invariants for Tier 5 Forward+ renderer scaffold
/// (WSM3DRenderer AllocateTargets/DepthPrepass stubs, SavedSettings.ForwardPlusRenderer).
/// </summary>
public sealed class ForwardPlusRendererInvariantsTests
{
    const string RendererRelativePath = "WorldSphereMod/Code/Renderer/WSM3DRenderer.cs";
    const string SettingsRelativePath = "WorldSphereMod/Code/SavedSettings.cs";
    const string ModRelativePath = "WorldSphereMod/Code/Mod.cs";

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
    public void SavedSettings_exposes_ForwardPlusRenderer_default_off()
    {
        var settings = ReadSource(SettingsRelativePath);

        Regex.IsMatch(settings, @"public\s+bool\s+ForwardPlusRenderer\s*=\s*false")
            .Should().BeTrue("Forward+ renderer is Tier 5 opt-in; must default OFF until passes land");
        settings.Should().Contain("forward-plus-renderer-spec.md",
            "setting must reference the Forward+ spec for maintainers");
    }

    [Fact]
    public void WSM3DRenderer_declares_allocate_targets_and_depth_prepass_stub_methods()
    {
        var renderer = ReadSource(RendererRelativePath);

        Regex.IsMatch(renderer, @"\bvoid\s+AllocateTargets\s*\(\s*\)")
            .Should().BeTrue("AllocateTargets stub method must exist");
        Regex.IsMatch(renderer, @"\bvoid\s+DepthPrepass\s*\(\s*\)")
            .Should().BeTrue("DepthPrepass stub method must exist");

        var allocateBody = ExtractMethodBody(renderer, "void AllocateTargets()");
        allocateBody.Should().Contain("GetTemporaryRT(DepthRtId",
            "AllocateTargets must reserve depth prepass RT");
        allocateBody.Should().Contain("GetTemporaryRT(ColorRtId");
        allocateBody.Should().Contain("GetTemporaryRT(AoRtId");
        allocateBody.Should().Contain("_camera.pixelWidth");
        allocateBody.Should().Contain("_camera.pixelHeight");

        var depthPrepassBody = ExtractMethodBody(renderer, "void DepthPrepass()");
        depthPrepassBody.Should().Contain("DepthRtId",
            "DepthPrepass stub must reference depth RT id for future mesh submit");
        depthPrepassBody.Should().Contain("Stub:",
            "DepthPrepass must remain an explicit stub until mesh submit ships");
    }

    [Fact]
    public void WSM3DRenderer_Execute_invokes_stubs_and_gates_on_ForwardPlusRenderer()
    {
        var renderer = ReadSource(RendererRelativePath);

        var executeBody = ExtractMethodBody(renderer, "public void Execute()");
        executeBody.Should().Contain("ForwardPlusRenderer",
            "Execute must gate on SavedSettings.ForwardPlusRenderer");
        executeBody.Should().Contain("_commandBuffer.Clear()");
        executeBody.Should().Contain("AllocateTargets()");
        executeBody.Should().Contain("DepthPrepass()");
    }

    [Fact]
    public void WSM3DRenderer_EnsureCreated_gates_on_ForwardPlusRenderer_and_Mod_wires_it()
    {
        var renderer = ReadSource(RendererRelativePath);
        var mod = ReadSource(ModRelativePath);

        var ensureCreatedBody = ExtractMethodBody(renderer, "public static void EnsureCreated()");
        ensureCreatedBody.Should().Contain("ForwardPlusRenderer",
            "EnsureCreated must gate on SavedSettings.ForwardPlusRenderer");
        ensureCreatedBody.Should().Contain("AddComponent<WSM3DRenderer>()",
            "EnsureCreated must attach renderer when settings allow");

        mod.Should().Contain("WSM3DRenderer.EnsureCreated()",
            "Mod.PostInit must attach renderer after scene transitions");
    }
}
