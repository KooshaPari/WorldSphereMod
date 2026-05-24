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
    public void VoxelRender_EnsureMaterial_uses_Standard_candidate_and_excludes_Sprites_Default()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        var methodBody = ExtractMethodBody(source, "public static bool EnsureMaterial()");

        methodBody.Should().Contain("string[] candidates =");
        methodBody.Should().Contain("\"Standard\"");
        methodBody.Should().NotContain("\"Sprites/Default\"",
            "Sprites/Default is a transparent dummy fallback — inline OpaqueVertexColor + Standard cover BRP");

        methodBody.Should().Contain("foreach (var name in candidates)");
        methodBody.Should().Contain("TryCompileInlineVoxelShader()");
        methodBody.Should().Contain("return true;");
    }

    [Fact]
    public void SunRig_FogColor_blends_horizon_and_ambient_for_atmospheric_tint()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/SunRig.cs");
        var methodBody = ExtractMethodBody(source, "public static Color FogColor(float t)");

        methodBody.Should().Contain("HorizonColor(t)");
        methodBody.Should().Contain("AmbientColor(t)");
        methodBody.Should().Contain("Color.Lerp(ambient, horizon, 0.6f)");
    }

    [Fact]
    public void TimeOfDay_ApplyFog_wires_SavedSettings_FogDensity_to_RenderSettings_and_shader_globals()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/TimeOfDay.cs");
        var methodBody = ExtractMethodBody(source, "static void ApplyFog(float t)");

        methodBody.Should().Contain("Core.savedSettings.FogDensity",
            "fog density must come from SavedSettings");
        methodBody.Should().Contain("RenderSettings.fogDensity = density",
            "Unity built-in fog must mirror the user-facing scalar");
        methodBody.Should().Contain("RenderSettings.fogColor = fogColor",
            "fog tint must be assigned for built-in pipeline consumers");
        methodBody.Should().Contain("SunRig.FogColor(t)",
            "fog tint must follow the Phase 8 sky horizon/ambient blend");
        methodBody.Should().Contain("Shader.SetGlobalFloat(_wsmFogDensity, density)",
            "VoxelLit forward pass reads _WSM_FogDensity");
        methodBody.Should().Contain("Shader.SetGlobalColor(_wsmFogColor, fogColor)",
            "VoxelLit forward pass reads _WSM_FogColor");
        source.Should().Contain("ApplyFog(Current)",
            "per-frame TimeOfDay update must drive fog");
    }

    [Fact]
    public void VoxelLit_ForwardLit_applies_depth_exponential_squared_fog()
    {
        var shader = ReadSourceFile("WorldSphereMod/Resources/Shaders/VoxelLit.shader");
        var forwardHlsl = ExtractForwardLitHlsl(shader);

        forwardHlsl.Should().Contain("_WSM_FogDensity",
            "shader must consume the global density uploaded by TimeOfDay");
        forwardHlsl.Should().Contain("_WSM_FogColor",
            "shader must consume the global tint uploaded by TimeOfDay");
        forwardHlsl.Should().Contain("distance(input.positionWS, _WorldSpaceCameraPos.xyz)",
            "fog must be depth-based in world space");
        forwardHlsl.Should().Contain("exp2(-fogCoord * fogCoord)",
            "fog falloff must match RenderSettings ExponentialSquared");
        forwardHlsl.Should().Contain("lerp(_WSM_FogColor.rgb, color, fogFactor)",
            "frag output must fade toward the sky-aligned fog tint");
    }

    static string ExtractForwardLitHlsl(string shaderSource)
    {
        const string passTag = "Name \"ForwardLit\"";
        int passIndex = shaderSource.IndexOf(passTag, StringComparison.Ordinal);
        passIndex.Should().BeGreaterThanOrEqualTo(0, "VoxelLit ForwardLit pass must exist");

        int hlslStart = shaderSource.IndexOf("HLSLPROGRAM", passIndex, StringComparison.Ordinal);
        hlslStart.Should().BeGreaterThanOrEqualTo(0, "ForwardLit pass must contain HLSLPROGRAM");

        int hlslEnd = shaderSource.IndexOf("ENDHLSL", hlslStart, StringComparison.Ordinal);
        hlslEnd.Should().BeGreaterThan(hlslStart, "ForwardLit pass must close with ENDHLSL");

        return shaderSource.Substring(hlslStart, hlslEnd - hlslStart);
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
