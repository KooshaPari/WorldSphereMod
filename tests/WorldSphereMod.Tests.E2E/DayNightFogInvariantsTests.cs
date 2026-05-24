using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

public class DayNightFogInvariantsTests
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
    public void SavedSettings_FogDensity_defaults_to_0_05f()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/SavedSettings.cs");

        Regex.Match(source, @"public\s+float\s+FogDensity\s*=\s*0\.05f\s*;")
            .Success.Should().BeTrue("Phase 8 live default FogDensity must stay 0.05f");
    }

    [Fact]
    public void TimeOfDay_is_gated_by_DayNightCycle_phase_and_uploads_fog_each_Update()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/TimeOfDay.cs");

        Regex.Match(source, @"\[Phase\(nameof\(SavedSettings\.DayNightCycle\)\)\]")
            .Success.Should().BeTrue("TimeOfDay must honor the DayNightCycle phase gate");

        var updateBody = ExtractMethodBody(source, "void Update()");
        updateBody.Should().Contain("ApplyFog(Current)",
            "per-frame TimeOfDay update must drive fog");

        Regex.Match(source, @"Shader\.PropertyToID\(""(_WSM_FogDensity|_WSM_FogColor)""\)")
            .Success.Should().BeTrue("shader globals must be cached via PropertyToID");
    }

    [Fact]
    public void SunRig_FogColor_blends_horizon_and_ambient_for_atmospheric_tint()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/SunRig.cs");
        var methodBody = ExtractMethodBody(source, "public static Color FogColor(float t)");

        methodBody.Should().Contain("HorizonColor(t)");
        methodBody.Should().Contain("AmbientColor(t)");
        methodBody.Should().Contain("Color.Lerp(ambient, horizon, 0.6f)");

        Regex.Match(methodBody, @"Color\.Lerp\s*\(\s*ambient\s*,\s*horizon\s*,\s*0\.6f\s*\)")
            .Success.Should().BeTrue("FogColor must lerp ambient toward horizon at 60%");
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

        Regex.Match(methodBody, @"RenderSettings\.fogMode\s*=\s*FogMode\.ExponentialSquared")
            .Success.Should().BeTrue("built-in fog mode must match shader exp2 falloff");
        Regex.Match(methodBody, @"bool\s+fogOn\s*=\s*density\s*>\s*0f")
            .Success.Should().BeTrue("fog must disable when density is zero");
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

        Regex.Match(shader, @"TimeOfDay\.ApplyFog from SavedSettings\.FogDensity \+ SunRig\.FogColor")
            .Success.Should().BeTrue("shader must document the Phase 8 fog upload contract");
        Regex.Match(forwardHlsl, @"float\s+fogCoord\s*=\s*_WSM_FogDensity\s*\*\s*dist\s*;")
            .Success.Should().BeTrue("fogCoord must scale world-space distance by global density");
    }
}
