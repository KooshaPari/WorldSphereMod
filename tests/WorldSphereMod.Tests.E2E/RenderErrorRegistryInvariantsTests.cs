using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Invariant tests for the render-error registry and visual marker system (FR-WSM-016).
///
/// RenderErrorRegistry is the telemetry hub: all render paths report failures via
/// Record(); the registry deduplicates by type, accumulates counts, and provides
/// snapshots for the /diag/errors bridge. RenderErrorMarkers is the visual sink.
/// These source-level checks ensure the enum, keys, and reset semantics remain stable.
/// </summary>
[Trait("Category", "E2E")]
public class RenderErrorRegistryInvariantsTests
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

    // ------------------------------------------------------------------
    // 1. RenderErrorType enum is stable and covers all wired failure paths
    // ------------------------------------------------------------------
    [Fact]
    public void RenderErrorType_enum_has_expected_values_in_order()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/RenderErrorRegistry.cs");

        source.Should().Contain("ShaderFailed = 0", "ShaderFailed must be 0 (magenta)");
        source.Should().Contain("MeshBuildFailed = 1", "MeshBuildFailed must be 1 (red)");
        source.Should().Contain("VoxelNotReady = 2", "VoxelNotReady must be 2 (yellow)");
        source.Should().Contain("MaterialNull = 3", "MaterialNull must be 3 (orange)");
        source.Should().Contain("Unsupported = 4", "Unsupported must be 4 (cyan)");
        source.Should().Contain("SpriteNull = 5", "SpriteNull must be 5 (grey)");
    }

    [Fact]
    public void RenderErrorMarkers_ColorFor_maps_all_enum_values()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/RenderErrorMarkers.cs");

        source.Should().Contain("case RenderErrorType.ShaderFailed:", "marker color must map ShaderFailed");
        source.Should().Contain("case RenderErrorType.MeshBuildFailed:", "marker color must map MeshBuildFailed");
        source.Should().Contain("case RenderErrorType.VoxelNotReady:", "marker color must map VoxelNotReady");
        source.Should().Contain("case RenderErrorType.MaterialNull:", "marker color must map MaterialNull");
        source.Should().Contain("case RenderErrorType.Unsupported:", "marker color must map Unsupported");
        source.Should().Contain("case RenderErrorType.SpriteNull:", "marker color must map SpriteNull");
    }

    // ------------------------------------------------------------------
    // 2. Registry records and deduplicates by type
    // ------------------------------------------------------------------
    [Fact]
    public void RenderErrorRegistry_Record_is_public_static_and_telemetry_always_on()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/RenderErrorRegistry.cs");

        source.Should().Contain("public static void Record(RenderErrorType type, string objectName, string reason, Vector3 worldPos)",
            "Record must be public static with the canonical 4-parameter signature");

        source.Should().Contain("ALWAYS records telemetry",
            "doc comment must promise unconditional telemetry (only visual prop is gated)");
    }

    [Fact]
    public void RenderErrorRegistry_Record_dedups_by_type_and_caps_examples()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/RenderErrorRegistry.cs");

        source.Should().Contain("if (!_entries.TryGetValue(type, out Entry e))",
            "Record must use TryGetValue to deduplicate by type (one Entry per RenderErrorType)");
        source.Should().Contain("e.count++", "Record must increment the per-type counter");
        source.Should().Contain("if (e.examples.Count < MaxExamplesPerType)",
            "Record must cap examples to MaxExamplesPerType to avoid unbounded memory growth");
    }

    [Fact]
    public void RenderErrorRegistry_CountOf_returns_0_for_never_recorded_type()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/RenderErrorRegistry.cs");

        source.Should().Contain("public static long CountOf(RenderErrorType type)",
            "CountOf must be public static");
        source.Should().Contain("return _entries.TryGetValue(type, out Entry e) ? e.count : 0L;",
            "CountOf must return 0L when a type has never been recorded");
    }

    [Fact]
    public void RenderErrorRegistry_TotalCount_sums_all_types()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/RenderErrorRegistry.cs");

        source.Should().Contain("public static long TotalCount()",
            "TotalCount must be public static");
        source.Should().Contain("foreach (var kv in _entries) total += kv.Value.count;",
            "TotalCount must sum every Entry.count across all types");
    }

    [Fact]
    public void RenderErrorRegistry_Snapshot_returns_copy_not_mutable_reference()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/RenderErrorRegistry.cs");

        source.Should().Contain("public static List<TypeReport> Snapshot()",
            "Snapshot must be public static");
        source.Should().Contain("report.examples.Add(new Example { name = src.name, reason = src.reason, x = src.x, y = src.y, z = src.z })",
            "Snapshot must deep-copy examples so callers cannot mutate registry state");
    }

    // ------------------------------------------------------------------
    // 3. Reset clears all mutable state
    // ------------------------------------------------------------------
    [Fact]
    public void RenderErrorRegistry_Reset_clears_entries_and_markers_and_summary()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/RenderErrorRegistry.cs");

        source.Should().Contain("public static void Reset()",
            "Reset must be public static for world-reload cleanup");
        source.Should().Contain("_entries.Clear();", "Reset must clear per-type entries");
        source.Should().Contain("_lastSummaryCounts.Clear();", "Reset must clear summary snapshot");
        source.Should().Contain("_frameMarkers.Clear();", "Reset must clear queued visual markers");
        source.Should().Contain("_lastSummaryTime = -999f;", "Reset must reset summary throttle timestamp");
    }

    [Fact]
    public void RenderErrorMarkers_Reset_clears_frame_markers()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/RenderErrorMarkers.cs");

        source.Should().Contain("public static void Reset()",
            "RenderErrorMarkers.Reset must be public static");
        source.Should().Contain("RenderErrorRegistry.ClearFrameMarkers();",
            "Reset must delegate to ClearFrameMarkers so stale markers don't leak across reloads");
    }

    // ------------------------------------------------------------------
    // 4. Frame-marker lifecycle is bounded per frame
    // ------------------------------------------------------------------
    [Fact]
    public void RenderErrorRegistry_DrainFrameMarkers_adds_and_clears()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/RenderErrorRegistry.cs");

        source.Should().Contain("public static void DrainFrameMarkers(List<Marker> into)",
            "DrainFrameMarkers must be public static and accept a target list");
        source.Should().Contain("into.AddRange(_frameMarkers);", "DrainFrameMarkers must append markers to the caller's list");
        source.Should().Contain("_frameMarkers.Clear();", "DrainFrameMarkers must clear the source buffer after draining");
    }

    [Fact]
    public void RenderErrorRegistry_ClearFrameMarkers_is_public_static()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/RenderErrorRegistry.cs");

        source.Should().Contain("public static void ClearFrameMarkers()",
            "ClearFrameMarkers must be public static for the visual sink to drop markers without rendering");
    }

    // ------------------------------------------------------------------
    // 5. Summary log is throttled, not per-frame
    // ------------------------------------------------------------------
    [Fact]
    public void RenderErrorRegistry_MaybeEmitSummary_throttles_by_interval()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/RenderErrorRegistry.cs");

        source.Should().Contain("public static void MaybeEmitSummary()",
            "MaybeEmitSummary must be public static");
        source.Should().Contain("const float SummaryMinInterval = 5f;",
            "summary log must have a minimum interval to avoid console flooding");
        source.Should().Contain("(now - _lastSummaryTime) < SummaryMinInterval",
            "summary must be skipped when interval has not elapsed");
    }
}
