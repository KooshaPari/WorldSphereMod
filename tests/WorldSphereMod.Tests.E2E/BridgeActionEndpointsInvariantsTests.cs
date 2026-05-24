using System;
using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// Bridge action endpoint source invariants — mock/stub level, no WorldBox DLLs.
/// </summary>
public sealed class BridgeActionEndpointsInvariantsTests
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

    [Fact]
    public void Spawn_units_endpoint_is_routed_to_a_queued_main_thread_action_with_input_limits()
    {
        var bridgeServer = ReadSourceFile("WorldSphereMod/Code/Bridge/BridgeServer.cs");
        var routeBody = ExtractMethodBody(bridgeServer, "void ProcessRequest(HttpListenerContext context)");
        var spawnBody = ExtractMethodBody(bridgeServer, "object SpawnUnitsQueued(string countText, string race)");

        routeBody.Should().Contain("string.Equals(path, \"/actions/spawn_units\", StringComparison.OrdinalIgnoreCase)",
            "/actions/spawn_units must be routed explicitly from ProcessRequest");
        routeBody.Should().Contain("SpawnUnitsQueued(countText, race)",
            "/actions/spawn_units must dispatch into the queued action helper");
        routeBody.Should().NotContain("InvokeOnMainThread(SpawnUnitsQueued",
            "/actions/spawn_units must not use the synchronous main-thread dispatcher");

        spawnBody.Should().Contain("BridgeSettingParser.TryParseNonNegativeInt(countText, out int count)",
            "spawn_units must validate the requested count before queueing work");
        spawnBody.Should().Contain("count = Math.Min(count, 200);",
            "spawn_units must cap the requested count to the documented safety limit");
        spawnBody.Should().Contain("_mainThreadQueue.Enqueue(() =>",
            "spawn_units must schedule Unity object mutation on the main thread queue");
        spawnBody.Should().Contain("if (World.world == null || MapBox.instance == null)",
            "spawn_units must fail closed when the world or map box is not ready");
        spawnBody.Should().Contain("mapBox.units.createNewUnit(race, tile);",
            "spawn_units must create units from the queued main-thread lambda");
        spawnBody.Should().Contain("return new { ok = true, count, race, queued = true };",
            "spawn_units must acknowledge async queueing to the caller");
        spawnBody.Should().Contain("Debug.Log($\"[WSM3D][Bridge] spawn_units: spawned {spawned}/{count} {race} units\");",
            "spawn_units should report the final spawn count after the queued work runs");
    }

    [Fact]
    public void Generate_world_endpoint_is_routed_to_a_queued_main_thread_action_and_rebuilds_the_map_on_demand()
    {
        var bridgeServer = ReadSourceFile("WorldSphereMod/Code/Bridge/BridgeServer.cs");
        var routeBody = ExtractMethodBody(bridgeServer, "void ProcessRequest(HttpListenerContext context)");
        var generateBody = ExtractMethodBody(bridgeServer, "object GenerateWorldQueued()");

        routeBody.Should().Contain("string.Equals(path, \"/actions/generate_world\", StringComparison.OrdinalIgnoreCase)",
            "/actions/generate_world must be routed explicitly from ProcessRequest");
        routeBody.Should().Contain("GenerateWorldQueued()",
            "/actions/generate_world must dispatch into the queued action helper");
        routeBody.Should().NotContain("InvokeOnMainThread(GenerateWorldQueued",
            "/actions/generate_world must not use the synchronous main-thread dispatcher");

        generateBody.Should().Contain("_mainThreadQueue.Enqueue(() =>",
            "generate_world must schedule map generation on the main thread queue");
        generateBody.Should().Contain("if (MapBox.instance == null)",
            "generate_world must fail closed when the map box is not ready");
        generateBody.Should().Contain("MapBox.instance.generateNewMap();",
            "generate_world must rebuild the world from the queued main-thread lambda");
        generateBody.Should().Contain("Debug.Log(\"[WSM3D][Bridge] generate_world: new map generated\");",
            "generate_world should report successful regeneration after the queued work runs");
        generateBody.Should().Contain("return new { ok = true, queued = true };",
            "generate_world must acknowledge async queueing to the caller");
        generateBody.Should().Contain("Debug.LogWarning(\"[WSM3D][Bridge] generate_world skipped: MapBox not ready\");",
            "generate_world should report a clear readiness guard when the map box is unavailable");
    }
}
