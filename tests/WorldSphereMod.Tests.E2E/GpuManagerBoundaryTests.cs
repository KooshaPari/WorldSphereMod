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
}
