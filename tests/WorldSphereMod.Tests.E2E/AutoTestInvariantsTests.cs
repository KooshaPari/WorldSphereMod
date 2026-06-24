using System.IO;
using FluentAssertions;
using Xunit;

[Trait("Category", "E2E")]
public class AutoTestInvariantsTests
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

    [Fact]
    public void AutoTestDriver_is_public_monobehaviour_with_phase_flags_array()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/AutoTest.cs");

        source.Should().Contain("public class AutoTestDriver : MonoBehaviour",
            "AutoTestDriver must be a public MonoBehaviour so Mod.PostInit can AddComponent it");
        source.Should().Contain("static readonly string[] PhaseFlags",
            "PhaseFlags must be a static readonly string array so the test cycle is immutable and discoverable");
    }

    [Fact]
    public void AutoTestDriver_phase_flags_declares_expected_sequence_and_excludes_skeletal_animation()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/AutoTest.cs");

        source.Should().Contain("nameof(SavedSettings.VoxelEntities)",
            "PhaseFlags must include the base voxel feature as the first step in the cycle");
        source.Should().Contain("nameof(SavedSettings.ProceduralBuildings)",
            "PhaseFlags must include procedural buildings in the test cycle");
        source.Should().Contain("nameof(SavedSettings.MeshWater)",
            "PhaseFlags must include mesh water in the test cycle");
        source.Should().Contain("nameof(SavedSettings.WorldspaceUI)",
            "PhaseFlags must include worldspace UI in the test cycle");
        source.Should().Contain("nameof(SavedSettings.DayNightCycle)",
            "PhaseFlags must include day/night cycle in the test cycle");
        source.Should().Contain("nameof(SavedSettings.PostFX)",
            "PhaseFlags must include post-FX in the test cycle");
        source.Should().Contain("nameof(SavedSettings.ParticleEffects)",
            "PhaseFlags must include particle effects in the test cycle");
        source.Should().NotContain("nameof(SavedSettings.SkeletalAnimation)",
            "PhaseFlags must intentionally exclude SkeletalAnimation because the rig bind offsets do not align with voxel mesh space");
    }

    [Fact]
    public void AutoTestDriver_start_coroutine_snapshots_restores_and_measures_peak_counters()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/AutoTest.cs");

        source.Should().Contain("IEnumerator Start()",
            "Start must be a coroutine so it can yield across load, warmup, and measurement phases");
        source.Should().Contain("var preTestState = new System.Collections.Generic.Dictionary<string, bool>();",
            "Start must snapshot pre-test flag values so the user's chosen settings are restored after the cycle");
        source.Should().Contain("foreach (string flagName in PhaseFlags)",
            "Start must iterate every declared phase flag in order");
        source.Should().Contain("SetPhase(field, flagName, true);",
            "Start must enable each phase flag before measuring its frame cost");
        source.Should().Contain("for (int tick = 0; tick < 180; tick++)",
            "Start must measure exactly 180 frames per phase to produce a consistent peak-draw-call sample");
        source.Should().Contain("MeshInstanceBatcher.FrameDrawCalls",
            "Start must record MeshInstanceBatcher.FrameDrawCalls during the measurement window");
        source.Should().Contain("MeshInstanceBatcher.FrameInstances",
            "Start must record MeshInstanceBatcher.FrameInstances during the measurement window");
        source.Should().Contain("SetPhase(field, flagName, false);",
            "Start must disable each phase flag after measurement so the next phase starts from a clean state");
        source.Should().Contain("foreach (var kv in preTestState)",
            "Start must restore every pre-test flag value after the full cycle completes");
    }

    [Fact]
    public void AutoTestDriver_setphase_uses_applyphasetoggle_and_does_not_persist_to_disk()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/AutoTest.cs");

        source.Should().Contain("Core.ApplyPhaseToggle(flagName, value);",
            "SetPhase must route through Core.ApplyPhaseToggle so render caches are invalidated when flags change");
        source.Should().Contain("// Core.SaveSettings();",
            "SetPhase must NOT persist AutoTest mutations to disk — otherwise the user's defaults would be overwritten");
    }

    [Fact]
    public void AutoTestDriver_load_latest_world_or_generate_falls_back_to_new_world()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/AutoTest.cs");

        source.Should().Contain("static void LoadLatestWorldOrGenerate()",
            "LoadLatestWorldOrGenerate must be a static helper so the coroutine can invoke it without instance state");
        source.Should().Contain("World.world.save_manager.loadWorld(path, false);",
            "LoadLatestWorldOrGenerate must attempt to load the latest save when one is found");
        source.Should().Contain("World.world.startTheGame(true);",
            "LoadLatestWorldOrGenerate must fall back to generating a new world when no save exists");
    }

    [Fact]
    public void AutoTestDriver_find_latest_save_path_scans_save_directories()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/AutoTest.cs");

        source.Should().Contain("Directory.GetDirectories(saves, \"save*\")",
            "FindLatestSavePath must enumerate directories matching save* under the saves folder");
        source.Should().Contain("GetSaveWriteTime(dir)",
            "FindLatestSavePath must compare write times so the most recently touched save is chosen");
        source.Should().Contain("ParseSlot(dir)",
            "FindLatestSavePath must parse the numeric slot from the directory name");
    }

    [Fact]
    public void AutoTestDriver_force_tilemap_refresh_uses_reflection_with_fallback_methods()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/AutoTest.cs");

        source.Should().Contain("typeof(WorldTilemap).GetMethod(\"rerenderEverything\"",
            "ForceTilemapRefresh must try rerenderEverything first to trigger a full tilemap rebuild");
        source.Should().Contain("typeof(WorldTilemap).GetMethod(\"refreshAll\"",
            "ForceTilemapRefresh must fall back to refreshAll if rerenderEverything is missing");
        source.Should().Contain("typeof(WorldTilemap).GetMethod(\"clearAndRedraw\"",
            "ForceTilemapRefresh must fall back to clearAndRedraw if both earlier methods are missing");
        source.Should().Contain("catch (Exception e)",
            "ForceTilemapRefresh must catch reflection/invoke failures so a missing method does not crash the cycle");
    }

    [Fact]
    public void AutoTestDriver_get_first_actor_pos_is_null_safe()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/AutoTest.cs");

        source.Should().Contain("static string GetFirstActorPos()",
            "GetFirstActorPos must be a static helper so it can be called from the coroutine without instance state");
        source.Should().Contain("try",
            "GetFirstActorPos must wrap the actor enumeration in try/catch because World.world.units may be null during scene transitions");
        source.Should().Contain("catch (Exception e)",
            "GetFirstActorPos must catch exceptions and return a diagnostic string instead of throwing");
        source.Should().Contain("return \"<none>\";",
            "GetFirstActorPos must return a well-known sentinel when no actors exist");
    }

    [Fact]
    public void AutoTest_gated_by_savedsettings_and_environment_variable_in_mod()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Mod.cs");

        source.Should().Contain("System.Environment.GetEnvironmentVariable(\"WSM3D_AUTOTEST\") == \"1\"",
            "IsAutoTest must be triggerable by the WSM3D_AUTOTEST environment variable for CI headless runs");
        source.Should().Contain("Core.savedSettings.AutoTest",
            "IsAutoTest must also be gated by the SavedSettings.AutoTest flag so users can opt in from the UI");
        source.Should().Contain("if (IsAutoTest && Object != null && Object.GetComponent<AutoTestDriver>() == null) Object.AddComponent<AutoTestDriver>();",
            "AutoTestDriver must only be added when IsAutoTest is true and the component is not already present");
    }

    [Fact]
    public void AutoScreenshotDriver_is_sealed_monobehaviour_gated_by_savedsettings()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/AutoScreenshotDriver.cs");

        source.Should().Contain("public sealed class AutoScreenshotDriver : MonoBehaviour",
            "AutoScreenshotDriver must be a sealed MonoBehaviour so it can be mounted on the mod GameObject");
        source.Should().Contain("Core.savedSettings.AutoScreenshotEnabled",
            "AutoScreenshotDriver must gate the capture loop on SavedSettings.AutoScreenshotEnabled");
        source.Should().Contain("Core.savedSettings.AutoScreenshotIntervalSeconds",
            "AutoScreenshotDriver must read the interval from SavedSettings.AutoScreenshotIntervalSeconds");
    }

    [Fact]
    public void ScreenshotCapture_exposes_public_static_entry_points_with_null_safe_callbacks()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/ScreenshotCapture.cs");

        source.Should().Contain("public static class ScreenshotCapture",
            "ScreenshotCapture must be a public static class so callers can start captures without instantiation");
        source.Should().Contain("public static string BuildDefaultPath()",
            "BuildDefaultPath must be a public static entry point for tests and callers that need a default filename");
        source.Should().Contain("public static IEnumerator CaptureCoroutine(string outputPath, Action<string, bool, string> completed)",
            "CaptureCoroutine must be a public static entry point so the driver can yield it as a coroutine");
        source.Should().Contain("public static IEnumerator CaptureCameraCoroutine(string outputPath, Action<string, bool, string> completed)",
            "CaptureCameraCoroutine must be a public static entry point for the offscreen camera capture path");
        source.Should().Contain("completed?.Invoke(",
            "ScreenshotCapture must null-safe the completed callback with ?.Invoke so callers that pass null do not crash");
        source.Should().Contain("string.IsNullOrWhiteSpace(outputPath)",
            "ScreenshotCapture must treat empty or whitespace paths as a signal to use the default path");
    }
}
