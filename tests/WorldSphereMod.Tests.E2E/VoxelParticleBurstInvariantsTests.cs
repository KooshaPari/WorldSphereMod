using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using FluentAssertions;

public class VoxelParticleBurstInvariantsTests
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
    public void VoxelParticleBurst_uses_VoxelMeshCache_and_spawn_grow_fade_envelope()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Fx/VoxelParticleBurst.cs");

        source.Should().Contain("VoxelMeshCache.Get(sprite)",
            "burst path must resolve voxel mesh from the effect sprite");
        source.Should().Contain("MeshInstanceBatcher.Submit(state.Mesh, state.Material, trs, tint)",
            "burst must submit cached mesh each frame");

        var tryStart = ExtractMethodBody(source, "public static bool TryStart(BaseEffect effect)");
        tryStart.Should().Contain("SuppressSprite(effect, out bool spriteWasEnabled)",
            "successful start must hide the upstream billboard");

        var update = ExtractMethodBody(source, "public static void Update(BaseEffect effect)");
        update.Should().Contain("Mathf.SmoothStep(0.12f, 1f, growT)",
            "growth phase must ramp scale from near-zero");
        update.Should().Contain("tint.a *= alpha",
            "fade phase must reduce burst alpha");
        update.Should().Contain("RestoreSprite(effect, state)",
            "TTL end must restore suppressed sprites");
    }

    [Theory]
    [InlineData("fx_meteorite")]
    [InlineData("fx_explosion_wave")]
    [InlineData("fx_fire_smoke")]
    [InlineData("fx_cloud")]
    public void VoxelParticleBurst_profiles_include_phase9b_effect_ids(string effectId)
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Fx/VoxelParticleBurst.cs");
        source.Should().Contain($"[\"{effectId}\"]",
            $"Phase 9b spec effect {effectId} must have a burst profile");
    }

    [Fact]
    public void Effects_cs_routes_spec_hooks_through_VoxelParticleBurst()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Effects.cs");

        ExtractMethodBody(source, "public static void MeteoritePatch(Meteorite __instance)")
            .Should().Contain("BasePatch(__instance)",
                "meteorite spawn must enter the 3D effect path that calls TryStart");

        ExtractMethodBody(source, "public static void ExplosionPatch(ExplosionFlash __instance)")
            .Should().Contain("BasePatch(__instance)",
                "explosion flash start must enter the 3D effect path that calls TryStart");

        var statusParticle = ExtractMethodBody(source,
            "public static bool FixParticle(StatusParticle __instance, Vector3 pVector, Color pColor, float pScale)");
        statusParticle.Should().Contain("SetEffect3D(__instance, GetData(__instance))",
            "status particles must route through SetEffect3D / TryStart instead of vanilla sprite-only setup");

        source.Should().Contain("VoxelParticleBurst.TryStart(__result)",
            "GetObject postfix must start bursts when ParticleEffects is enabled");
        source.Should().Contain("VoxelParticleBurst.Update(Effect)",
            "per-frame update must drive the burst envelope");
    }

    [Fact]
    public void Effects_Shadow3D_skips_sprite_shadow_while_voxel_burst_is_active()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Effects.cs");
        var shadowBody = ExtractMethodBody(source, "public static bool Shadow3D(SpriteShadow __instance)");

        shadowBody.Should().Contain("VoxelParticleBurst.IsActive(burstEffect)",
            "Phase 5 shadow path must opt out when the billboard is hidden for a voxel burst");
    }

    [Fact]
    public void EffectPatches9_clears_voxel_burst_state_on_world_finish()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Fx/EffectPatches9.cs");
        var onFinish = ExtractMethodBody(source, "public static void OnFinish()");

        onFinish.Should().Contain("VoxelParticleBurst.Clear()",
            "world unload must drop transient burst state");
    }
}
