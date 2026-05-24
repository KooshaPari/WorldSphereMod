using System;
using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// Save/load bridge hardening invariants from
/// docs/journeys/scratch/bridge-scene-transition-known-issue.md — mock/stub level, no WorldBox DLLs.
/// </summary>
public sealed class BridgeSaveLoadStabilityInvariantsTests
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
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(fullPath).Should().BeTrue($"source file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    private static string ExtractMethodBody(string source, string signature)
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

    /// <summary>
    /// Minimal stub mirroring BridgeServer.OnDestroy generation guard — deterministic, no Unity/HTTP.
    /// </summary>
    sealed class ListenerGenerationGuardStub
    {
        static int _instanceGeneration;
        bool _listenerRunning;

        public void RegisterInstance(int generation)
        {
            _instanceGeneration = generation;
            _listenerRunning = true;
        }

        public bool TryStopListener(int myGeneration)
        {
            if (myGeneration < _instanceGeneration)
            {
                return false;
            }

            _listenerRunning = false;
            return true;
        }

        public bool IsListenerRunning => _listenerRunning;
    }

    [Fact]
    public void Bridge_save_load_stability_listener_generation_guard_and_LateUpdate_flush_contract()
    {
        var stub = new ListenerGenerationGuardStub();
        stub.RegisterInstance(generation: 1);
        stub.RegisterInstance(generation: 2);
        stub.TryStopListener(myGeneration: 1).Should().BeFalse(
            "stale BridgeServer OnDestroy must not stop the replacement HTTP accept loop");
        stub.IsListenerRunning.Should().BeTrue(
            "replacement listener must stay running when an older generation is destroyed");
        stub.TryStopListener(myGeneration: 2).Should().BeTrue(
            "current-generation teardown may stop the listener");
        stub.IsListenerRunning.Should().BeFalse();

        var bridgeServer = ReadSourceFile("WorldSphereMod/Code/Bridge/BridgeServer.cs");
        var bridgeTick = ReadSourceFile("WorldSphereMod/Code/Bridge/BridgePerFrameTick.cs");
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        var mod = ReadSourceFile("WorldSphereMod/Code/Mod.cs");

        var onDestroyBody = ExtractMethodBody(bridgeServer, "void OnDestroy()");
        int guardIndex = onDestroyBody.IndexOf("_myGeneration < _instanceGeneration", StringComparison.Ordinal);
        int stopIndex = onDestroyBody.IndexOf("StopListener()", StringComparison.Ordinal);
        guardIndex.Should().BeGreaterThanOrEqualTo(0,
            "OnDestroy must compare instance generation before stopping HTTP listener");
        stopIndex.Should().BeGreaterThan(guardIndex,
            "StopListener must only run after the generation guard passes");

        bridgeServer.Should().Contain("static int _instanceGeneration",
            "listener generation must survive BridgeServer instance teardown across save/load");
        bridgeServer.Should().Contain("int _myGeneration",
            "each BridgeServer host must record its generation for stale OnDestroy detection");

        var survivalRunBody = ExtractMethodBody(bridgeTick, "public static void Run(bool runVoxelFrame)");
        IndexOfOrFail(survivalRunBody, "BridgeServer.CaptureMainThread()").Should()
            .BeLessThan(IndexOfOrFail(survivalRunBody, "BridgeServer.EnsureCreated()"),
            "main thread id must be captured before recreating bridge host post-transition");
        IndexOfOrFail(survivalRunBody, "BridgeServer.EnsureCreated()").Should()
            .BeLessThan(IndexOfOrFail(survivalRunBody, "BridgeServer.DrainStaticQueue()"),
            "EnsureCreated must run before draining queued HTTP RPC work each frame");
        IndexOfOrFail(survivalRunBody, "BridgeServer.DrainStaticQueue()").Should()
            .BeLessThan(survivalRunBody.IndexOf("VoxelFrameDriver.TickPerFrame()", StringComparison.Ordinal),
            "queue drain must precede pre-emit voxel work in the primary survival hook");

        survivalRunBody.Should().Contain("if (!runVoxelFrame || !Core.IsWorld3D) return",
            "backup ActorManager drain must skip TickPerFrame so emit postfixes finish before LateUpdate flush");

        bridgeTick.Should().Contain("BridgeSurvival.Run(runVoxelFrame: true)",
            "MapBox.renderStuff must run primary survival hook after save/load transitions");
        bridgeTick.Should().Contain("BridgeSurvival.Run(runVoxelFrame: false)",
            "ActorManager backup must drain bridge without pre-emit voxel work");

        mod.Should().Contain("BridgeServer.EnsureCreated()",
            "Mod.PostInit must re-create bridge after scene transitions");

        var tickBody = ExtractMethodBody(voxelRender, "public static void TickPerFrame()");
        tickBody.Should().NotContain("VoxelRender.Flush()",
            "TickPerFrame runs from MapBox.renderStuff before emit postfixes — flush must stay in LateUpdate");
        tickBody.Should().Contain("Flush runs in LateUpdate after all emit postfixes",
            "TickPerFrame must document deferred flush contract for save/load observability");

        var lateBody = ExtractMethodBody(voxelRender, "void LateUpdate()");
        lateBody.Should().Contain("MeshInstanceBatcher.HasPendingSubmissions",
            "LateUpdate flush must gate on pending batcher work");
        lateBody.Should().Contain("VoxelRender.Flush()",
            "LateUpdate is the end-of-frame sink after Harmony emit postfixes");
        lateBody.Should().Contain("VoxelMeshCache.DrainPendingDestroy()",
            "mesh eviction drain must follow the LateUpdate flush");
    }

    static int IndexOfOrFail(string source, string needle)
    {
        int index = source.IndexOf(needle, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"{needle} must appear in BridgeSurvival.Run");
        return index;
    }
}
