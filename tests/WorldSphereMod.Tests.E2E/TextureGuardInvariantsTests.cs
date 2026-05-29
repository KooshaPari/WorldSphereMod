using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Invariant: texture reads/writes against sky materials must be HasProperty-guarded.
/// Different skybox shaders (vanilla 6-sided, cubemap, procedural) expose different
/// texture slots — _MainTex, _Tex, _Cube, _Cubemap, _SkyCubemap. Touching
/// material.mainTexture or material.GetTexture("_MainTex"/"_Tex") on a material that
/// lacks the slot logs a Unity error every frame (and can return null/garbage). Every
/// such access in the sky/water material code must sit behind a HasProperty check.
/// </summary>
public sealed class TextureGuardInvariantsTests
{
    static readonly string[] SkyMaterialSources =
    {
        "WorldSphereMod/Code/Lighting/ProceduralSky.cs",
        "WorldSphereMod/Code/Water/WaterSurface.cs",
    };

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

    static string[] ReadLines(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(path).Should().BeTrue($"source file must exist at {path}");
        return File.ReadAllLines(path);
    }

    // Returns true if a HasProperty guard for `prop` appears within `lookback`
    // lines preceding (and including) the access line. Sky material accesses are
    // gated by an `if (mat.HasProperty("...")) { ... access ... }` a few lines up.
    static bool GuardedNearby(string[] lines, int accessLineIndex, string prop, int lookback = 12)
    {
        int start = Math.Max(0, accessLineIndex - lookback);
        var guard = new Regex(@"HasProperty\(\s*""" + Regex.Escape(prop) + @"""\s*\)");
        for (int i = start; i <= accessLineIndex; i++)
        {
            if (guard.IsMatch(lines[i])) return true;
        }
        return false;
    }

    [Fact]
    public void mainTexture_accesses_are_HasProperty_guarded()
    {
        foreach (var rel in SkyMaterialSources)
        {
            var lines = ReadLines(rel);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(".mainTexture")) continue;

                GuardedNearby(lines, i, "_MainTex").Should().BeTrue(
                    $"{rel} line {i + 1}: a .mainTexture access must be guarded by HasProperty(\"_MainTex\")");
            }
        }
    }

    [Fact]
    public void GetTexture_MainTex_and_Tex_accesses_are_HasProperty_guarded()
    {
        var pairs = new (string prop, Regex access)[]
        {
            ("_MainTex", new Regex(@"GetTexture\(\s*""_MainTex""\s*\)")),
            ("_Tex", new Regex(@"GetTexture\(\s*""_Tex""\s*\)")),
        };

        foreach (var rel in SkyMaterialSources)
        {
            var lines = ReadLines(rel);
            foreach (var (prop, access) in pairs)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!access.IsMatch(lines[i])) continue;

                    GuardedNearby(lines, i, prop).Should().BeTrue(
                        $"{rel} line {i + 1}: GetTexture(\"{prop}\") must be guarded by HasProperty(\"{prop}\")");
                }
            }
        }
    }

    [Fact]
    public void At_least_one_guarded_sky_texture_access_exists()
    {
        // Sanity: confirm the tests above are actually exercising real accesses,
        // not vacuously passing because the patterns never appear.
        int accesses = 0;
        var probes = new[]
        {
            new Regex(@"\.mainTexture"),
            new Regex(@"GetTexture\(\s*""_MainTex""\s*\)"),
            new Regex(@"GetTexture\(\s*""_Tex""\s*\)"),
        };

        foreach (var rel in SkyMaterialSources)
        {
            foreach (var line in ReadLines(rel))
            {
                foreach (var probe in probes)
                {
                    if (probe.IsMatch(line)) accesses++;
                }
            }
        }

        accesses.Should().BeGreaterThan(0,
            "the guard tests must be checking real sky-material texture accesses, not passing vacuously");
    }
}
