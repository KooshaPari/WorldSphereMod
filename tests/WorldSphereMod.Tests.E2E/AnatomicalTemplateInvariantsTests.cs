using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Source invariants for anatomical template pipeline stub
/// (docs/journeys/scratch/anatomical-template-spec.md).
/// </summary>
public sealed class AnatomicalTemplateInvariantsTests
{
    const string SpecRelativePath = "docs/journeys/scratch/anatomical-template-spec.md";

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
    public void Anatomical_template_spec_documents_stub_deferral_and_extrusion_fallback()
    {
        var spec = ReadSource(SpecRelativePath);

        spec.Should().Contain("AnatomicalTemplatePipeline.ShouldUseTemplate");
        spec.Should().Contain("TryBuildColorizedTemplate");
        spec.Should().Contain("Stub — always defers");
        spec.Should().Contain("TryGetTemplate` returns false for all rigs");
        spec.Should().Contain("SpriteVoxelizer.Build()");
    }

    [Fact]
    public void AnatomicalTemplateRegistry_TryGetTemplate_stub_always_returns_false()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Voxel/AnatomicalTemplateRegistry.cs");
        var body = ExtractMethodBody(source, "public static bool TryGetTemplate(RigType rigType, out AnatomicalTemplate template)");

        body.Should().Contain("template = default");
        body.Should().Contain("RigType.None");
        body.Should().Contain("RigType.Static");
        Regex.IsMatch(body, @"return\s+false\s*;", RegexOptions.Multiline)
            .Should().BeTrue("registry must defer until canonical coordinate volumes are authored");
    }

    [Fact]
    public void AnatomicalTemplatePipeline_ShouldUseTemplate_defers_via_registry_and_validation()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Voxel/AnatomicalTemplatePipeline.cs");
        var body = ExtractMethodBody(source, "public static bool ShouldUseTemplate(RigType rigType)");

        body.Should().Contain("RigType.None");
        body.Should().Contain("AnatomicalTemplateRegistry.TryGetTemplate");
        body.Should().Contain("AnatomicalTemplateValidation.TryValidate");
        Regex.IsMatch(body, @"return\s+false\s*;", RegexOptions.Multiline)
            .Should().BeTrue("None rig must fall back to extrusion-only path");
    }

    [Fact]
    public void AnatomicalTemplatePipeline_TryBuildColorizedTemplate_stub_returns_false_and_default()
    {
        var source = ReadSource(@"WorldSphereMod/Code/Voxel/AnatomicalTemplatePipeline.cs");
        var body = ExtractMethodBody(source,
            "public static bool TryBuildColorizedTemplate(");

        body.Should().Contain("colorized = default");
        body.Should().Contain("ShouldUseTemplate(rigType)");
        body.Should().Contain("AnatomicalTemplateRegistry.TryGetTemplate");
        body.Should().Contain("AnatomicalTemplateValidation.TryValidate");
        Regex.IsMatch(body, @"return\s+false\s*;", RegexOptions.Multiline)
            .Should().BeTrue("colorized build must stay off until sprite projection ships");
    }

    [Fact]
    public void VoxelMeshCache_and_RigCache_remain_extrusion_only_until_template_wiring()
    {
        var meshCache = ReadSource(@"WorldSphereMod/Code/Voxel/VoxelMeshCache.cs");
        var rigCache = ReadSource(@"WorldSphereMod/Code/Rig/RigCache.cs");

        meshCache.Should().NotContain("AnatomicalTemplatePipeline",
            "template path must not branch into mesh cache until runtime order §7 lands");
        meshCache.Should().Contain("SpriteVoxelizer.BuildPerTexel",
            "rigged actors must keep extrusion floor while template is stubbed");
        rigCache.Should().NotContain("AnatomicalTemplate",
            "rig cache must not reference template scaffold before integration");
    }
}
