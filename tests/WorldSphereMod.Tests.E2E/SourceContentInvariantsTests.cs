using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using FluentAssertions;
using Newtonsoft.Json.Linq;

public class SourceContentInvariantsTests
{
    // Locate the repo root from test output directory.
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
    public void ProcGenCache_does_not_cache_transient_fallback_meshes()
    {
        var cache = ReadSourceFile("WorldSphereMod/Code/ProcGen/ProcGenCache.cs");
        var meshGen = ReadSourceFile("WorldSphereMod/Code/ProcGen/BuildingMeshGen.cs");

        cache.Should().Contain("BuildingMeshGen.IsTransientMesh(m)",
            "GetOrGenerate must skip caching shared/unreadable fallback meshes");
        meshGen.Should().Contain("public static bool IsTransientMesh(Mesh? mesh)",
            "BuildingMeshGen must expose transient-mesh detection for the cache layer");
        meshGen.Should().Contain("procgen:shared-unreadable",
            "unreadable sprites must use one shared fallback mesh, not per-asset cubes");
        meshGen.Should().NotContain("procgen:fallback:",
            "per-asset fallback cubes must not be generated — they poison ProcGenCache");
        meshGen.Should().NotContain("procgen:unreadable:",
            "per-asset unreadable cubes must not be generated — they poison ProcGenCache");

        var getOrGenerateBody = ExtractMethodBody(cache, "public static Mesh? GetOrGenerate(BuildingAsset asset, BuildingRules rules)");
        getOrGenerateBody.Should().Contain("IsTransientMesh(m)",
            "cache insert path must guard against transient meshes");
    }

    [Fact]
    public void WorldUnloadPatch_OnFinish_is_authoritative_deferred_destroy_and_lighting_teardown()
    {
        var patch = ReadSourceFile("WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs");
        var onFinish = ExtractOnFinishMethodBody(patch);

        onFinish.Should().Contain("WorldSphereMod.Lighting.SunDriver.Teardown()",
            "world unload must tear down the directional-light root so lighting does not bleed across reloads");

        AssertDrainBeforeClear(onFinish, "WorldSphereMod.Voxel.VoxelMeshCache",
            "VoxelMeshCache.Clear drops _pendingDestroy without destroying queued eviction meshes");
        AssertDrainBeforeClear(onFinish, "WorldSphereMod.Rig.RigCache",
            "RigCache.Clear drops _pendingDestroy without destroying evicted base meshes");

        AssertClearThenDrain(onFinish, "WorldSphereMod.ProcGen.ProcGenCache",
            "ProcGenCache.Clear enqueues meshes for deferred destroy on the main thread");
        AssertClearThenDrain(onFinish, "WorldSphereMod.Foliage.CrossedQuadMeshCache",
            "CrossedQuadMeshCache.Clear enqueues meshes for deferred destroy on the main thread");

        onFinish.Should().Contain("WorldSphereMod.Voxel.VoxelRender.Reset()",
            "world unload must reset VoxelRender static material/latch state");
    }

    static string ExtractOnFinishMethodBody(string patchSource)
    {
        const string signature = "public static void OnFinish()";
        int headerIndex = patchSource.IndexOf(signature, StringComparison.Ordinal);
        headerIndex.Should().BeGreaterThanOrEqualTo(0, "WorldUnloadPatch.OnFinish must exist");

        int openBrace = patchSource.IndexOf('{', headerIndex);
        openBrace.Should().BeGreaterThanOrEqualTo(0, "OnFinish must open with a '{'");

        int depth = 0;
        for (int i = openBrace; i < patchSource.Length; i++)
        {
            char c = patchSource[i];
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
                return patchSource.Substring(openBrace + 1, i - openBrace - 1);
            }
        }

