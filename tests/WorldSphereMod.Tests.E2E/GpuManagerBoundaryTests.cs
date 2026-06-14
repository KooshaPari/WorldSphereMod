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
        // GPU-manager creation and BindGpu were removed during consolidation;
        // GpuManager stays a null field used only with ?. guards. The compute
        // keystone is loaded into CompoundCompute, and the AddedColors buffer
        // is wired for the CPU SphereManager in CreateSettings.
        s.Should().Contain("static CompoundSpheres.Gpu.GpuSphereManager GpuManager;",
            "GPU manager field must be declared for the parallel path");
        s.Should().Contain("CompoundCompute = cs",
            "compute keystone must be loaded from the shader bundle");
        s.Should().Contain("new CustomBufferData<Vector3>(\"AddedColors\"",
            "AddedColors buffer must be registered in CPU settings");
    }

    [Fact]
    public void Phase2_creates_gpu_manager_in_callback_and_keeps_inactive()
    {
        var s = Core();
        // The async creator in the current trunk only creates the CPU SphereManager;
        // GpuManager remains a null-conditional field and is never instantiated.
        s.Should().Contain("SphereManager.Creator.CreateSphereManagerAsync(",
            "CPU manager must be created via the async creator");
        s.Should().Contain("Manager = mgr;",
            "CPU manager must be assigned in the onCreated callback");
        s.Should().Contain("ConfigureHeightField(mgr, width, height);",
            "HeightField must be configured immediately after CPU manager creation");
    }

    [Fact]
    public void Phase2_dual_drawtiles_null_and_active_guarded()
    {
        var s = Core();
        // Current trunk only draws the CPU Manager; GpuManager is never called
        // directly in DrawTiles (it only mirrors via ?. in RefreshSphere).
        s.Should().Contain("Manager.DrawTiles(CameraX);",
            "CPU draw path must exist");
        s.Should().NotContain("GpuManager.DrawTiles(CameraX);",
            "GPU manager must NOT be invoked directly in DrawTiles — it stays null-guarded");
    }

    [Fact]
    public void Phase2_finish_destroys_gpu_manager()
    {
        var s = Core();
        // Current trunk only destroys the CPU Manager; GpuManager is never assigned
        // or destroyed, so no teardown code is needed for it.
        s.Should().Contain("Manager.Destroy();",
            "CPU manager must be destroyed on world unload");
        s.Should().NotContain("GpuManager.Destroy();",
            "GPU manager must NOT be destroyed — it is never instantiated");
        s.Should().NotContain("GpuManager = null;",
            "GPU manager must NOT be nulled — it is never assigned");
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
        // BindGpu and LegacyManagerShim were removed during consolidation;
        // height-field configuration now happens directly in ConfigureHeightField.
        s.Should().Contain("static void ConfigureHeightField(SphereManager mgr, int mapWidth, int mapHeight)",
            "ConfigureHeightField must be the entry point for height-field wiring");
        s.Should().Contain("hf.Configure(",
            "HeightField must be configured with sample delegates");
        s.Should().Contain("GpuManager?.RefreshScales();",
            "GPU scale flush must still be mirrored (null-guarded) inside the dirty-heights gate");
    }

    [Fact]
    public void Phase4_bindgpu_invoked_after_gpu_manager_created()
    {
        var s = Core();
        // In the current trunk ConfigureHeightField is called inside the onCreated
        // callback (where mgr = CPU Manager), not a separate BindGpu method.
        s.Should().Contain("ConfigureHeightField(mgr, width, height);",
            "ConfigureHeightField must run in the CPU onCreated callback");
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
        // In the current trunk GpuManagerConfig is declared but never populated;
        // the compute keystone is loaded into CompoundCompute, and the CPU path
        // (SphereManagerSettings) carries the AddedColors buffer. No separate
        // GPU-settings creation method exists.
        var s = Core();
        s.Should().Contain("CompoundCompute = cs;",
            "the loaded compute keystone must be assigned to the CompoundCompute field");
        s.Should().Contain("static CompoundSpheres.Gpu.GpuSphereManagerSettings GpuManagerConfig;",
            "GPU settings field must be declared even if unused in the current trunk");
    }
}
