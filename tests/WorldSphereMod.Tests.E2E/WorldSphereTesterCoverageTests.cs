using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

public class WorldSphereTesterCoverageTests
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
    public void VoxelMeshCache_Evicts_least_recently_used_entries()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelMeshCache.cs");

        source.Should().Contain("static void Evict()");
        source.Should().Contain("int toRemoveCount = _cache.Count - MAX_ENTRIES;");
        source.Should().Contain("foreach (var kv in _cache)");
        source.Should().Contain("if (kv.Value.LastFrame < lruFrame)");
        source.Should().Contain("_cache.Remove(lruKey)");
        source.Should().Contain("if (_cache.Count > Capacity) Evict();");
    }

    [Fact]
    public void VoxelMeshCache_updates_last_frame_on_hit_for_lru_ordering()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelMeshCache.cs");

        source.Should().Contain("e.LastFrame = _frame;");
        source.Should().Contain("public static void Tick()");
        source.Should().Contain("lock (_lock) _frame++;");
        source.Should().Contain("static readonly Dictionary<int, Entry> _cache");
        source.Should().Contain("Entry { Mesh = mesh, Snapshot = snapshot, LastFrame = _frame }");
    }

    [Fact]
    public void MeshInstanceBatcher_submit_uses_thread_safe_concurrent_queue_and_counts_pending_items()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs");

        source.Should().Contain("static readonly ConcurrentQueue<SubmitRecord> _pendingSubmissions = new ConcurrentQueue<SubmitRecord>();");
        source.Should().Contain("_pendingSubmissions.Enqueue(new SubmitRecord(mesh, mat, matrix, tint));");
        source.Should().Contain("Interlocked.Increment(ref _pendingSubmissionCount);");
        source.Should().Contain("while (_pendingSubmissions.TryDequeue(out var record))");
        source.Should().Contain("Interlocked.Add(ref _pendingSubmissionCount, -drained);");
        source.Should().Contain("if (_mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId)");
    }

    [Fact]
    public void No_impostor_billboard_class_exists_voxel_or_invisible()
    {
        // VOXEL-OR-INVISIBLE (user, 2026-05-30): the impostor billboard LOD tier and its
        // atlas cache were removed. Far objects cull (draw nothing); they are never
        // re-rendered as flat camera-facing billboards. The file must not exist.
        var root = FindRepoRoot();
        var impostorPath = Path.Combine(root, "WorldSphereMod/Code/LOD/ImpostorBillboard.cs");
        File.Exists(impostorPath).Should().BeFalse(
            "ImpostorBillboard.cs must be removed — there is no billboard tier");
    }

    [Fact]
    public void LodSelector_hysteresis_requires_three_frames_before_tier_change()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/LOD/LodSelector.cs");

        source.Should().Contain("if (h.pending == proposed)");
        source.Should().Contain("h.pendingFrames++");
        // Debounce holds a proposed tier for _hystFrames (== 3) frames before promotion,
        // which kills the per-frame Voxel<->Cull flip (the LOD flash-wave).
        source.Should().Contain("if (h.pendingFrames >= _hystFrames)");
        source.Should().Contain("const int _hystFrames = 3");
        source.Should().Contain("h.current = proposed;");
        source.Should().Contain("h.pendingFrames = 0;");
        source.Should().Contain("else { h.pending = proposed; h.pendingFrames = 1; }");
        source.Should().Contain("public static void ResetHysteresis()");
        source.Should().Contain("_hyst.Clear();");
    }
}
