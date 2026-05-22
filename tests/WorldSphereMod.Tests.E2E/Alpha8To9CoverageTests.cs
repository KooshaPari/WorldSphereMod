using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using FluentAssertions;

public class Alpha8To9CoverageTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("repo root with WorldSphereMod.sln must be locatable from test cwd");
        return dir!.FullName;
    }

    private static string ReadSourceFile(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        File.Exists(fullPath).Should().BeTrue($"source file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    private static string ExtractMethodBody(string source, string signature)
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
    public void WorldSphereTab_cs_PowerWindow_init_has_null_Object_and_layout_group_guards()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/WorldSphereTab.cs");
        var methodBody = ExtractMethodBody(source, "public void init(string id, GameObject content, List<ButtonData> Buttons)");

        Regex objectGuard = new Regex("if\\s*\\(\\s*Object\\s*==\\s*null\\s*\\)\\s*\\{[\\s\\S]*?return;\\s*\\}", RegexOptions.IgnoreCase);
        Regex layoutGuard = new Regex("if\\s*\\(\\s*layoutGroup\\s*==\\s*null\\s*\\)\\s*\\{[\\s\\S]*?return;\\s*\\}", RegexOptions.IgnoreCase);

        objectGuard.IsMatch(methodBody).Should().BeTrue("PowerWindow.init should return early when content GameObject is null/destroyed");
        layoutGuard.IsMatch(methodBody).Should().BeTrue("PowerWindow.init should return early when VerticalLayoutGroup component cannot be created");

        methodBody.Should().Contain("VerticalLayoutGroup layoutGroup = Object.AddComponent<VerticalLayoutGroup>()");
    }

    [Fact]
    public void DimensionConverter_TranspilerPosition_returns_original_instructions_when_no_matches()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/DimensionConverter.cs");
        var methodBody = ExtractMethodBody(source, "public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)");

        methodBody.Should().Contain("CodeMatcher Matcher = new CodeMatcher(instructions, generator);");
        methodBody.Should().Contain("while (Matcher.FindNext(");
        methodBody.Should().Contain("return Matcher.Instructions();");
        methodBody.Should().NotContain("return instructions;");

        // If this transpiler cannot match any position read/write opcodes, Matcher.Instructions() is\n        // the unchanged input sequence and is returned by the default control flow.
    }

    [Fact]
    public void VoxelRender_EnsureMaterial_includes_and_prefers_Standard_before_Sprites_Default()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        var methodBody = ExtractMethodBody(source, "public static bool EnsureMaterial()");

        methodBody.Should().Contain("string[] candidates =");
        methodBody.Should().Contain("\"Standard\"");
        methodBody.Should().Contain("\"Sprites/Default\"");

        int standardIndex = methodBody.IndexOf("\"Standard\"", StringComparison.Ordinal);
        int spritesDefaultIndex = methodBody.IndexOf("\"Sprites/Default\"", StringComparison.Ordinal);
        standardIndex.Should().BeLessThan(spritesDefaultIndex, "Standard should be attempted before fallback Sprites/Default");

        methodBody.Should().Contain("foreach (var name in candidates)");
        methodBody.Should().Contain("return true;");
    }

    [Fact]
    public void MeshInstanceBatcher_applies_Color_per_instance_in_material_property_block()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs");
        var methodBody = ExtractMethodBody(source, "static void DrawFallbackPath(Key key, Bucket bucket, int total, int layer, Camera renderCamera, ShadowCastingMode shadows, bool receive, int start = 0)");

        source.Should().Contain("static readonly int _colorPropUnlit = Shader.PropertyToID(\"_Color\");");
        methodBody.Should().Contain("Vector4 tint = bucket.Colors[i];");
        methodBody.Should().Contain("bucket.Block.SetColor(_colorPropUnlit, tint);");
    }
}
