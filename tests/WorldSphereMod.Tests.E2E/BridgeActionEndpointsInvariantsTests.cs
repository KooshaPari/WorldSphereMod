using System;
using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// Source-invariant checks for the action bridge endpoints in BridgeServer.
/// These tests only inspect source text and do not require Unity runtime.
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
    public void Bridge_action_endpoints_are_routed_through_post_dispatch_and_json_writer()
    {
        var bridgeServer = ReadSourceFile("WorldSphereMod/Code/Bridge/BridgeServer.cs");
        var processRequestBody = ExtractMethodBody(bridgeServer, "void ProcessRequest(HttpListenerContext context)");

        processRequestBody.Should().Contain("string.Equals(method, \"POST\", StringComparison.OrdinalIgnoreCase)",
            "the action endpoints must remain POST-only");
        processRequestBody.Should().Contain("string.Equals(path, \"/actions/spawn_units\", StringComparison.OrdinalIgnoreCase)",
            "spawn_units route must be dispatched from ProcessRequest");
        processRequestBody.Should().Contain("string.Equals(path, \"/actions/generate_world\", StringComparison.OrdinalIgnoreCase)",
            "generate_world route must be dispatched from ProcessRequest");
        processRequestBody.Should().Contain("WriteJson(context.Response, SpawnUnitsQueued(countText, race));",
            "spawn_units must return a JSON response via the bridge writer");
        processRequestBody.Should().Contain("WriteJson(context.Response, GenerateWorldQueued());",
            "generate_world must return a JSON response via the bridge writer");
        processRequestBody.Should().Contain("catch (Exception ex)",
            "ProcessRequest must keep the top-level try/catch so failures serialize as JSON errors");
        processRequestBody.Should().Contain("new { ok = false, error = ex.Message }",
            "ProcessRequest exceptions must be normalized into JSON error payloads");

        bridgeServer.Should().Contain("void WriteJson(HttpListenerResponse response, object payload, HttpStatusCode statusCode = HttpStatusCode.OK) => WriteRawJson(response, JsonConvert.SerializeObject(payload, Formatting.None), statusCode);",
            "WriteJson must serialize action responses with compact JSON and forward them to the raw writer");
        bridgeServer.Should().Contain("JsonConvert.SerializeObject(payload, Formatting.None)",
            "JSON responses must serialize compact anonymous payloads without extra formatting");

        var writeRawJsonBody = ExtractMethodBody(bridgeServer, "void WriteRawJson(HttpListenerResponse response, string json, HttpStatusCode statusCode = HttpStatusCode.OK)");
        writeRawJsonBody.Should().Contain("response.ContentType = \"application/json; charset=utf-8\"",
            "bridge action responses must advertise JSON content type");
        writeRawJsonBody.Should().Contain("response.StatusCode = (int)statusCode",
            "bridge action responses must preserve HTTP status codes");
    }

    [Fact]
    public void Spawn_units_endpoint_uses_worldbox_types_and_nested_error_handling()
    {
        var bridgeServer = ReadSourceFile("WorldSphereMod/Code/Bridge/BridgeServer.cs");
        var spawnUnitsBody = ExtractMethodBody(bridgeServer, "object SpawnUnitsQueued(string countText, string race)");

        spawnUnitsBody.Should().Contain("BridgeSettingParser.TryParseNonNegativeInt(countText, out int count)",
            "spawn_units must validate the count query parameter before queueing work");
        spawnUnitsBody.Should().Contain("World.world == null || MapBox.instance == null",
            "spawn_units must short-circuit when the WorldBox world or MapBox are not ready");
        spawnUnitsBody.Should().Contain("World.world",
            "spawn_units must route through the live WorldBox world object");
        spawnUnitsBody.Should().Contain("MapBox.instance",
            "spawn_units must target the live MapBox singleton");
        spawnUnitsBody.Should().Contain("MapBox.width",
            "spawn_units must use WorldBox map dimensions to choose spawn tiles");
        spawnUnitsBody.Should().Contain("MapBox.height",
            "spawn_units must use WorldBox map dimensions to choose spawn tiles");
        spawnUnitsBody.Should().Contain("WorldTile tile = mapBox.GetTile(x, y);",
            "spawn_units must resolve a WorldTile before spawning units");
        spawnUnitsBody.Should().Contain("mapBox.units.createNewUnit(race, tile);",
            "spawn_units must create units through the WorldBox unit manager");
        spawnUnitsBody.Should().Contain("catch (Exception spawnEx)",
            "spawn_units must isolate per-unit creation failures so one bad unit does not abort the whole loop");
        spawnUnitsBody.Should().Contain("catch (Exception ex)",
            "spawn_units must guard the queued work item itself");
        spawnUnitsBody.Should().Contain("return new { ok = true, count, race, queued = true };",
            "spawn_units must return a JSON-friendly queued acknowledgement");
        spawnUnitsBody.Should().Contain("return new { ok = false, error = \"invalid_count\", count = countText };",
            "spawn_units must return JSON error payloads for invalid input");
    }

    [Fact]
    public void Generate_world_endpoint_uses_mapbox_and_returns_a_json_acknowledgement()
    {
        var bridgeServer = ReadSourceFile("WorldSphereMod/Code/Bridge/BridgeServer.cs");
        var generateWorldBody = ExtractMethodBody(bridgeServer, "object GenerateWorldQueued()");

        generateWorldBody.Should().Contain("MapBox.instance == null",
            "generate_world must guard against a missing MapBox instance");
        generateWorldBody.Should().Contain("MapBox.instance.generateNewMap();",
            "generate_world must invoke the WorldBox map generation entrypoint");
        generateWorldBody.Should().Contain("catch (Exception ex)",
            "generate_world must catch and log queued work failures");
        generateWorldBody.Should().Contain("return new { ok = true, queued = true };",
            "generate_world must return a JSON-friendly queued acknowledgement");
    }
}
