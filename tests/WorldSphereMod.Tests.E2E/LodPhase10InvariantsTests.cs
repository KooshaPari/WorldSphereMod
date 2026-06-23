using System;
using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// Closes Phase 10 E2E gaps: mid LOD tier selection, compute-shader hardware fallback,
/// impostor material fallback, culling, and perf-budget telemetry (e2e-coverage-gaps.md #4).
/// </summary>
public class LodPhase10InvariantsTests
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
    public void LodSelector_exposes_two_tier_voxel_or_cull_ladder_with_hysteresis()
    {
        var lod = ReadSourceFile("WorldSphereMod/Code/LOD/LodSelector.cs");

        // VOXEL-OR-INVISIBLE: the ladder is exactly two tiers — Voxel (near) or Cull (far).
        // The legacy Proxy/Impostor billboard tiers are removed; far = draw nothing.
        lod.Should().Contain("public enum LodTier { Voxel, Cull }",
            "two-tier ladder: near voxel or far cull, no billboard tier");
        lod.Should().NotContain("LodTier.Impostor",
            "the impostor billboard tier must be gone (voxel-or-invisible)");
        lod.Should().NotContain("LodTier.Proxy",
            "the proxy billboard tier must be gone (voxel-or-invisible)");

        var selectBody = ExtractMethodBody(lod, "public static LodTier Select(Vector3 worldPos, int instanceId)");
        selectBody.Should().Contain("LodTier.Voxel");
        selectBody.Should().Contain("LodTier.Cull",
            "far objects must select Cull, not an intermediate billboard");
        // Hysteresis debounce stabilizes the near/far flip so tiles do not oscillate
        // (the LOD flash-wave). _hystFrames == 3.
        selectBody.Should().Contain("h.pendingFrames >= _hystFrames",
            "tier changes must require hysteresis debounce");
        lod.Should().Contain("const int _hystFrames = 3",
            "hysteresis debounce holds a proposed tier for 3 frames before promotion");
    }

    [Fact]
    public void Mod_OnLoad_enables_cull_only_when_compute_indirect_unsupported()
    {
        var mod = ReadSourceFile("WorldSphereMod/Code/Mod.cs");

        mod.Should().Contain("!SystemInfo.supportsComputeShaders || !SystemInfo.supportsIndirectArgumentsBuffer");
        // ImpostorOnlyMode kept as the flag name for call-site compatibility, but it now
        // means "cull everything" (voxel-or-invisible) — there is no billboard fallback.
        mod.Should().Contain("LodSelector.ImpostorOnlyMode = true",
            "hardware without compute/indirect must force the cull-only compatibility path");
    }

    [Fact]
    public void VoxelFrameDriver_TickPerFrame_updates_frustum_planes_and_logs_perf_budget()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        var tickBody = ExtractMethodBody(voxelRender, "public static void TickPerFrame()");

        tickBody.Should().Contain("FrustumCuller.UpdatePlanes()",
            "LOD culling must refresh view frustum each frame before emit");
        tickBody.Should().Contain("kPerfSampleWindowFrames",
            "perf budget must sample frame delta over a fixed window");
        tickBody.Should().Contain("[WSM3D][Perf]",
            "budget regression tests rely on periodic perf log emission");
        // No ImpostorBillboard atlas to advance anymore — the billboard tier is removed.
        voxelRender.Should().NotContain("ImpostorBillboard",
            "the impostor billboard atlas must be gone (voxel-or-invisible)");
    }

    [Fact]
    public void VoxelRender_actor_emit_culls_at_far_tier_instead_of_billboarding()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        // Far tier = CULL, never a billboard. The actor emit must branch on the Cull tier
        // and draw nothing, after suppressing the vanilla 2D sprite up-front.
        voxelRender.Should().Contain("if (tier == WorldSphereMod.LOD.LodTier.Cull)",
            "far actors must be culled (draw nothing), not billboarded");
        voxelRender.Should().NotContain("ImpostorBillboard.GetOrCreate",
            "no impostor atlas billboard path may exist");
        voxelRender.Should().Contain("FrustumCuller.IsVisible(cullPos, radius)",
            "near/far LOD selection must run only for frustum-visible actors");
    }

    [Fact]
    public void Emit_paths_never_branch_on_a_billboard_tier()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        var buildingProc = ReadSourceFile("WorldSphereMod/Code/ProcGen/BuildingProcRender.cs");

        // Voxel-or-invisible: the only tiers are Voxel and Cull. No Proxy/Impostor branch.
        voxelRender.Should().NotContain("LodTier.Proxy",
            "actor/building/projectile emit must not branch on a Proxy billboard tier");
        voxelRender.Should().NotContain("LodTier.Impostor",
            "actor/building/projectile emit must not branch on an Impostor billboard tier");
        buildingProc.Should().NotContain("LodTier.Proxy",
            "procedural building emit must not branch on a Proxy billboard tier");
        buildingProc.Should().NotContain("LodTier.Impostor",
            "procedural building emit must not branch on an Impostor billboard tier");

        voxelRender.Should().Contain("Mesh m = VoxelMeshCache.Get(sp)",
            "the near (Voxel) tier shares the full voxel cache path");
    }
}
