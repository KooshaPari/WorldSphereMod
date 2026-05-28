using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Voxel pipeline regression tests. Each test guards a specific invariant that,
/// when broken, caused invisible/black/missing voxel actors during the Phase 1
/// bringup (alpha.1 through alpha.8). These are source-level E2E checks — they
/// parse the actual .cs files so they catch regressions even when the Unity
/// runtime isn't available.
///
/// Regression catalogue:
///   1. _bakeEmission black  -> actors invisible under Standard shader (no scene light)
///   2. VoxelScaleMultiplier too small -> sub-pixel meshes, invisible actors
///   3. VoxelEntities default false -> voxel pipeline never activates on fresh install
///   4. DefaultDepth too low -> paper-thin 2.5D extrusions
///   5. EnsureMaterial missing late-upgrade path -> stuck on Standard after bundle loads
///   6. Shader load list missing OpaqueVertexColor -> bundle shaders never cached
///   7. Bundle load not wrapped in try/catch -> missing bundle NREs the entire mod
/// </summary>
public class VoxelPipelineRegressionTests
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

    // ---------------------------------------------------------------
    // 1. _bakeEmission must NOT be black (0,0,0)
    // ---------------------------------------------------------------
    // Regression: Standard shader is LIT. Without ambient/directional light
    // hitting voxel meshes, a black emission value means zero light reaches
    // the framebuffer -> 100% invisible actors.
    [Fact]
    public void BakeEmission_is_not_black()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs");

        // Match the _bakeEmission field declaration
        var match = Regex.Match(source,
            @"_bakeEmission\s*=\s*new\s+(?:UnityEngine\.)?Color\(\s*" +
            @"(?<r>[0-9.f]+)\s*,\s*(?<g>[0-9.f]+)\s*,\s*(?<b>[0-9.f]+)");

        match.Success.Should().BeTrue(
            "_bakeEmission field must exist in MeshInstanceBatcher.cs");

        float r = ParseFloatLiteral(match.Groups["r"].Value);
        float g = ParseFloatLiteral(match.Groups["g"].Value);
        float b = ParseFloatLiteral(match.Groups["b"].Value);

        (r + g + b).Should().BeGreaterThan(0f,
            "_bakeEmission (r={0}, g={1}, b={2}) must not be black — " +
            "Standard shader without scene light renders black actors without emission");
    }

    // ---------------------------------------------------------------
    // 2. VoxelScaleMultiplier default >= 8.0
    // ---------------------------------------------------------------
    // Regression: upstream sprite scale is ~0.1 world units. Without a
    // multiplier >= 8, voxel meshes are sub-pixel at strategy zoom and
    // read as invisible even when the pipeline is otherwise correct.
    [Fact]
    public void VoxelScaleMultiplier_default_is_at_least_8()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/SavedSettings.cs");

        var match = Regex.Match(source,
            @"VoxelScaleMultiplier\s*=\s*(?<val>[0-9.f]+)");

        match.Success.Should().BeTrue(
            "VoxelScaleMultiplier field must exist in SavedSettings.cs");

        float value = ParseFloatLiteral(match.Groups["val"].Value);

        value.Should().BeGreaterThanOrEqualTo(8.0f,
            "VoxelScaleMultiplier default must be >= 8.0 to produce visible meshes " +
            "at strategy-view camera altitude (sub-pixel regression at lower values)");
    }

    // ---------------------------------------------------------------
    // 3. VoxelEntities defaults to true
    // ---------------------------------------------------------------
    // Regression: if VoxelEntities defaults to false in SavedSettings,
    // fresh installs never activate the voxel pipeline. The Phase gate
    // skips all Harmony patches, and actors render as 2D sprites forever.
    [Fact]
    public void VoxelEntities_defaults_to_true()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/SavedSettings.cs");

        var match = Regex.Match(source,
            @"public\s+bool\s+VoxelEntities\s*=\s*(?<val>true|false)");

        match.Success.Should().BeTrue(
            "VoxelEntities field must exist in SavedSettings.cs with an explicit default");

        match.Groups["val"].Value.Should().Be("true",
            "VoxelEntities must default to true so the voxel pipeline activates " +
            "on fresh installs without manual settings editing");
    }

    // ---------------------------------------------------------------
    // 4. DefaultDepth >= 8
    // ---------------------------------------------------------------
    // Regression: depth < 8 produces paper-thin extrusions that read as
    // flat 2.5D slabs from the side and fail the "looks 3D" bar for
    // Phase 1 acceptance.
    [Fact]
    public void SpriteVoxelizer_DefaultDepth_is_at_least_8()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs");

        var match = Regex.Match(source,
            @"const\s+int\s+DefaultDepth\s*=\s*(?<val>\d+)");

        match.Success.Should().BeTrue(
            "DefaultDepth const must exist in SpriteVoxelizer.cs");

        int value = int.Parse(match.Groups["val"].Value);

        value.Should().BeGreaterThanOrEqualTo(8,
            "DefaultDepth must be >= 8 for acceptable voxel volume depth; " +
            "lower values produce paper-thin 2.5D extrusions");
    }

    // ---------------------------------------------------------------
    // 5. EnsureMaterial has late-upgrade path (Standard -> OpaqueVertexColor)
    // ---------------------------------------------------------------
    // Regression: the shader bundle loads asynchronously. If EnsureMaterial
    // resolves to Standard before the bundle finishes, actors render black
    // (Standard + no light). The late-upgrade path checks for the bundle
    // shader on every call and swaps the material when it becomes available.
    [Fact]
    public void EnsureMaterial_has_late_upgrade_from_Standard_to_OpaqueVertexColor()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        // The late-upgrade guard must check if current shader is Standard
        // AND if OpaqueVertexColor is now available in LoadedShaders.
        source.Should().Contain("shader.name == \"Standard\"",
            "EnsureMaterial must detect when the current material is still on the " +
            "Standard shader fallback");

        source.Should().Contain("LoadedShaders.ContainsKey(\"OpaqueVertexColor\")",
            "EnsureMaterial must check LoadedShaders for OpaqueVertexColor availability " +
            "to trigger the late-upgrade path");

        // The upgrade must actually replace the material
        source.Should().Contain("upgraded from Standard to OpaqueVertexColor",
            "EnsureMaterial must log the late-upgrade so the diagnostic pipeline " +
            "can confirm the swap happened");
    }

    // ---------------------------------------------------------------
    // 5a. ActorVoxelEmit Harmony wiring is present
    // ---------------------------------------------------------------
    // Regression: if the Harmony patch attributes are moved, renamed, or
    // removed, the voxel actor pipeline silently stops emitting voxels even
    // though the rest of the render code still compiles.
    [Fact]
    public void ActorVoxelEmit_has_harmony_postfix_and_phase_gate()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        source.Should().Contain("[Phase(nameof(SavedSettings.VoxelEntities))]",
            "ActorVoxelEmit must remain gated by VoxelEntities so the phase can be " +
            "enabled/disabled through SavedSettings");

        source.Should().Contain("[HarmonyPatch(typeof(ActorManager), nameof(ActorManager.precalculateRenderDataParallel))]",
            "ActorVoxelEmit must remain patched against ActorManager.precalculateRenderDataParallel");

        source.Should().Contain("[HarmonyPostfix]",
            "EmitVoxels must remain a Harmony postfix so it runs after the game's render-data pass");

        var actorVoxelEmitBody = ExtractTypeBody(source, "public static class ActorVoxelEmit");
        actorVoxelEmitBody.Should().Contain("EmitVoxels(ActorManager __instance)",
            "ActorVoxelEmit must still expose the EmitVoxels postfix entry point");
    }

    // ---------------------------------------------------------------
    // 5b. VoxelRender.Reset must call MeshInstanceBatcher.Reset
    // ---------------------------------------------------------------
    // Regression: the world-unload path reset VoxelRender but missed the
    // batcher reset, leaving stale mesh-instance state alive across reloads.
    [Fact]
    public void VoxelRender_Reset_calls_MeshInstanceBatcher_Reset()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");
        var resetBody = ExtractMethodBody(source, "public static void Reset()");

        resetBody.Should().Contain("MeshInstanceBatcher.Reset()",
            "VoxelRender.Reset must reset MeshInstanceBatcher so stale batch state " +
            "does not survive world unload/reload");
    }

    // ---------------------------------------------------------------
    // 6. Shader load list includes OpaqueVertexColor
    // ---------------------------------------------------------------
    // Regression: if OpaqueVertexColor is not in the shader load list,
    // the bundle loads but the voxel-specific shader is never cached in
    // LoadedShaders. EnsureMaterial falls through to Standard, which
    // renders black without scene light.
    [Fact]
    public void Core_shader_load_list_includes_OpaqueVertexColor()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Core.cs");

        // SafeShaders constant must contain OpaqueVertexColor.
        source.Should().Contain("\"OpaqueVertexColor\"",
            "Core.cs SafeShaders must include \"OpaqueVertexColor\" so it gets " +
            "loaded from the wsm3d-shaders bundle into LoadedShaders cache");

        // Also verify it gets stored into LoadedShaders
        source.Should().Contain("LoadedShaders[shaderName] = sh",
            "loaded shaders must be stored in the LoadedShaders dictionary");
    }

    // ---------------------------------------------------------------
    // 6b. Shader load list matches SafeShaders EXACTLY
    // ---------------------------------------------------------------
    // ADR-0013: loading corrupted shaders from wsm3d-shaders triggers
    // Unity's native crash reporter ("Uploading Crash Report") which
    // freezes the game. The foreach loop MUST use SafeShaders and
    // SafeShaders MUST contain exactly the curated runtime set, no more.
    [Fact]
    public void Core_shader_load_list_matches_SafeShaders_exactly()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Core.cs");

        // SafeShaders must be a named constant (not inline)
        source.Should().Contain("public static readonly string[] SafeShaders",
            "SafeShaders must be a public static readonly string[] constant");

        // The foreach must iterate over SafeShaders, not an inline array
        source.Should().Contain("foreach (var shaderName in SafeShaders)",
            "the shader load loop must iterate over the SafeShaders constant, " +
            "not an inline array — prevents agents from silently expanding the list");

        // Extract the SafeShaders array initializer and verify exact contents.
        var arrayMatch = Regex.Match(source,
            @"SafeShaders\s*=\s*new\s*\[\]\s*\{(?<body>[^}]*)\}",
            RegexOptions.Singleline);
        arrayMatch.Success.Should().BeTrue("SafeShaders array initializer must exist");

        var bodyWithoutLineComments = Regex.Replace(
            arrayMatch.Groups["body"].Value,
            @"//.*$",
            string.Empty,
            RegexOptions.Multiline);
        var entries = Regex.Matches(bodyWithoutLineComments, @"""(?<name>[^""]+)""");
        var shaderNames = new string[entries.Count];
        for (int i = 0; i < entries.Count; i++)
            shaderNames[i] = entries[i].Groups["name"].Value;

        var expected = new[]
        {
            "OpaqueVertexColor",
            "GerstnerWater",
            "ColorGradingLUT",
            "ProceduralSky",
            "Impostor",
            "ScreenSpaceAO",
            "ScreenSpaceGI",
            "BrpBloom",
            "BrpACES",
        };
        shaderNames.Should().BeEquivalentTo(expected,
            "SafeShaders must contain EXACTLY the runtime shader load set");

        // The ADR-0013 reference must be present as a guard against uninformed edits
        source.Should().Contain("ADR-0013",
            "the SafeShaders constant or its comment must reference ADR-0013 " +
            "so future editors know WHY the list is restricted");
    }

    // ---------------------------------------------------------------
    // 7. Bundle load is wrapped in try/catch
    // ---------------------------------------------------------------
    // Regression: if the wsm3d-shaders bundle is missing or corrupt and
    // the load is not wrapped in try/catch, the entire LoadAssets method
    // throws, preventing CompoundSphereMesh and CompoundSphereMaterial
    // from being assigned, which makes ALL terrain invisible.
    [Fact]
    public void Core_shader_bundle_load_is_wrapped_in_try_catch()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Core.cs");

        // The initial bundle acquisition must be guarded
        source.Should().Contain("try { shaderAb = AssetBundleUtils.GetAssetBundle(\"wsm3d-shaders\"); }",
            "wsm3d-shaders bundle load must be wrapped in try/catch so a missing " +
            "bundle does not crash LoadAssets and leave terrain assets null");

        // The shader iteration loop inside the bundle must also be guarded
        // (it's a separate try/catch around the foreach)
        var loadAssetsBody = ExtractMethodBody(source, "static void LoadAssets()");

        // Count try blocks related to shader loading
        int shaderTryCount = CountOccurrences(loadAssetsBody, "try");

        shaderTryCount.Should().BeGreaterThanOrEqualTo(2,
            "LoadAssets must have at least 2 try blocks in the shader section: " +
            "one around the bundle acquisition and one around the shader iteration loop");

        // The shader iteration must also have a catch
        loadAssetsBody.Should().Contain("catch (System.Exception ex)",
            "shader iteration loop must catch exceptions with context for diagnostics");
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    static float ParseFloatLiteral(string literal)
    {
        // Strip trailing 'f' or 'F' suffix from C# float literals
        string clean = literal.TrimEnd('f', 'F');
        return float.Parse(clean, System.Globalization.CultureInfo.InvariantCulture);
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

    static string ExtractTypeBody(string source, string signature)
    {
        int headerIndex = source.IndexOf(signature, StringComparison.Ordinal);
        headerIndex.Should().BeGreaterThanOrEqualTo(0, $"type signature should exist: {signature}");

        int openBrace = source.IndexOf('{', headerIndex);
        openBrace.Should().BeGreaterThanOrEqualTo(0, "type must open with a '{'");

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

        throw new InvalidOperationException("Unbalanced braces while extracting type body");
    }

    // ---------------------------------------------------------------
    // 8. Become3D must guard on MaxTilesFor3D
    // ---------------------------------------------------------------
    // Regression: large maps (e.g. 576x576 = 331K tiles) cause GPU hangs
    // during SphereManager creation. Become3D must read MaxTilesFor3D
    // from savedSettings and early-return when totalTiles exceeds it.
    [Fact]
    public void Become3D_guards_on_MaxTilesFor3D()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Core.cs");
        var become3DBody = ExtractMethodBody(source, "public static void Become3D()");

        become3DBody.Should().Contain("savedSettings.MaxTilesFor3D",
            "Become3D must read the MaxTilesFor3D threshold from savedSettings " +
            "to gate 3D mode on large maps that would cause GPU hangs");

        become3DBody.Should().Contain("MapBox.width * MapBox.height",
            "Become3D must compute total tile count from MapBox dimensions " +
            "to compare against MaxTilesFor3D");

        become3DBody.Should().Contain("totalTiles > maxTiles",
            "Become3D must compare the computed tile count against the max " +
            "and early-return when the map is too large for 3D mode");
    }

    // ---------------------------------------------------------------
    // 9. SavedSettings.MaxTilesFor3D default >= 65536
    // ---------------------------------------------------------------
    // The default must be high enough to support all standard map sizes
    // (up to ~256x256 = 65536 tiles) while still gating truly large maps
    // that would hang the GPU.
    [Fact]
    public void SavedSettings_MaxTilesFor3D_default_is_at_least_65536()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/SavedSettings.cs");

        var match = Regex.Match(source,
            @"public\s+int\s+MaxTilesFor3D\s*=\s*(?<val>\d+)");

        match.Success.Should().BeTrue(
            "MaxTilesFor3D field must exist in SavedSettings.cs with an explicit int default");

        int value = int.Parse(match.Groups["val"].Value);

        value.Should().BeGreaterThanOrEqualTo(65536,
            "MaxTilesFor3D default must be >= 65536 so all standard map sizes " +
            "(up to ~256x256) work in 3D mode; current value is " + value);
    }

    // ---------------------------------------------------------------
    // 10. SkeletalAnimation gate must precede RigDriver.SubmitSkinnedActor
    // ---------------------------------------------------------------
    // Regression: when SkeletalAnimation is disabled, the skeletal-rig path
    // in ActorVoxelEmit.EmitVoxels MUST skip entirely. If the gate is
    // removed or moved, SubmitSkinnedActor runs unconditionally, causing
    // double-rendering (voxel + rig) and wasted GPU work on settings where
    // the rig system is intentionally disabled.
    [Fact]
    public void SkeletalPath_is_gated_by_SkeletalAnimation_setting()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Voxel/VoxelRender.cs");

        // Locate the single SubmitSkinnedActor call site
        int submitIndex = source.IndexOf("RigDriver.SubmitSkinnedActor", StringComparison.Ordinal);
        submitIndex.Should().BeGreaterThanOrEqualTo(0,
            "VoxelRender.cs must call RigDriver.SubmitSkinnedActor in the skeletal path");

        // Verify exactly one call site (the gated one). If a second appears,
        // the test must be reviewed to confirm gating for the new site too.
        int secondIndex = source.IndexOf("RigDriver.SubmitSkinnedActor", submitIndex + 1, StringComparison.Ordinal);
        secondIndex.Should().Be(-1,
            "RigDriver.SubmitSkinnedActor must have exactly one call site in VoxelRender.cs; " +
            "a second call would require re-verifying the SkeletalAnimation gate covers it");

        // Find the nearest preceding SkeletalAnimation gate. It must exist
        // in a reasonable window before the submit call (the surrounding if).
        int sourceBeforeSubmit_start = Math.Max(0, submitIndex - 4000);
        string windowBeforeSubmit = source.Substring(sourceBeforeSubmit_start, submitIndex - sourceBeforeSubmit_start);

        int gateIndex = windowBeforeSubmit.LastIndexOf(
            "Core.savedSettings.SkeletalAnimation", StringComparison.Ordinal);
        gateIndex.Should().BeGreaterThanOrEqualTo(0,
            "RigDriver.SubmitSkinnedActor call must be preceded by a " +
            "Core.savedSettings.SkeletalAnimation gate in the same scope");

        // The gate must be inside an `if (...)` that references SkeletalAnimation
        // — i.e. the line containing SkeletalAnimation must also contain `if`.
        int absGateIndex = sourceBeforeSubmit_start + gateIndex;
        int lineStart = source.LastIndexOf('\n', absGateIndex) + 1;
        int lineEnd = source.IndexOf('\n', absGateIndex);
        if (lineEnd < 0) lineEnd = source.Length;
        string gateLine = source.Substring(lineStart, lineEnd - lineStart);

        gateLine.Should().Contain("if",
            "the SkeletalAnimation gate must be an `if` statement that branches around " +
            "the skeletal path (line was: " + gateLine.Trim() + ")");
        gateLine.Should().Contain("Core.savedSettings.SkeletalAnimation",
            "the gate must reference Core.savedSettings.SkeletalAnimation directly");
    }

    static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
