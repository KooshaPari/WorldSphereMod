using System;
using System.IO;
using FluentAssertions;
using Xunit;

[Trait("Category", "E2E")]
public class BridgeServerInvariantsTests
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
    public void BridgeServer_uses_port_fallback_and_main_thread_queue_draining()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Bridge/BridgeServer.cs");

        source.Should().Contain("static readonly int[] CandidatePorts = { Port, 8767, 8768, 8769 };",
            "the bridge listener must fall back across the documented loopback ports");
        source.Should().Contain("public static void EnsureCreated()",
            "the bridge host must recreate itself after scene transitions");
        source.Should().Contain("public static void DrainStaticQueue()",
            "main-thread work must be drained from the shared queue each frame");
        source.Should().Contain("RefreshHealthCache();",
            "health snapshots must be refreshed before the queue is drained");
        source.Should().Contain("RefreshTelemetryCache();",
            "telemetry must be refreshed from the main thread");
        source.Should().Contain("TryRunLiveTelemetryProbeEndOfFrame();",
            "end-of-frame probing must share the same per-frame drain path");
        source.Should().Contain("LiveTelemetryProbeEnabled = true;",
            "successful listener startup must enable the live telemetry probe");
        source.Should().Contain("LiveTelemetryProbeEnabled = false;",
            "bind failures must disable the live telemetry probe so stale probes do not keep running");
    }

    [Fact]
    public void BridgeServer_health_probe_requires_world3d_or_explicit_override()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Bridge/BridgeServer.cs");

        source.Should().Contain("if (!LiveTelemetryProbeEnabled && !Core.savedSettings.DebugSanityCube) return;",
            "the probe must stay opt-in unless the debug sanity cube or live telemetry is enabled");
        source.Should().Contain("if (!Core.IsWorld3D && !LiveTelemetryProbeEnabled) return;",
            "flat worlds should not submit the sanity probe unless live telemetry explicitly overrides it");
        source.Should().Contain("WorldSphereMod.Voxel.SanityTestCube.Draw();",
            "the probe must render the sanity cube before refreshing telemetry");
        source.Should().Contain("WorldSphereMod.Voxel.VoxelRender.Flush();",
            "queued voxel submissions must be flushed before telemetry is sampled");
        source.Should().Contain("WorldSphereMod.Voxel.VoxelMeshCache.DrainPendingDestroy();",
            "deferred mesh destruction must be drained in the same probe path");
    }

    // ---------------------------------------------------------------
    // Perf: BridgeServer must not triple-drain per frame
    // ---------------------------------------------------------------
    // Regression: BridgeServer.Update, LateUpdate, and FixedUpdate all called
    // DrainStaticQueue — ~170 drain invocations/sec at 60Hz + 50Hz FixedUpdate.
    // VoxelRender.LateUpdate already calls DrainStaticQueue for mid-frame submits
    // so LateUpdate and FixedUpdate overrides were fully redundant.
    // Fix: removed LateUpdate and FixedUpdate one-liner drains. (#robustness perf)
    [Fact]
    public void BridgeServer_LateUpdate_and_FixedUpdate_do_not_drain_static_queue()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Bridge/BridgeServer.cs");

        // Update must still drain — it is the primary per-frame consumer
        source.Should().Contain("void Update() => DrainStaticQueue()",
            "BridgeServer.Update must drain the static queue every frame");

        // LateUpdate one-liner drain must be absent
        source.Should().NotContain("void LateUpdate() => DrainStaticQueue()",
            "BridgeServer.LateUpdate must not be a one-liner DrainStaticQueue — " +
            "redundant triple-drain removed; VoxelRender.LateUpdate handles mid-frame submits (#robustness)");

        // FixedUpdate one-liner drain must be absent
        source.Should().NotContain("void FixedUpdate() => DrainStaticQueue()",
            "BridgeServer.FixedUpdate must not call DrainStaticQueue — " +
            "was a ~50Hz redundant drain on top of the 60Hz Update path (#robustness)");
    }
}
