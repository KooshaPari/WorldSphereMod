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
    public void LodSelector_exposes_Voxel_Proxy_Impostor_mid_tier_with_hysteresis()
    {
        var lod = ReadSourceFile("WorldSphereMod/Code/LOD/LodSelector.cs");

        lod.Should().Contain("public enum LodTier { Voxel, Proxy, Impostor }",
            "three-tier ladder must include mid Proxy LOD");
        lod.Should().Contain("public static float ProxyThreshold = 0.020f",
            "Proxy boundary must be tunable separately from Voxel threshold");

        var selectBody = ExtractMethodBody(lod, "public static LodTier Select(Vector3 worldPos, int instanceId)");
        selectBody.Should().Contain("proposed = LodTier.Voxel");
        selectBody.Should().Contain("proposed = LodTier.Proxy",
            "screen-size math must propose Proxy between voxel and impostor distances");
        selectBody.Should().Contain("proposed = LodTier.Impostor");
        selectBody.Should().Contain("if (h.pendingFrames >= 3)",
            "tier changes must require hysteresis debounce");
    }

    [Fact]
    public void Mod_OnLoad_enables_impostor_only_when_compute_indirect_unsupported()
    {
        var mod = ReadSourceFile("WorldSphereMod/Code/Mod.cs");

        mod.Should().Contain("!SystemInfo.supportsComputeShaders || !SystemInfo.supportsIndirectArgumentsBuffer");
        mod.Should().Contain("LodSelector.ImpostorOnlyMode = true",
            "hardware without compute/indirect must force impostor-only compatibility path");
    }

    [Fact]
    public void ImpostorBillboard_resolves_bundled_shader_then_URP_and_Standard_fallbacks()
    {
        var impostor = ReadSourceFile("WorldSphereMod/Code/LOD/ImpostorBillboard.cs");
        var getMatBody = ExtractMethodBody(impostor, "public static Material? GetMaterial()");

        getMatBody.Should().Contain("Core.Sphere.LoadedShaders.TryGetValue(\"Impostor\"",
            "bundled impostor shader must be preferred");
        getMatBody.Should().Contain("Shader.Find(\"WSM3D/Impostor\")");
        getMatBody.Should().Contain("Impostor material fallback shader resolved via Shader.Find",
            "URP/Standard chain must exist when bundled shader is missing");
        getMatBody.Should().Contain("MeshInstanceBatcher.UseFallbackPath",
            "instancing fallback must disable enableInstancing on impostor material");
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
        tickBody.Should().Contain("ImpostorBillboard.Tick()",
            "impostor atlas LRU must advance each voxel frame");
    }

    [Fact]
    public void VoxelRender_actor_emit_branches_on_Impostor_tier_before_full_voxel_mesh()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        voxelRender.Should().Contain("if (tier == WorldSphereMod.LOD.LodTier.Impostor)",
            "far actors must submit impostor billboards instead of full voxel meshes");
        voxelRender.Should().Contain("ImpostorBillboard.GetOrCreate(sp)",
            "impostor path must use atlas cache");
        voxelRender.Should().Contain("FrustumCuller.IsVisible(cullPos, radius)",
            "near/far LOD selection must run only for frustum-visible actors");
    }

    [Fact]
    public void Proxy_tier_emit_uses_full_voxel_path_until_BuildProxy_ships()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        var buildingProc = ReadSourceFile("WorldSphereMod/Code/ProcGen/BuildingProcRender.cs");
        var voxelizer = ReadSourceFile("WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs");
        var proxyCache = ReadSourceFile("WorldSphereMod/Code/Voxel/ProxyMeshCache.cs");

        voxelRender.Should().NotContain("tier == WorldSphereMod.LOD.LodTier.Proxy",
            "actor/building/projectile emit must not branch on Proxy until mid-tier meshes exist");
        buildingProc.Should().NotContain("tier == WorldSphereMod.LOD.LodTier.Proxy",
            "procedural building emit must not branch on Proxy until mid-tier meshes exist");

        voxelizer.Should().Contain("public static Mesh BuildProxy(Sprite sprite)",
            "Phase 10 proxy entry point must exist as a documented deferral stub");
        voxelizer.Should().Contain("phase10-proxy-tier-status.md",
            "BuildProxy stub must document deferral to status doc");
        proxyCache.Should().Contain("public static class ProxyMeshCache",
            "proxy mesh cache stub must exist for future mid-tier wiring");
        proxyCache.Should().Contain("return null",
            "ProxyMeshCache.Get must defer until BuildProxy and emit dispatch ship");

        voxelRender.Should().Contain("LodTier.Proxy (and Voxel) share full voxel path",
            "emit fallthrough must document that Proxy still uses VoxelMeshCache");
        voxelRender.Should().Contain("Mesh m = VoxelMeshCache.Get(sp, -1, true)",
            "non-impostor tiers (Voxel and Proxy) must share the full voxel cache path");
    }
}
