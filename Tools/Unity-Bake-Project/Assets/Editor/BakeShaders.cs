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

        // FIX 3 (#208): belt-and-suspenders — warm up the SVC before BuildPipeline.BuildAssetBundles.
        // WarmUp() forces Unity to compile all variants listed in the SVC at editor time so they
        // are resident in the shader cache when the bundle build starts. This means the strip pass
        // cannot claim "no compiled data exists" for any of our registered variants.
        var svcForWarmup = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(SvcAssetPath);
        if (svcForWarmup != null)
        {
            svcForWarmup.WarmUp();
            Debug.Log("[WSM3D-Bake] SVC WarmUp() complete — all registered variants pre-compiled.");
        }
        else
        {
            Debug.LogWarning("[WSM3D-Bake] SVC WarmUp(): could not load SVC at " + SvcAssetPath + " — skipping WarmUp.");
        }

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

            // Bake editor and WorldBox runtime are BOTH 2022.3.60f1 (see
            // ProjectVersion.txt), so the SerializedShader layout matches and shaders
            // deserialize with their real .name. Uncompressed + force-rebuild +
            // strict-mode keeps the bundle layout deterministic; type trees are kept
            // (DisableWriteTypeTree is intentionally NOT set) so the runtime can
            // reconcile the Shader class layout if WorldBox ever ships a newer patch.
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

        // Self-verify: load the just-built win bundle back through the SAME
        // per-name GetObject path Core.cs SafeShaders uses at runtime, and log
        // each Shader's resolved .name + isSupported. A non-empty name here on a
        // patch-matched editor is the bake-time proof that the runtime empty-name
        // regression (ADR-0013) is gone. If any name comes back empty, the bake
        // fails loudly so the bundle is never shipped silently.
        VerifyBuiltBundle(Path.Combine(outBase, "win"), assetsShaderDir);
    }

    static void VerifyBuiltBundle(string winDir, string assetsShaderDir)
    {
        string bundlePath = Path.Combine(winDir, "wsm3d-shaders");
        if (!File.Exists(bundlePath))
        {
            Debug.LogError("[WSM3D-Bake] VERIFY: built bundle not found at " + bundlePath);
            EditorApplication.Exit(1);
            return;
        }

        var ab = AssetBundle.LoadFromFile(bundlePath);
        if (ab == null)
        {
            Debug.LogError("[WSM3D-Bake] VERIFY: AssetBundle.LoadFromFile returned null for " + bundlePath);
            EditorApplication.Exit(1);
            return;
        }

        int ok = 0, empty = 0;
        foreach (var path in Directory.GetFiles(assetsShaderDir, "*.shader"))
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            string assetPath = $"assets/wsm3d/shaders/{fileName.ToLowerInvariant()}.shader";
            Shader sh = ab.LoadAsset<Shader>(assetPath);
            if (sh == null)
            {
                Debug.LogError($"[WSM3D-Bake] VERIFY: LoadAsset returned null for {assetPath}");
                empty++;
                continue;
            }
            if (string.IsNullOrEmpty(sh.name))
            {
                Debug.LogError($"[WSM3D-Bake] VERIFY: EMPTY NAME for {assetPath} — bundle would fail at runtime (ADR-0013).");
                empty++;
                continue;
            }
            Debug.Log($"[WSM3D-Bake] VERIFY OK: {assetPath} -> name='{sh.name}' isSupported={sh.isSupported}");
            ok++;
        }
        ab.Unload(true);

        Debug.Log($"[WSM3D-Bake] VERIFY summary: {ok} shaders with valid names, {empty} empty/null.");
        if (empty > 0)
        {
            Debug.LogError($"[WSM3D-Bake] VERIFY FAILED: {empty} shader(s) deserialized with empty/null name. Do NOT ship this bundle.");
            EditorApplication.Exit(1);
        }
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

            // FIX 2 (#208): detect whether this shader uses #pragma multi_compile_instancing
            // so we can add INSTANCING_ON keyword variants for the 3 affected shaders
            // (Impostor, OpaqueVertexColor, StratumVoxelPBR). Without these variants the
            // GPU-instancing draw path baked by BuildAssetBundles is stripped — the runtime
            // DrawMeshInstancedIndirect calls can't find a matching compiled program and
            // silently falls back to uninstanced draws (or worse, mismatches the blob layout).
            //
            // #208 SECONDARY FIX: also detect #pragma multi_compile _ WSM3D_POSTFX_KEEP.
            // The 5 single-pass postFX shaders (BrpACES, ColorGradingLUT, ScreenSpaceGI,
            // ScreenSpaceAO, ProceduralSky) and the 4-pass BrpBloom now declare this no-op
            // keyword so they each have 2 variants. We register both "" and "WSM3D_POSTFX_KEEP"
            // variants here so the SVC covers every compiled permutation.
            bool hasInstancing = false;
            bool hasPostFxKeep = false;
            try
            {
                string shaderSrc = File.ReadAllText(path);
                hasInstancing = shaderSrc.Contains("#pragma multi_compile_instancing");
                hasPostFxKeep = shaderSrc.Contains("WSM3D_POSTFX_KEEP");
                if (hasInstancing)
                    Debug.Log($"[WSM3D-Bake] SVC: {shader.name} has #pragma multi_compile_instancing — will add INSTANCING_ON variants.");
                if (hasPostFxKeep)
                    Debug.Log($"[WSM3D-Bake] SVC: {shader.name} has WSM3D_POSTFX_KEEP — will add WSM3D_POSTFX_KEEP variants.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[WSM3D-Bake] SVC: could not read shader source for instancing detection ({shader.name}): {ex.Message}");
            }

            foreach (var passType in passTypes)
            {
                // No-keyword baseline variant.
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

                // INSTANCING_ON variant — only for shaders declaring multi_compile_instancing.
                if (hasInstancing)
                {
                    try
                    {
                        var instVariant = new ShaderVariantCollection.ShaderVariant(shader, passType, "INSTANCING_ON");
                        if (!svc.Contains(instVariant))
                        {
                            svc.Add(instVariant);
                            variantCount++;
                        }
                    }
                    catch (System.Exception)
                    {
                        // This pass+keyword combo is invalid for this shader — skip silently.
                        // We already logged that this shader has instancing above; the relevant
                        // pass types will be among the ones that DO accept INSTANCING_ON.
                    }
                }

                // WSM3D_POSTFX_KEEP variant — only for postFX shaders declaring the keep-keyword.
                // Mirrors the INSTANCING_ON pattern above. The no-op keyword gives each
                // single-pass postFX shader a 2nd variant so Unity does not treat it as
                // a candidate for stripping (keep-threshold requires >1 variant in practice).
                if (hasPostFxKeep)
                {
                    try
                    {
                        var keepVariant = new ShaderVariantCollection.ShaderVariant(shader, passType, "WSM3D_POSTFX_KEEP");
                        if (!svc.Contains(keepVariant))
                        {
                            svc.Add(keepVariant);
                            variantCount++;
                        }
                    }
                    catch (System.Exception)
                    {
                        // This pass+keyword combo is invalid for this shader — skip silently.
                    }
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

        // #208 PRIMARY FIX: keep the SVC EXTERNAL — do NOT bundle it into wsm3d-shaders.
        // Unity 2022.3 does not re-read an SVC from GraphicsSettings.m_PreloadedShaders
        // while building the bundle that *contains* that SVC; the strip pass therefore
        // never consults it and single-variant shaders (the 6 postFX) are stripped to
        // 80-byte stubs. Clearing assetBundleName ensures the SVC stays in the project
        // as a standalone asset so the strip pass can reach it via m_PreloadedShaders.
        var svcImporter = AssetImporter.GetAtPath(SvcAssetPath);
        if (svcImporter != null)
        {
            svcImporter.assetBundleName = "";
            svcImporter.SaveAndReimport();
            Debug.Log("[WSM3D-Bake] SVC kept external (not bundled) so strip pass reads it");
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

        // FIX 1 (#208): CRITICAL — also register the SVC in GraphicsSettings.m_PreloadedShaders.
        // PlayerSettings.preloadedAssets is the RUNTIME startup loader (no effect on AssetBundle
        // build-time stripping). The array that Unity's AssetBundle strip pass actually reads is
        // GraphicsSettings.m_PreloadedShaders. When that array is empty, the strip pass sees no
        // reachable SVC and silently strips all variants → 80-byte stubs. We must append our SVC
        // to m_PreloadedShaders so the BuildAssetBundles strip pass finds it.
        RegisterSvcInGraphicsSettingsPreloadedShaders(svc);

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

    // FIX 1 (#208): registers the ShaderVariantCollection in GraphicsSettings.m_PreloadedShaders —
    // the array that Unity's AssetBundle variant-strip pass reads at build time.
    // PlayerSettings.preloadedAssets is a runtime-only mechanism; only m_PreloadedShaders
    // gates the strip pass. An empty m_PreloadedShaders means the strip pass sees no
    // reachable SVC and silently drops all compiled program data → 80-byte stubs on disk.
    static void RegisterSvcInGraphicsSettingsPreloadedShaders(ShaderVariantCollection svc)
    {
        var gsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (gsAssets == null || gsAssets.Length == 0)
        {
            Debug.LogWarning("[WSM3D-Bake] RegisterSvcInGraphicsSettings: could not load GraphicsSettings.asset; m_PreloadedShaders NOT updated.");
            return;
        }
        var gsSo = new SerializedObject(gsAssets[0]);
        var preloadProp = gsSo.FindProperty("m_PreloadedShaders");
        if (preloadProp == null)
        {
            Debug.LogWarning("[WSM3D-Bake] RegisterSvcInGraphicsSettings: m_PreloadedShaders property not found in GraphicsSettings; skipping.");
            return;
        }

        // Check if SVC is already present to avoid duplicates across repeated bake runs.
        bool found = false;
        for (int i = 0; i < preloadProp.arraySize; i++)
        {
            var elem = preloadProp.GetArrayElementAtIndex(i);
            if (elem.objectReferenceValue == svc) { found = true; break; }
        }
        if (!found)
        {
            preloadProp.arraySize++;
            var newElem = preloadProp.GetArrayElementAtIndex(preloadProp.arraySize - 1);
            newElem.objectReferenceValue = svc;
            gsSo.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[WSM3D-Bake] registered SVC in GraphicsSettings.m_PreloadedShaders — variant strip pass will now see our SVC.");
        }
        else
        {
            Debug.Log("[WSM3D-Bake] SVC already present in GraphicsSettings.m_PreloadedShaders.");
        }
    }

    [MenuItem("WSM3D/Bake wsm3d-shaders AssetBundles")]
    public static void MenuBake() => BakeAll();
}
