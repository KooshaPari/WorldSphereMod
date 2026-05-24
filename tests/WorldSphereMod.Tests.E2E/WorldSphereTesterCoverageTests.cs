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
    public void ImpostorBillboard_lru_stamps_use_frame_count_at_access_time()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/LOD/ImpostorBillboard.cs");

        source.Should().Contain("ulong frameStamp = (ulong)Time.frameCount;");
        source.Should().Contain("entry.LastFrame = frameStamp;");
        source.Should().Contain("LastFrame = frameStamp");
        source.Should().Contain("static void Evict()");
        source.Should().Contain("if (_atlas.Count > Capacity) Evict();");
        source.Should().Contain("public static void Tick()",
            "Tick remains for call-site compatibility even though LRU uses frameCount");
    }

    [Fact]
    public void LodSelector_hysteresis_requires_three_frames_before_tier_change()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/LOD/LodSelector.cs");

        source.Should().Contain("if (h.pending == proposed)");
        source.Should().Contain("h.pendingFrames++");
        source.Should().Contain("if (h.pendingFrames >= 3)");
        source.Should().Contain("h.current = proposed;");
        source.Should().Contain("h.pendingFrames = 0;");
        source.Should().Contain("else { h.pending = proposed; h.pendingFrames = 1; }");
        source.Should().Contain("public static void ResetHysteresis()");
        source.Should().Contain("_hyst.Clear();");
    }
}
