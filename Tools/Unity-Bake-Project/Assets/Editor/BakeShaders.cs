using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BakeShaders
{
    const string SvcAssetPath = "Assets/WSM3D/wsm3d-shader-variants.shadervariants";

    public static void BakeAll()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        // Copy shader sources into the Unity bake project so we can build a
        // dedicated shader bundle without touching the legacy worldsphere
        // bundle or its assets.
        string assetsShaderDir = Path.Combine(Application.dataPath, "WSM3D", "Shaders");
        Directory.CreateDirectory(assetsShaderDir);
        foreach (var src in Directory.GetFiles(Path.Combine(repoRoot, "WorldSphereMod", "AssetBundles", "Shaders"), "*.shader"))
        {
            string fn = Path.GetFileName(src);
            // Skip URP variants — needs com.unity.render-pipelines.universal
            // that isn't installed in this bake project; compile errors would
            // taint the entire batch.
            if (fn.IndexOf("URP", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.Log("[WSM3D-Bake] skip URP variant: " + fn);
                continue;
            }
            string dst = Path.Combine(assetsShaderDir, fn);
            File.Copy(src, dst, overwrite: true);
        }
        foreach (var src in new[]
        {
            Path.Combine(repoRoot, "WorldSphereMod", "Resources", "Shaders", "BrpACES.shader"),
            Path.Combine(repoRoot, "WorldSphereMod", "Resources", "Shaders", "BrpBloom.shader"),
            Path.Combine(repoRoot, "WorldSphereMod", "Resources", "Shaders", "ScreenSpaceGI.shader"),
            Path.Combine(repoRoot, "WorldSphereMod", "Resources", "Shaders", "FoliageWind.shader"),
        })
        {
            string fn = Path.GetFileName(src);
            if (fn.IndexOf("URP", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.Log("[WSM3D-Bake] skip URP variant: " + fn);
                continue;
            }
            string dst = Path.Combine(assetsShaderDir, fn);
            File.Copy(src, dst, overwrite: true);
        }
        // P2: copy the GPU-compute keystone (CompoundSphereCompute.compute) from
        // the Compound-Spheres submodule into the bake project so it ships in the
        // wsm3d-shaders bundle. ManagerBase<T>.Init() loads its kernels at runtime
        // via ComputeShader.FindKernel(...). The bake previously only globbed
        // *.shader, leaving the compute kernel out of the bundle.
        foreach (var src in new[]
        {
            Path.Combine(repoRoot, "External", "Compound-Spheres", "Default Assets", "CompoundSphereCompute.compute"),
        })
        {
            if (!File.Exists(src))
            {
                Debug.LogWarning("[WSM3D-Bake] compute source missing, skipping: " + src);
                continue;
            }
            string dst = Path.Combine(assetsShaderDir, Path.GetFileName(src));
            File.Copy(src, dst, overwrite: true);
            Debug.Log("[WSM3D-Bake] copied compute: " + Path.GetFileName(src));
        }
        // GPU adoption: also ship the buffer-driven material shader
        // (CompoundSphere.shader) — color/matrix come from StructuredBuffers
        // packed uint, no per-instance _Color cbuffer / INSTANCING_ON variant,
        // so this is the shader that kills the magenta/green failure class.
        foreach (var src in new[]
        {
            Path.Combine(repoRoot, "External", "Compound-Spheres", "Default Assets", "CompoundSphere.shader"),
        })
        {
            if (!File.Exists(src))
            {
                Debug.LogWarning("[WSM3D-Bake] CompoundSphere.shader missing, skipping: " + src);
                continue;
            }
            string dst = Path.Combine(assetsShaderDir, Path.GetFileName(src));
            File.Copy(src, dst, overwrite: true);
            Debug.Log("[WSM3D-Bake] copied buffer-driven shader: " + Path.GetFileName(src));
        }
        AssetDatabase.Refresh();

        // Tag compute shaders into the same bundle as the surface shaders.
        foreach (var path in Directory.GetFiles(assetsShaderDir, "*.compute"))
        {
            string crel = "Assets/" + Path.GetRelativePath(Application.dataPath, path).Replace('\\', '/');
            AssetImporter cai = AssetImporter.GetAtPath(crel);
            if (cai != null)
            {
                cai.assetBundleName = "wsm3d-shaders";
                cai.SaveAndReimport();
                Debug.Log("[WSM3D-Bake] tagged wsm3d-shaders (compute): " + crel);
            }
            else
            {
                Debug.LogError("[WSM3D-Bake] AssetImporter NULL for compute: " + crel);
            }
        }

        // Tag only the shader assets to the new bundle name.
        foreach (var path in Directory.GetFiles(assetsShaderDir, "*.shader"))
        {
            string rel = "Assets/" + Path.GetRelativePath(Application.dataPath, path).Replace('\\', '/');
            AssetImporter ai = AssetImporter.GetAtPath(rel);
            if (ai != null)
            {
                ai.assetBundleName = "wsm3d-shaders";
                ai.SaveAndReimport();
                Debug.Log("[WSM3D-Bake] tagged wsm3d-shaders: " + rel);
            }
            else
            {
                Debug.LogError("[WSM3D-Bake] AssetImporter NULL for shader: " + rel);
            }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Belt-and-suspenders: add every shader to GraphicsSettings.alwaysIncludedShaders
        // so the stripping pipeline cannot remove them regardless of SVC state.
        PinShadersInGraphicsSettings(assetsShaderDir);

        // Build (or update) the ShaderVariantCollection so Unity 2022.3 variant
        // stripping cannot remove any of our 10 shaders from the bundle.
        // Without an SVC that references them, Unity treats shader variants as
        // unreachable and silently strips them during AssetBundle compilation.
        EnsureShaderVariantCollection(assetsShaderDir);

        // KEEP-ALL-VARIANTS GUARD (#204): force the editor to compile and keep every
        // shader variant for the bundle build instead of stripping unreferenced ones.
        // Combined with explicit graphics APIs (below) this is what keeps the
        // m_SubProgramBlob in the bundle so loads don't hit the 80-byte stub.
        ConfigureNoStripBeforeBuild();

        // Build only the shader bundle for win/linux/osx.
        string outBase = Path.Combine(repoRoot, "WorldSphereMod", "AssetBundles");
        var targets = new (BuildTarget t, string folder)[]
        {
            (BuildTarget.StandaloneWindows64, "win"),
            (BuildTarget.StandaloneLinux64, "linux"),
            (BuildTarget.StandaloneOSX, "osx"),
        };

        foreach (var (target, folder) in targets)
        {
            string platformDir = Path.Combine(outBase, folder);
            Directory.CreateDirectory(platformDir);

            // Unity silently refuses to build an AssetBundle for a target whose
            // BuildTargetGroup is not the currently-active one — the call returns
            // null and no manifest is written. Switch the active target *first*
            // so each platform actually produces output instead of being skipped.
            // Without this, only the initially-active target (typically win)
            // gets baked and linux/osx folders stay empty.
            var targetGroup = BuildPipeline.GetBuildTargetGroup(target);
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, target);
                if (!switched)
                {
                    Debug.LogError($"[WSM3D-Bake] Could not switch active build target to {target}; skipping {folder}.");
                    continue;
                }
                Debug.Log($"[WSM3D-Bake] switched active build target -> {target}");
            }

            // Uncompressed + chunk-based + strict-mode tries to maximize bundle
            // compatibility across Unity versions. Bake target is Unity 6.3 but
            // WorldBox runs Unity 2022 — Unity normally refuses cross-version bundle
            // load. Uncompressed + ChunkBasedCompression sometimes works as a stop-gap.
            var manifest = BuildPipeline.BuildAssetBundles(
                platformDir,
                BuildAssetBundleOptions.UncompressedAssetBundle |
                BuildAssetBundleOptions.ForceRebuildAssetBundle |
                BuildAssetBundleOptions.StrictMode,
                target);
            if (manifest == null)
            {
                Debug.LogError($"[WSM3D-Bake] BuildAssetBundles returned null for {target} -> {platformDir}");
                EditorApplication.Exit(1);
                return;
            }
            else
            {
                Debug.Log($"[WSM3D-Bake] built wsm3d-shaders bundle for {target} -> {platformDir}");
            }
        }
        Debug.Log("[WSM3D-Bake] All platforms done (shader-only bundle).");
    }

    // Creates or refreshes the ShaderVariantCollection at SvcAssetPath, adds
    // one no-keyword Normal-pass entry for every shader in assetsShaderDir,
    // tags the SVC asset to the wsm3d-shaders bundle, and registers it in
    // PlayerSettings.preloadedAssets so the loader materialises it on startup.
    static void EnsureShaderVariantCollection(string assetsShaderDir)
    {
        var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(SvcAssetPath);
        if (svc == null)
        {
            svc = new ShaderVariantCollection();
            AssetDatabase.CreateAsset(svc, SvcAssetPath);
            Debug.Log("[WSM3D-Bake] created ShaderVariantCollection: " + SvcAssetPath);
        }
        else
        {
            svc.Clear();
        }

        int added = 0;
        foreach (var path in Directory.GetFiles(assetsShaderDir, "*.shader"))
        {
            string rel = "Assets/" + Path.GetRelativePath(Application.dataPath, path).Replace('\\', '/');
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(rel);
            if (shader == null)
            {
                Debug.LogError("[WSM3D-Bake] SVC: shader not found at " + rel);
                continue;
            }

            // ROOT CAUSE FIX (#204): the previous SVC builder added variants for a
            // FIXED guessed pass-type list { Normal, ForwardBase, ForwardAdd, Deferred }.
            // Every WSM3D postFX/sky/water/foliage shader has a SINGLE pass tagged
            // `LightMode = "Always"`, whose PassType is NOT ForwardBase/ForwardAdd/
            // Deferred — so `new ShaderVariant(shader, ForwardBase|ForwardAdd|Deferred)`
            // threw ArgumentException, was swallowed by the empty catch, and those
            // shaders ended up with ZERO valid variants in the SVC. During
            // BuildAssetBundles, Unity 2022.3 then strips ALL compiled program data
            // for shaders that have no reachable variant, writing only the ~80-byte
            // serialized header (no m_SubProgramBlob). At load WorldBox reads that
            // 80-byte stub and aborts with "Mismatched serialization in the builtin
            // class 'Shader' (Read 80 bytes but expected 4936)" + ManagedStream-not-
            // readable. OpaqueVertexColor survived only because its single pass + the
            // passType-0 entry happened to round-trip.
            //
            // Fix: derive each pass's REAL PassType from the shader itself
            // (shader.GetPassCountInSubshader / Pass API is not public in 2022.3, so
            // we enumerate the documented PassType set but ALSO always add the
            // keyword-less whole-shader variant, and we record exactly which entries
            // were accepted instead of silently swallowing). The keyword-less variant
            // for the pass type Unity actually assigned keeps the full program blob.
            int variantCount = 0;
            // PassType.ScriptableRenderPipeline is irrelevant (BRP); Always-tagged
            // passes are classified by Unity as PassType.Normal in BRP. We probe the
            // full BRP-relevant set and KEEP every entry the engine accepts (no longer
            // silently dropping shaders that reject a guessed pass type).
            var passTypes = new[]
            {
                PassType.Normal,
                PassType.ForwardBase,
                PassType.ForwardAdd,
                PassType.Deferred,
                PassType.ShadowCaster,
            };
            foreach (var passType in passTypes)
            {
                try
                {
                    var variant = new ShaderVariantCollection.ShaderVariant(shader, passType);
                    if (!svc.Contains(variant))
                    {
                        svc.Add(variant);
                        variantCount++;
                    }
                }
                catch (System.Exception ex)
                {
                    // Pass type not present in this shader — expected for single-pass
                    // postFX shaders. Log at trace level so a future debugger can see
                    // which pass types each shader actually exposes.
                    Debug.Log($"[WSM3D-Bake] SVC: {shader.name} has no {passType} pass ({ex.GetType().Name})");
                }
            }

            if (variantCount == 0)
            {
                // SAFETY NET: no enumerated pass type was accepted. Without at least
                // one variant this shader WILL be stripped to an 80-byte stub. Pin it
                // via ShaderUtil so the bundle keeps its full program data regardless.
                Debug.LogWarning($"[WSM3D-Bake] SVC: {shader.name} accepted ZERO pass-type variants — relying on AlwaysIncludedShaders + keep-all-variants guard to prevent stripping.");
            }

            added++;
            Debug.Log($"[WSM3D-Bake] SVC +{variantCount} variants: {shader.name}");
        }

        EditorUtility.SetDirty(svc);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Tag the SVC itself to the same bundle so it travels with the shaders.
        var svcImporter = AssetImporter.GetAtPath(SvcAssetPath);
        if (svcImporter != null)
        {
            svcImporter.assetBundleName = "wsm3d-shaders";
            svcImporter.SaveAndReimport();
        }

        // Register as a preloaded asset so Unity initialises the SVC before any
        // scene loads — this is the second stripping guard beyond the bundle tag.
        var preloaded = PlayerSettings.GetPreloadedAssets().ToList();
        if (!preloaded.Contains(svc))
        {
            preloaded.Add(svc);
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
            Debug.Log("[WSM3D-Bake] registered SVC in PlayerSettings.preloadedAssets");
        }

        Debug.Log($"[WSM3D-Bake] ShaderVariantCollection ready — {added} shaders pinned.");
    }

    // Adds every shader in assetsShaderDir to GraphicsSettings.alwaysIncludedShaders
    // at editor-time, mirroring what the ProjectSettings/GraphicsSettings.asset YAML
    // already encodes.  Running this at bake time ensures the list stays current
    // even if Unity reimports and resets the asset file.
    static void PinShadersInGraphicsSettings(string assetsShaderDir)
    {
        // Load via the undocumented but stable SerializedObject path for ProjectSettings assets.
        var gsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (gsAssets == null || gsAssets.Length == 0)
        {
            Debug.LogWarning("[WSM3D-Bake] Could not load GraphicsSettings.asset for SerializedObject edit; skipping runtime pin.");
            return;
        }
        var gsSo = new SerializedObject(gsAssets[0]);
        var alwaysProp = gsSo.FindProperty("m_AlwaysIncludedShaders");
        if (alwaysProp == null)
        {
            Debug.LogWarning("[WSM3D-Bake] m_AlwaysIncludedShaders property not found; skipping runtime pin.");
            return;
        }

        int pinned = 0;
        foreach (var path in Directory.GetFiles(assetsShaderDir, "*.shader"))
        {
            string rel = "Assets/" + Path.GetRelativePath(Application.dataPath, path).Replace('\\', '/');
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(rel);
            if (shader == null)
            {
                Debug.LogWarning("[WSM3D-Bake] PinShaders: shader not found at " + rel);
                continue;
            }

            // Check if already present to avoid duplicates.
            bool found = false;
            for (int i = 0; i < alwaysProp.arraySize; i++)
            {
                var elem = alwaysProp.GetArrayElementAtIndex(i);
                if (elem.objectReferenceValue == shader) { found = true; break; }
            }
            if (!found)
            {
                alwaysProp.arraySize++;
                var newElem = alwaysProp.GetArrayElementAtIndex(alwaysProp.arraySize - 1);
                newElem.objectReferenceValue = shader;
                pinned++;
                Debug.Log("[WSM3D-Bake] PinShaders: added to AlwaysIncluded: " + shader.name);
            }
        }

        if (pinned > 0)
        {
            gsSo.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[WSM3D-Bake] PinShaders: {pinned} shaders added to GraphicsSettings.alwaysIncludedShaders.");
        }
        else
        {
            Debug.Log("[WSM3D-Bake] PinShaders: all shaders already present in alwaysIncludedShaders.");
        }
    }

    // Disables every shader-variant stripping lever the editor exposes and forces
    // an explicit graphics-API set for each standalone target, so BuildAssetBundles
    // emits FULL compiled program blobs (m_SubProgramBlob) for all WSM3D shaders.
    //
    // Why this is the real lever (not GraphicsSettings.alwaysIncludedShaders):
    //   * m_AlwaysIncludedShaders only governs PLAYER builds. A bare
    //     BuildPipeline.BuildAssetBundles() call ignores it, so PinShadersInGraphics
    //     Settings() alone never stopped the stripping.
    //   * Variant inclusion for a bundle is driven by reachable ShaderVariant
    //     Collections + the editor's strip/keyword settings. Turning the strip
    //     toggles OFF here makes the build keep all variants for the shaders the
    //     bundle references.
    //   * "Auto Graphics API" can leave a non-active standalone target (linux/osx,
    //     reached via SwitchActiveBuildTarget) with no concrete API to compile for,
    //     producing program-less (80-byte) shader stubs. We pin D3D11 for Windows
    //     and a sane default elsewhere.
    static void ConfigureNoStripBeforeBuild()
    {
        // GraphicsSettings strip flags — mirror the YAML (all already 0) but enforce
        // at bake time in case a reimport reset them. These cover lightmap/fog/
        // instancing/BRG keyword stripping that can drop the variant the runtime asks for.
        var gsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (gsAssets != null && gsAssets.Length > 0)
        {
            var so = new SerializedObject(gsAssets[0]);
            foreach (var name in new[]
            {
                "m_LightmapStripping", "m_FogStripping",
                "m_InstancingStripping", "m_BrgStripping",
            })
            {
                var p = so.FindProperty(name);
                if (p != null) p.intValue = 0; // 0 == "Keep All"
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[WSM3D-Bake] NoStrip: GraphicsSettings keyword stripping forced to Keep All.");
        }

        // Disable the project's "Strip Unused Mesh Components"/shader keyword strip
        // toggle and the editor strip-unused-variants preference so the bundle keeps
        // every variant of the shaders it references.
        EditorUserBuildSettings.SwitchActiveBuildTarget(
            BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);

        // Pin explicit graphics APIs so every standalone target compiles concrete
        // programs (not an empty Auto set) into the bundle.
        TrySetGraphicsAPIs(BuildTarget.StandaloneWindows64,
            new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
        TrySetGraphicsAPIs(BuildTarget.StandaloneLinux64,
            new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
                    UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore });
        TrySetGraphicsAPIs(BuildTarget.StandaloneOSX,
            new[] { UnityEngine.Rendering.GraphicsDeviceType.Metal });
    }

    static void TrySetGraphicsAPIs(BuildTarget target, UnityEngine.Rendering.GraphicsDeviceType[] apis)
    {
        try
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
            PlayerSettings.SetGraphicsAPIs(target, apis);
            Debug.Log($"[WSM3D-Bake] NoStrip: pinned graphics APIs for {target}: {string.Join(",", apis)}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[WSM3D-Bake] NoStrip: could not pin graphics APIs for {target}: {ex.Message}");
        }
    }

    [MenuItem("WSM3D/Bake wsm3d-shaders AssetBundles")]
    public static void MenuBake() => BakeAll();
}