        throw new InvalidOperationException("Unbalanced braces while extracting OnFinish body");
    }

    static string ExtractMethodBody(string source, string signature)
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

    static void AssertDrainBeforeClear(string onFinish, string typeName, string because)
    {
        int drainIndex = onFinish.IndexOf(typeName + ".DrainPendingDestroy()", StringComparison.Ordinal);
        int clearIndex = onFinish.IndexOf(typeName + ".Clear()", StringComparison.Ordinal);

        drainIndex.Should().BeGreaterThanOrEqualTo(0, because + $" — {typeName}.DrainPendingDestroy must be wired in OnFinish");
        clearIndex.Should().BeGreaterThanOrEqualTo(0, because + $" — {typeName}.Clear must be wired in OnFinish");
        drainIndex.Should().BeLessThan(clearIndex, because + $" — drain must run before clear for {typeName}");
    }

    static void AssertClearThenDrain(string onFinish, string typeName, string because)
    {
        int clearIndex = onFinish.IndexOf(typeName + ".Clear()", StringComparison.Ordinal);
        int drainIndex = onFinish.IndexOf(typeName + ".DrainPendingDestroy()", StringComparison.Ordinal);

        clearIndex.Should().BeGreaterThanOrEqualTo(0, because + $" — {typeName}.Clear must be wired in OnFinish");
        drainIndex.Should().BeGreaterThanOrEqualTo(0, because + $" — {typeName}.DrainPendingDestroy must be wired in OnFinish");
        clearIndex.Should().BeLessThan(drainIndex, because + $" — clear must run before drain for {typeName}");
    }

    [Fact]
    public void QuantumSprites_calculateactordata3D_keeps_parallel_transform_bookkeeping_only()
    {
        var quantumSprites = ReadSourceFile("WorldSphereMod/Code/QuantumSprites.cs");
        var body = ExtractMethodBody(quantumSprites, "public static void calculateactordata3D(ActorManager __instance)");

        body.Should().Contain("Parallel.For");
        body.Should().Contain("updatePos()");
        body.Should().Contain("Get3DRot()");
        body.Should().NotContain("calculateMainSprite");
        body.Should().NotContain("ignore_generic_render");
    }

    [Fact]
    public void BridgePerFrameTick_drains_queue_via_MapBox_renderStuff_postfix()
    {
        var bridgeTick = ReadSourceFile("WorldSphereMod/Code/Bridge/BridgePerFrameTick.cs");
        var bridgeServer = ReadSourceFile("WorldSphereMod/Code/Bridge/BridgeServer.cs");
        var core = ReadSourceFile("WorldSphereMod/Code/Core.cs");
        var mod = ReadSourceFile("WorldSphereMod/Code/Mod.cs");

        bridgeTick.Should().Contain("[HarmonyPatch(typeof(MapBox), nameof(MapBox.renderStuff))]",
            "bridge queue drain must hook MapBox.renderStuff (always reached via RefreshSphere Prefix)");
        bridgeTick.Should().Contain("[HarmonyPatch(typeof(ActorManager), nameof(ActorManager.precalculateRenderDataParallel))]",
            "backup survival hook must drain when actor render prep runs post-transition");
        bridgeTick.Should().Contain("BridgeSurvival.Run(",
            "shared survival runner must wire queue drain + optional voxel tick");
        bridgeTick.Should().Contain("BridgeServer.DrainStaticQueue()",
            "per-frame hook must drain the static main-thread queue");
        bridgeTick.Should().Contain("BridgeServer.EnsureCreated()",
            "per-frame hook must recreate bridge host after scene transitions");
        bridgeTick.Should().Contain("VoxelFrameDriver.TickPerFrame()",
            "voxel frame driver must run from the same vanilla per-frame hook");
        bridgeTick.Should().Contain("BridgeSurvival.Run(runVoxelFrame: true)",
            "primary MapBox.renderStuff hook must run voxel pre-emit work");
        bridgeTick.Should().Contain("BridgeSurvival.Run(runVoxelFrame: false)",
            "backup ActorManager hook must drain bridge only — emit postfixes finish before LateUpdate flush");

        var survivalRunBody = ExtractMethodBody(bridgeTick, "public static void Run(bool runVoxelFrame)");
        survivalRunBody.Should().Contain("if (!Core.IsWorld3D) return",
            "non-3D worlds skip TickPerFrame while bridge queue drain still runs each hook");
        survivalRunBody.Should().Contain("if (runVoxelFrame)",
            "backup drain must gate TickPerFrame so emit postfixes run before LateUpdate flush");
        survivalRunBody.Should().Contain("VoxelFrameDriver.TickPerFrame()",
            "primary hook must still invoke pre-emit voxel work when runVoxelFrame is true");

        var backupClassStart = bridgeTick.IndexOf("class BridgeSurvivalBackup", StringComparison.Ordinal);
        backupClassStart.Should().BeGreaterThan(0);
        var backupSection = bridgeTick.Substring(backupClassStart);
        var backupPostfixInBackup = ExtractMethodBody(backupSection, "public static void Postfix()");
        backupPostfixInBackup.Should().Contain("runVoxelFrame: false");
        backupPostfixInBackup.Should().NotContain("TickPerFrame()",
            "ActorManager backup must not call TickPerFrame — that runs pre-emit from MapBox.renderStuff");

        core.Should().Contain("Patcher.PatchAll(typeof(WorldSphereMod.Bridge.BridgeSurvivalBackup))",
            "backup survival patch must be registered explicitly in Core.Patch");
        mod.Should().Contain("BridgeServer.EnsureCreated()",
            "Mod.PostInit must re-create bridge after scene transitions");

        bridgeServer.Should().Contain("static int _mainThreadId",
            "main thread id must be static so HTTP RPC survives BridgeServer instance teardown");
        bridgeServer.Should().Contain("_mainThreadId = Thread.CurrentThread.ManagedThreadId",
            "DrainStaticQueue must refresh main thread id from the drain site");
        bridgeServer.Should().Contain("_myGeneration < _instanceGeneration",
            "OnDestroy must not stop HTTP listener when a newer BridgeServer instance exists");
        bridgeServer.Should().Contain("if (_mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId)",
            "InvokeOnMainThread must use the captured static main thread id");
        bridgeServer.Should().Contain("WriteJson(context.Response, BuildHealthPayload());",
            "/health must bypass InvokeOnMainThread so it can answer while Unity main-thread work is stalled");
        bridgeServer.Should().Contain("bridgeAlive = true",
            "health payload must always expose a bridge-alive marker");
        bridgeServer.Should().Contain("listenerThreadAlive = _listenerThread != null && _listenerThread.IsAlive",
            "health payload must stay on bridge-owned thread-safe state");
        bridgeServer.Should().Contain("RefreshHealthCache()",
            "health version/world flags must be cached on the main thread, not read from the listener thread");
        bridgeServer.Should().Contain("version = _cachedHealthVersion",
            "health must expose cached version for PlayCUA without blocking on Unity");
        bridgeServer.Should().Contain("isWorld3D = _cachedIsWorld3D",
            "health must expose cached isWorld3D for PlayCUA without blocking on Unity");
        bridgeServer.Should().NotContain("WriteJson(context.Response, InvokeOnMainThread(BuildHealthPayload));",
            "/health must not be serialized through the timeout-prone main-thread dispatcher");
        bridgeServer.Should().Contain("public static void RefreshTelemetryCache()",
            "telemetry cache must be callable after MeshInstanceBatcher.Flush");
        bridgeServer.Should().Contain("UpdateSettingQueued(string key, string rawValue)",
            "settings POST must keep the listener-thread entrypoint explicit");
        var updateSettingBody = ExtractMethodBody(bridgeServer, "object UpdateSettingQueued(string key, string rawValue)");
        updateSettingBody.Should().Contain("_mainThreadQueue.Enqueue(() =>",
            "settings updates must enqueue Unity mutation and persistence work");
        updateSettingBody.Should().Contain("Core.SaveSettings()",
            "settings persistence must happen on the main thread queue");
        int enqueueIndex = updateSettingBody.IndexOf("_mainThreadQueue.Enqueue(() =>", StringComparison.Ordinal);
        int fieldSetIndex = updateSettingBody.IndexOf("field.SetValue(Core.savedSettings, parsed);", StringComparison.Ordinal);
        int saveSettingsIndex = updateSettingBody.IndexOf("Core.SaveSettings();", StringComparison.Ordinal);
        enqueueIndex.Should().BeGreaterThanOrEqualTo(0);
        fieldSetIndex.Should().BeGreaterThan(enqueueIndex,
            "savedSettings mutation must be deferred into the queued main-thread work");
        saveSettingsIndex.Should().BeGreaterThan(enqueueIndex,
            "settings persistence must be deferred into the queued main-thread work");
        bridgeServer.Should().Contain("WorldSphereMod.Voxel.SanityTestCube.Reset();",
            "bridge start/stop must reset probe state so live telemetry cannot reuse stale positions");
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        voxelRender.Should().Contain("Bridge.BridgeServer.RefreshTelemetryCache()",
            "telemetry must refresh after flush so drawCalls reflect the completed frame");
        voxelRender.Should().MatchRegex(
            @"void LateUpdate\(\)[\s\S]*RefreshTelemetryCache\(\);",
            "LateUpdate must refresh telemetry every frame, not only when HasPendingSubmissions");
        bridgeServer.Should().Contain("WriteJson(context.Response, BuildTelemetryPayload());",
            "/telemetry must bypass InvokeOnMainThread so PlayCUA assert_telemetry does not get null");
        bridgeServer.Should().Contain("drawCalls = _cachedDrawCalls",
            "telemetry must expose cached drawCalls for PlayCUA");
        bridgeServer.Should().Contain("lastNonZeroDrawCalls = _cachedLastNonZeroDrawCalls",
            "telemetry must expose last non-zero drawCalls for between-frame PlayCUA checks");
        bridgeServer.Should().Contain("EnsureVoxelFrameDriverOnBridgeHost()",
            "bridge DDOL host must host VoxelFrameDriver so LateUpdate flush runs after scene transitions");
        bridgeServer.Should().Contain("frameMs = _cachedFrameMs",
            "telemetry must expose cached frameMs for PlayCUA");
        bridgeServer.Should().NotContain("InvokeOnMainThread(BuildTelemetryPayload)",
            "/telemetry must not block on the 5s main-thread dispatcher");
    }

    [Fact]
    public void SanityTestCube_live_probe_refreshes_position_each_draw_and_keeps_normal_caching()
    {
        var cube = ReadSourceFile("WorldSphereMod/Code/Voxel/SanityTestCube.cs");

        cube.Should().Contain("bool liveProbe = Bridge.BridgeServer.LiveTelemetryProbeEnabled");
        cube.Should().Contain("TryEnsureProbePosition(bool liveProbe)");
        cube.Should().Contain("ResolveLiveProbePosition()");
        cube.Should().Contain("LastActorPos = ResolveLiveProbePosition();",
            "live probe positioning must derive from current world state each draw");
        cube.Should().Contain("if (_hasLastActorPos) return true;",
            "normal DebugSanityCube behavior should still retain its first sampled position");
        cube.Should().Contain("World.world != null && MapBox.width > 0 && MapBox.height > 0",
            "live probe fallback should derive from the current world center when available");
        cube.Should().Contain("Reset()",
            "probe state must be reset when bridge telemetry starts or stops");
    }

    [Fact]
    public void VoxelRender_cs_phase1b_drop_and_projectile_emit_use_shared_voxel_path()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        voxelRender.Should().Contain("[HarmonyPatch(typeof(Drop), nameof(Drop.updatePosition))]",
            "Phase 1b must postfix Drop.updatePosition after the 3D transform gate runs");
        voxelRender.Should().Contain("[HarmonyPatch(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawProjectiles))]",
            "Phase 1b must postfix QuantumSpriteLibrary.drawProjectiles at the game draw boundary");
        voxelRender.Should().Contain("class DropVoxelEmit",
            "drop voxel submission should live in a dedicated phase-gated patch class");
        voxelRender.Should().Contain("class ProjectileVoxelEmit",
            "projectile voxel submission should live in a dedicated phase-gated patch class");
        voxelRender.Should().Contain("World.world.projectiles.list",
            "projectile voxel emit must iterate the same projectile list as vanilla drawProjectiles");

        var dropBody = ExtractMethodBody(voxelRender, "public static void EmitVoxel(Drop __instance)");
        dropBody.Should().Contain("!Core.savedSettings.VoxelEntities");
        dropBody.Should().Contain("VoxelMeshCache.Get(sp");
        dropBody.Should().Contain("Submit(mesh, trs, tint)");

        var projectileBody = ExtractMethodBody(voxelRender, "public static void EmitVoxels(QuantumSpriteAsset pAsset)");
        projectileBody.Should().Contain("!Core.savedSettings.VoxelEntities");
        projectileBody.Should().Contain("VoxelMeshCache.Get(sprite");
        projectileBody.Should().Contain("Submit(mesh, trs, tint)");
        projectileBody.Should().NotContain("Thread.",
            "Phase 1b voxel submission must stay on the main-thread draw/update path");
    }

    [Fact]
    public void VoxelRender_cs_cull_skip_path_does_not_set_has_normal_render_false()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        // The recent fix ensures that after checking FrustumCuller.IsVisible,
        // we do NOT naively set rd.has_normal_render[i] = false. That would
        // disable rendering for culled entities, breaking the voxel pipeline.
        // We should skip the entire entity, not set has_normal_render false.

        // Find all occurrences of the culling check.
        var cullPattern = @"if\s*\(\s*!WorldSphereMod\.LOD\.FrustumCuller\.IsVisible\s*\([^)]+\)\s*\)";
        var matches = Regex.Matches(voxelRender, cullPattern);

        matches.Should().NotBeEmpty("VoxelRender must check FrustumCuller.IsVisible for frustum culling");

        // For each cull check, verify that the immediate next occurrence of has_normal_render
        // does NOT set it to false right after the IsVisible check.
        foreach (Match cullMatch in matches)
        {
            var cullEndPos = cullMatch.Index + cullMatch.Length;
            var nextSectionStart = cullEndPos;
            var nextSectionEnd = Math.Min(nextSectionStart + 200, voxelRender.Length);
            var nextSection = voxelRender.Substring(nextSectionStart, nextSectionEnd - nextSectionStart);

            // Within the next 200 chars after IsVisible, we should NOT see has_normal_render[i] = false
            // (that pattern would indicate the buggy code).
            var hasNormalRenderFalsePattern = @"has_normal_render\s*\[\s*\w+\s*\]\s*=\s*false";
            var hasBuggyCode = Regex.IsMatch(nextSection, hasNormalRenderFalsePattern);

            hasBuggyCode.Should().BeFalse(
                "VoxelRender must NOT set rd.has_normal_render[i] = false immediately after IsVisible check. " +
                "This would disable rendering for culled entities. Skip the entity instead.");
        }
    }

    [Fact]
    public void VoxelRender_cs_EnsureMaterial_excludes_default_sprites_and_hidden_colored()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        // EnsureMaterial must NOT include "Sprites/Default" or "Hidden/Internal-Colored"
        // because those are not real shader assets.
        voxelRender.Should().NotContain("\"Sprites/Default\"",
            "VoxelRender.EnsureMaterial must exclude Sprites/Default — it's a dummy fallback");

        voxelRender.Should().NotContain("\"Hidden/Internal-Colored\"",
            "VoxelRender.EnsureMaterial must exclude Hidden/Internal-Colored — it's internal-only");
    }

    [Fact]
    public void VoxelRender_cs_EnsureMaterial_reads_enableInstancing_after_setting()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        // Post-set validation: after assigning enableInstancing, the code must read it back
        // to verify it took effect (some materials may not support it).
        var ensureMethodPattern = @"(?:public\s+)?static\s+bool\s+EnsureMaterial\s*\([^)]*\)[\s\S]*?(?=\n\s{0,4}(?:public|static|private|\}))";
        var match = Regex.Match(voxelRender, ensureMethodPattern);

        match.Success.Should().BeTrue("VoxelRender.EnsureMaterial method must exist");

        var methodBody = match.Value;
        // Look for enableInstancing assignment followed by a read/check.
        var setThenReadPattern = @"enableInstancing\s*=\s*true[\s\S]*?enableInstancing";
        var hasSetAndRead = Regex.IsMatch(methodBody, setThenReadPattern);

        hasSetAndRead.Should().BeTrue(
            "VoxelRender.EnsureMaterial must verify enableInstancing was accepted after setting it");
    }

    [Fact]
    public void VoxelFrameDriver_LateUpdate_flushes_after_emit_postfixes_not_TickPerFrame()
    {
        var voxelRender = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        var tickBody = ExtractMethodBody(voxelRender, "public static void TickPerFrame()");
        tickBody.Should().NotContain("VoxelRender.Flush()",
            "TickPerFrame runs from MapBox.renderStuff before emit postfixes — flush must not run there");
        tickBody.Should().Contain("Flush runs in LateUpdate after all emit postfixes",
            "TickPerFrame must document that flush is deferred to LateUpdate");

        var lateBody = ExtractMethodBody(voxelRender, "void LateUpdate()");
        lateBody.Should().Contain("MeshInstanceBatcher.HasPendingSubmissions",
            "LateUpdate must gate flush on pending batcher work");
        lateBody.Should().Contain("VoxelRender.Flush()",
            "LateUpdate is the end-of-frame sink after Harmony emit postfixes");
        lateBody.Should().Contain("VoxelMeshCache.DrainPendingDestroy()",
            "mesh eviction drain must follow the LateUpdate flush");
    }

    [Fact]
    public void MeshInstanceBatcher_cs_Flush_wraps_DrawMeshInstanced_in_try_catch()
    {
        var batcher = ReadSourceFile("WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs");

        // The Flush method must wrap DrawMeshInstanced in try/catch to handle GPU exhaustion.
        batcher.Should().Contain("catch (System.InvalidOperationException",
            "MeshInstanceBatcher.Flush must catch InvalidOperationException from DrawMeshInstanced " +
            "in case the GPU runs out of instancing slots");
        batcher.Should().Contain("if (_mainThreadId == 0)",
            "SetMainThread must capture the Unity main thread id once, not overwrite on every drain");
    }

    [Fact]
    public void WorldSphereTab_cs_CreateButtons_contains_3D_Phases_window()
    {
        var tab = ReadSourceFile("WorldSphereMod/Code/WorldSphereTab.cs");

        // The CreateButtons method must reference "3D Phases" — the new window containing all phase toggles.
        tab.Should().Contain("3D Phases",
            "WorldSphereTab.CreateButtons must define the '3D Phases' window button");
    }

    [Fact]
    public void WorldSphereTab_cs_wires_core_phase_toggles_via_TogglePhase()
    {
        var tab = ReadSourceFile("WorldSphereMod/Code/WorldSphereTab.cs");

        tab.Should().Contain("static void TogglePhase(string phaseToggleId)",
            "WorldSphereTab must route phase buttons through a shared TogglePhase handler");

        var expectedPhaseButtonIds = new[]
        {
            "voxel_entities",
            "procedural_buildings",
            "crossed_quad_foliage",
            "biome_blending",
            "mesh_water",
            "high_shadows",
            "skeletal_animation",
            "worldspace_ui",
            "day_night_cycle",
            "post_fx",
            "particle_effects"
        };

        foreach (var buttonId in expectedPhaseButtonIds)
        {
            tab.Should().Contain($"\"{buttonId}\"",
                $"WorldSphereTab 3D Phases window must expose toggle '{buttonId}'");
        }
    }

    [Fact]
    public void WSM3DRenderer_forward_plus_scaffold_wires_command_buffer_and_settings_gate()
    {
        var renderer = ReadSourceFile("WorldSphereMod/Code/Renderer/WSM3DRenderer.cs");
        var settings = ReadSourceFile("WorldSphereMod/Code/SavedSettings.cs");
        var mod = ReadSourceFile("WorldSphereMod/Code/Mod.cs");

        settings.Should().Contain("public bool ForwardPlusRenderer = false",
            "Forward+ renderer is Tier 5 opt-in; must default OFF until passes land");

        renderer.Should().Contain("WSM3D.Forward\u002B", because: "CommandBuffer must use spec name");
        renderer.Should().Contain("CameraEvent.BeforeImageEffects",
            "inject at BeforeImageEffects so WorldBox UI overlays cleanly");
        renderer.Should().Contain("ForwardPlusRenderer",
            "Execute must gate on SavedSettings.ForwardPlusRenderer");
        renderer.Should().Contain("EnsureCreated()",
            "renderer must expose idempotent EnsureCreated for PostInit wiring");

        var executeBody = ExtractMethodBody(renderer, "public void Execute()");
        executeBody.Should().Contain("_commandBuffer.Clear()",
            "Execute scaffold must clear the command buffer each frame");
        executeBody.Should().Contain("AllocateTargets()",
            "Execute must reserve RTs via AllocateTargets stub");
        executeBody.Should().Contain("DepthPrepass()",
            "Execute must invoke DepthPrepass after AllocateTargets");
        executeBody.Should().Contain("_executeStubLogged",
            "Execute must log scaffold activation once via a guard flag");

        mod.Should().Contain("WSM3DRenderer.EnsureCreated()",
            "Mod.PostInit must attach renderer after scene transitions");
    }

    [Fact]
    public void EnJson_parses_as_valid_JSON()
    {
        var enJson = ReadSourceFile("WorldSphereMod/Locales/en.json");

        Action parseAction = () =>
        {
            var obj = JObject.Parse(enJson);
            obj.Should().NotBeNull();
        };

        parseAction.Should().NotThrow("en.json must be valid JSON");
    }

    [Fact]
    public void EnJson_contains_all_eleven_phase_toggle_keys()
    {
        var enJsonText = ReadSourceFile("WorldSphereMod/Locales/en.json");
        var enJson = JObject.Parse(enJsonText);

        var phaseKeys = new[]
        {
            "voxel_entities",
            "procedural_buildings",
            "crossed_quad_foliage",
            "biome_blending",
            "mesh_water",
            "high_shadows",
            "skeletal_animation",
            "worldspace_ui",
            "day_night_cycle",
            "post_fx",
            "particle_effects"
        };

        foreach (var key in phaseKeys)
        {
            enJson.ContainsKey(key).Should().BeTrue(
                $"en.json must contain locale key '{key}'");
        }
    }

    [Fact]
    public void EnJson_contains_descriptions_for_all_eleven_phases()
    {
        var enJsonText = ReadSourceFile("WorldSphereMod/Locales/en.json");
        var enJson = JObject.Parse(enJsonText);

        var phaseKeys = new[]
        {
            "voxel_entities",
            "procedural_buildings",
            "crossed_quad_foliage",
            "biome_blending",
            "mesh_water",
            "high_shadows",
            "skeletal_animation",
            "worldspace_ui",
            "day_night_cycle",
            "post_fx",
            "particle_effects"
        };

        foreach (var key in phaseKeys)
        {
            var descKey = $"{key}_description";
            enJson.ContainsKey(descKey).Should().BeTrue(
                $"en.json must contain locale description key '{descKey}'");
        }
    }

    [Fact]
    public void EnJson_phase_keys_and_descriptions_are_non_empty()
    {
        var enJsonText = ReadSourceFile("WorldSphereMod/Locales/en.json");
        var enJson = JObject.Parse(enJsonText);

        var phaseKeys = new[]
        {
            "voxel_entities",
            "procedural_buildings",
            "crossed_quad_foliage",
            "biome_blending",
            "mesh_water",
            "high_shadows",
            "skeletal_animation",
            "worldspace_ui",
            "day_night_cycle",
            "post_fx",
            "particle_effects"
        };

        foreach (var key in phaseKeys)
        {
            var mainText = enJson[key]?.ToString();
            mainText.Should().NotBeNullOrWhiteSpace(
                $"en.json key '{key}' must have non-empty string value");

            var descKey = $"{key}_description";
            var descText = enJson[descKey]?.ToString();
            descText.Should().NotBeNullOrWhiteSpace(
                $"en.json key '{descKey}' must have non-empty string value");
        }
    }
}
