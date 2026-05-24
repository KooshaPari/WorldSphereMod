using System;
using System.IO;
using Xunit;
using FluentAssertions;

public class CloudCrossedQuadInvariantsTests
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

    private static string ExtractOnFinishMethodBody(string patchSource)
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

    [Fact]
    public void Constants_fx_cloud_uses_EmitCrossedQuad()
    {
        var constants = ReadSourceFile("WorldSphereMod/Code/Constants.cs");
        constants.Should().Contain(
            "{\"fx_cloud\", new EffectData(false, true, 21, false, emitCrossedQuad: true) }",
            "fx_cloud must opt into the crossed-quad cloud path via EffectData.EmitCrossedQuad");
    }

    [Fact]
    public void CloudCrossedQuadRender_submits_through_MeshInstanceBatcher_with_foliage_material()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Fx/CloudCrossedQuadRender.cs");

        source.Should().Contain("CrossedQuadMeshCache.GetOrBuild(sprite, BuildingShape.CrossedQuad",
            "cloud mesh must come from the shared crossed-quad cache");
        source.Should().Contain("FoliageMaterial.EnsureMaterial()",
            "cloud path must use the foliage material gate");
        source.Should().Contain("MeshInstanceBatcher.Submit(state.Mesh, material, trs, tint)",
            "cloud path must submit each frame through the instancing batcher");

        var tryStart = ExtractMethodBody(source, "public static bool TryStart(BaseEffect effect, EffectData data)");
        tryStart.Should().Contain("SuppressSprite(effect, out bool spriteWasEnabled)",
            "successful cloud start must hide the upstream billboard");

        var update = ExtractMethodBody(source, "public static void Update(BaseEffect effect)");
        update.Should().Contain("MeshInstanceBatcher.Submit(state.Mesh, material, trs, tint)",
            "per-frame cloud update must resubmit the crossed-quad mesh");
    }

    [Fact]
    public void Effects_cs_wires_cloud_crossed_quad_emit_update_and_skips_separate_sprite()
    {
        var effects = ReadSourceFile("WorldSphereMod/Code/Effects.cs");

        var seperateSprite = ExtractMethodBody(effects, "public static void SeperateSprite(BaseEffect __result)");
        seperateSprite.Should().Contain("if (data.EmitCrossedQuad)",
            "GetObject postfix must skip sprite separation for crossed-quad clouds");
        seperateSprite.Should().Contain("CloudCrossedQuadRender.TryStart(__result, data)",
            "cloud spawn must start crossed-quad rendering from GetObject");

        var setEffect3D = ExtractMethodBody(effects, "public static void SetEffect3D(BaseEffect Effect, EffectData Data)");
        setEffect3D.Should().Contain("CloudCrossedQuadRender.TryStart(Effect, Data)",
            "3D effect setup must emit crossed-quad clouds");

        var updateEffect = ExtractMethodBody(effects, "public static void UpdateEffect(BaseEffect Effect)");
        updateEffect.Should().Contain("CloudCrossedQuadRender.Update(Effect)",
            "per-frame effect update must drive crossed-quad cloud submission");

        var updateCloud = ExtractMethodBody(effects, "public static void UpdateCloud(Cloud __instance)");
        updateCloud.Should().Contain("EffectManager.UpdateEffect(__instance)",
            "Cloud.update must route through UpdateEffect for crossed-quad refresh");

        var destroyEffect = ExtractMethodBody(effects, "public static void DestroyEffect(BaseEffect Effect)");
        destroyEffect.Should().Contain("CloudCrossedQuadRender.Clear(Effect)",
            "effect teardown must drop per-instance cloud state");
    }

    [Fact]
    public void WorldUnloadPatch_OnFinish_clears_cloud_crossed_quad_state()
    {
        var patch = ReadSourceFile("WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs");
        var onFinish = ExtractOnFinishMethodBody(patch);

        onFinish.Should().Contain("WorldSphereMod.Fx.CloudCrossedQuadRender.Clear()",
            "world unload must drop transient cloud crossed-quad state");
    }
}
