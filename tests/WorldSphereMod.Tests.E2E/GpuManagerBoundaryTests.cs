using System.IO;
using FluentAssertions;
using Xunit;

// Issue #199 GPU-compute go-live: per-phase source-contract tests for the parallel
// GpuSphereManager wiring in Core.cs / CompoundSphereScripts.cs. These follow the
// repo's established source-invariant idiom (the mod assembly cannot be loaded in a
// net8 test host because it links UnityEngine.dll), asserting the wiring seams the
// blueprint specifies are present and correctly guarded.
public class GpuManagerBoundaryTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
            dir = dir.Parent;
        dir.Should().NotBeNull("repo root with WorldSphereMod.sln must be locatable");
        return dir!.FullName;
    }

    private static string Read(string rel)
    {
        var full = Path.Combine(FindRepoRoot(), rel);
        File.Exists(full).Should().BeTrue($"source file must exist at {full}");
        return File.ReadAllText(full);
    }

    private static string Core() => Read("WorldSphereMod/Code/Core.cs");
    private static string Scripts() => Read("WorldSphereMod/Code/CompoundSphereScripts.cs");

    // ---- Phase 2 ----
    [Fact]
    public void Phase2_declares_parallel_gpu_manager_field()
    {
        Core().Should().Contain("static CompoundSpheres.Gpu.GpuSphereManager GpuManager;",
            "the GPU manager must run in parallel with the CPU Manager");
    }

    [Fact]
    public void Phase2_create_gpu_settings_guards_null_compute()
    {
        var s = Core();
        s.Should().Contain("static bool CreateGpuSettings()");
        s.Should().Contain("if (CompoundCompute == null)",
            "GPU creation must be skipped (no NRE in Init) when the compute keystone is missing");
        // AddedColors custom buffer wired here FIRST (risk #3) before any RefreshCustom mirror.
        s.Should().Contain("new CompoundSpheres.Gpu.CustomBufferData<Vector3>(\"AddedColors\"",
            "AddedColors buffer must be registered in settings to avoid KeyNotFoundException");
    }

    [Fact]
    public void Phase2_creates_gpu_manager_in_callback_and_keeps_inactive()
    {
        var s = Core();
        s.Should().Contain("CompoundSpheres.Gpu.GpuSphereManager.Creator.CreateSphereManagerAsync(",
            "GPU manager must be created via the async creator added in Phase 1");
        s.Should().Contain("if (CreateGpuSettings())",
            "GPU creation must be gated on settings construction (skips when compute null)");
        // Risk #2 mitigation (a): inactive until Phase 4 height sync.
        s.Should().Contain("gpuMgr.gameObject.SetActive(false)",
            "GPU tile layer must stay inactive until Phase 4 to avoid z-fighting the HeightField");
    }

    [Fact]
    public void Phase2_dual_drawtiles_null_and_active_guarded()
    {
        var s = Core();
        s.Should().Contain("Manager.DrawTiles(CameraX);");
        s.Should().Contain("GpuManager.DrawTiles(CameraX);");
        s.Should().Contain("GpuManager.gameObject.activeSelf",
            "GPU draw must be gated on the layer being active (inactive until Phase 4)");
    }

    [Fact]
    public void Phase2_finish_destroys_gpu_manager()
    {
        var s = Core();
        s.Should().Contain("GpuManager.Destroy();");
        s.Should().Contain("GpuManager = null;");
    }

    [Fact]
    public void Phase2_gpu_adapters_exist_and_are_index_based_for_added_colors()
    {
        var s = Scripts();
        s.Should().Contain("public static int GpuTileTexture(GpuSphereTile t)");
        s.Should().Contain("public static Vector3 GpuTileScaleForCurrentShape(GpuSphereTile t)");
        s.Should().Contain("public static Vector3 GpuTileAddedColor(int slot)",
            "GPU custom-buffer samplers are index-based (GetCustomData<T>(int Index))");
        s.Should().Contain("public static void GpuCameraRange(GpuSphereManager mgr, out CompoundSpheres.Gpu.Range Rows, out CompoundSpheres.Gpu.Range Cols)");
    }

    // ---- Phase 3 ----
    [Fact]
    public void Phase3_refreshsphere_mirrors_textures_and_added_to_gpu()
    {
        var s = Core();
        s.Should().Contain("GpuManager?.RefreshTextures();");
        s.Should().Contain("GpuManager?.RefreshCustom(\"AddedColors\");");
    }

    [Fact]
    public void Phase3_gpu_refreshscales_stays_inside_dirty_heights_gate()
    {
        // Risk #5: the GPU scale flush must live INSIDE the hadDirtyHeights block,
        // not run unconditionally every frame (would re-break the rebuild storm).
        var s = Core();
        var gateIdx = s.IndexOf("if (hadDirtyHeights && Manager.UseHeightFieldTerrain)", System.StringComparison.Ordinal);
        gateIdx.Should().BeGreaterThan(0);
        var gpuScaleIdx = s.IndexOf("GpuManager?.RefreshScales();", System.StringComparison.Ordinal);
        gpuScaleIdx.Should().BeGreaterThan(gateIdx, "GPU RefreshScales must appear after (inside) the hadDirtyHeights gate");
        var markDirtyIdx = s.IndexOf("Manager.HeightField.MarkDirty();", System.StringComparison.Ordinal);
        gpuScaleIdx.Should().BeLessThan(markDirtyIdx, "GPU RefreshScales must be within the same gated block");
    }

    [Fact]
    public void Phase3_refreshcolors_and_updates_mirrored_to_gpu()
    {
        var s = Core();
        s.Should().Contain("GpuManager?.RefreshColors();");
        s.Should().Contain("GpuManager?.UpdateCustom(\"AddedColors\", (Tile.X * Height) + Tile.Y);");
        s.Should().Contain("GpuManager?.UpdateColor(Tile.X, Tile.Y);");
    }

    // ---- Phase 4 ----
    [Fact]
    public void Phase4_bindgpu_pushes_heights_and_reactivates_layer()
    {
        var s = Core();
        s.Should().Contain("static void BindGpu(int mapWidth, int mapHeight)");
        s.Should().Contain("var shim = new CompoundSpheres.Compat.LegacyManagerShim(GpuManager);",
            "shim must be height-only (no color delegate) to avoid the O(N) color scan (risk #6)");
        s.Should().Contain("hf.BindGpu(shim);");
        s.Should().Contain("GpuManager.gameObject.SetActive(true);",
            "GPU tile layer must be re-activated once heights are synced");
    }

    [Fact]
    public void Phase4_bindgpu_invoked_after_gpu_manager_created()
    {
        var s = Core();
        s.Should().Contain("BindGpu(width, height);",
            "BindGpu must run in the GPU onCreated callback (both Manager.HeightField and GpuManager exist there)");
    }

    [Fact]
    public void Phase4_shim_is_height_only_no_color_arg()
    {
        // The shim ctor accepts optional color/height delegates; passing a color
        // delegate triggers an O(N)/frame full re-scan (risk #6). We must construct
        // it with only the GpuSphereManager (height pushed via BindGpu->SetHeights).
        var s = Core();
        s.Should().NotContain("new CompoundSpheres.Compat.LegacyManagerShim(GpuManager,",
            "no extra delegate args — height-only shim");
    }

    // ---- Phase 5 ----
    [Fact]
    public void Phase5_compound_compute_loaded_from_shader_bundle()
    {
        // 5.1: CompoundCompute is populated from the wsm3d-shaders bundle in LoadAssets.
        var s = Core();
        s.Should().Contain("CompoundCompute = cs;",
            "the GPU-compute keystone must be loaded from the shader bundle");
        s.Should().Contain("GetObject<UnityEngine.ComputeShader>(",
            "loaded via GetObject<ComputeShader> on the .compute asset path");
    }

    [Fact]
    public void Phase5_create_gpu_settings_passes_compute_and_skips_when_null()
    {
        // 5.2: CreateGpuSettings passes CompoundCompute as ComputeShader, and
        // returns false (skips GPU creation + logs) when it is null — no crash.
        var s = Core();
        s.Should().Contain("ComputeShader = CompoundCompute,",
            "the loaded compute keystone must be passed as the GPU settings ComputeShader");
        s.Should().Contain("MatrixKernel = CompoundSpheres.Gpu.GpuKernels.Matrix,");
        s.Should().Contain("ColorKernel = CompoundSpheres.Gpu.GpuKernels.Color,");
        // Guard: null compute => skip + warn (no NRE in ManagerBase.Init FindKernel).
        var guardIdx = s.IndexOf("if (CompoundCompute == null)", System.StringComparison.Ordinal);
        guardIdx.Should().BeGreaterThan(0);
        var returnFalse = s.IndexOf("GpuManagerConfig = null;\r\n                    return false;",
            System.StringComparison.Ordinal);
        if (returnFalse < 0)
            returnFalse = s.IndexOf("GpuManagerConfig = null;\n                    return false;",
                System.StringComparison.Ordinal);
        returnFalse.Should().BeGreaterThan(guardIdx, "the null guard must skip creation by returning false");
    }
}
