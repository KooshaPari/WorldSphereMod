using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

public class DayNightSmoothInvariantsTests
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
    public void TimeOfDay_Update_smoothly_tracks_world_time_without_hard_snaps()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/TimeOfDay.cs");
        var updateBody = ExtractMethodBody(source, "void Update()");

        updateBody.Should().Contain("Mathf.LerpAngle",
            "world_time catch-up must interpolate around the cycle, not snap");
        updateBody.Should().Contain("_worldTimeLerpSpeed",
            "exponential smoothing must gate how fast Current follows world_time");
        updateBody.Should().Contain("Mathf.Repeat(Current + Time.deltaTime * _worldTimeRate, 1f)",
            "autonomous advance should continue between world_time samples");

        var worldTimeBranch = ExtractMethodBody(updateBody, "if (boxed is float wt)");
        worldTimeBranch.Should().Contain("if (!_seededFromWorldTime)",
            "only the first world_time read may seed Current directly");
        Regex.Matches(worldTimeBranch, @"Current\s*=\s*worldTime").Count.Should().Be(1,
            "runtime must not reassign Current to world_time every frame (stepped preview behavior)");
    }

    [Fact]
    public void TimeOfDay_publishes_scalar_phase_to_SunDriver_each_frame()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/TimeOfDay.cs");
        var updateBody = ExtractMethodBody(source, "void Update()");

        updateBody.Should().Contain("SunDriver.TimeOfDay = Current * 24f");
        updateBody.Should().Contain("ApplyFog(Current)");
        updateBody.Should().Contain("RaiseTimeOfDay(Current)");
    }

    [Fact]
    public void SunRig_uses_piecewise_Color_Lerp_not_stepped_keyframe_snaps()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/SunRig.cs");

        source.Should().Contain("SampleSkyCurve(t,");
        source.Should().Contain("Color.Lerp(night, dawn, t / 0.25f)");
        source.Should().NotContain("switch (t)",
            "day phase must stay scalar-driven; no discrete fixture ladder in SunRig");
    }

    [Fact]
    public void ProceduralSky_samples_TimeOfDay_Current_every_LateUpdate()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Lighting/ProceduralSky.cs");
        var lateUpdateBody = ExtractMethodBody(source, "void LateUpdate()");

        lateUpdateBody.Should().Contain("Apply(TimeOfDay.Current)",
            "skybox colors must follow the continuous clock each frame");
    }

    [Fact]
    public void HighShadows_apply_does_not_touch_time_of_day_or_sun_rig_curves()
    {
        var shadowSource = ReadSourceFile("WorldSphereMod/Code/Lighting/ShadowCascadeConfig.cs");
        var sunDriverSource = ReadSourceFile("WorldSphereMod/Code/Lighting/SunDriver.cs");

        shadowSource.Should().NotContain("TimeOfDay");
        shadowSource.Should().NotContain("SunRig");
        shadowSource.Should().NotContain("ProceduralSky");

        var applyShadowBody = ExtractMethodBody(sunDriverSource, "public static void ApplyShadowSettings()");
        applyShadowBody.Should().NotContain("TimeOfDay");
        applyShadowBody.Should().NotContain("SunRig.Drive");
        applyShadowBody.Should().NotContain("Current");
    }
}
