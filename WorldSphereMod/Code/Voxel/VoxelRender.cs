using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using WorldSphereMod.Textures;
using WorldSphereMod.NewCamera;
using Debug = UnityEngine.Debug;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// Phase 1 wiring. Sits on top of <see cref="VoxelMeshCache"/> + <see cref="MeshInstanceBatcher"/>
    /// and provides the two integration points the rest of the mod needs:
    ///   • <see cref="EnsureMaterial"/> — lazy material resolution (no shader asset shipped yet).
    ///   • <see cref="Submit"/> / <see cref="Flush"/> — the per-frame submission API.
    ///
    /// The actual per-actor / per-building submission happens in this file's Harmony
    /// patches as a Postfix on the existing render-data calculation passes. They run
    /// AFTER the upstream Prefix has populated <c>render_data</c>, then walk it,
    /// emit voxel meshes, and finally suppress the upstream sprite render by clearing
    /// <c>has_normal_render</c> for the actors we drew in 3D.
    ///
    /// Gated behind <see cref="SavedSettings.VoxelEntities"/>. Default off during
    /// alpha — flip in the in-game settings tab once a tester confirms it renders.
    /// </summary>
    public static class VoxelRender
    {
        const float BuildingMaxScale = 3.0f;
        static Material? _material;
        static bool _materialAttempted;
        static bool _materialProbeLogged;
        static bool _materialDebugLogged;
        static bool _firstActorPosLogged;
        static int _actorVoxelColorSampleCount;
        static bool _actorVoxelDiagnosticLogged;
        static bool _actorImpostorDiagnosticLogged;
        static bool _actorSkeletalDiagnosticLogged;
        static readonly List<Vector3> _actorVoxelSubmitTranslations = new(5);

        /// <summary>
        /// Destroy the cached material and clear the resolve-attempted latch. Call when
        /// the world reloads — static fields outlive Unity's scene teardown and the
        /// underlying Material may have been invalidated.
        /// Wired from WorldUnloadPatch.OnFinish (Core.Sphere.Finish Prefix).
        /// </summary>
        public static void Reset()
        {
            if (_material != null) Object.Destroy(_material);
            _material = null;
            _materialAttempted = false;
            _materialProbeLogged = false;
            SanityTestCube.Reset();
            _materialDebugLogged = false;
            _firstActorPosLogged = false;
            _actorVoxelDiagnosticLogged = false;
            _actorVoxelColorSampleCount = 0;
            _actorImpostorDiagnosticLogged = false;
            _actorSkeletalDiagnosticLogged = false;
            _actorVoxelSubmitTranslations.Clear();
        }

        /// <summary>
        /// Resolve a material capable of rendering the voxel mesh's per-vertex colors.
        /// Walks a fallback chain of Unity built-in shaders so we don't need to ship a
        /// new shader asset in Phase 1 (Phase 5 introduces VoxelLit.shader and a real
        /// lit + shadow-casting material via the AssetBundle).
        /// </summary>
        public static bool EnsureMaterial()
        {
                if (_materialAttempted || _material != null)
                {
                    if ((MeshInstanceBatcher.UseFallbackPath || !Core.savedSettings.UseBRG) && _material != null && _material.enableInstancing)
                    {
                        _material.enableInstancing = false;
                    }
                    return _material != null;
                }
            _materialAttempted = true;

            string[] candidates =
            {
                // Particle shaders can consume Mesh COLOR output when _VERTEX_COLOR_ON
                // is enabled, so try them first for per-vertex tint fidelity.
                "Particles/Standard Surface",
                "Particles/Standard Unlit",
                // URP variants are clean opaque fallbacks and avoid legacy sprite
                // transparency ordering issues.
                "Universal Render Pipeline/Simple Lit",
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Particles/Unlit",
                // Legacy fallback path (if SRP fallback happens at runtime).
                // Sprites/Default LAST -- it produces open-box 2.5D transparent
                // rendering (single-sided faces, alpha-blended). c1abc6b promoted
                // it to first hoping to get vertex colors through; user-reported
                // regression was visible-only-front-faces. Standard back at higher
                // priority despite black-output risk since the per-instance emission
                // override (c7be9bd) + clamp (8ee4549) should mitigate.
                "Unlit/Texture",
                "Unlit/Color",
                "Standard",
            };
            var shaderLookup = new Dictionary<string, Shader>();
            foreach (var name in candidates)
            {
                Shader s = Shader.Find(name);
                shaderLookup[name] = s;
                if (!_materialProbeLogged)
                {
                    Debug.Log($"[WSM3D][MATERIAL] Shader probe: '{name}' {(s != null ? "FOUND" : "MISSING")}");
                }
            }
            _materialProbeLogged = true;
            // First try a custom inline opaque-vertex-color shader. Built-in
            // candidates that DON'T consume vertex colors (Standard) leave voxel
            // meshes gray/black; ones that DO are typically transparent
            // (Sprites/Default) — the open-box-see-through bug. This inline
            // shader is opaque AND consumes vertex colors as the only albedo.
            Material? inlineMat = TryCompileInlineVoxelShader();
                if (inlineMat != null)
                {
                    _material = inlineMat;
                    McPackLoader.ApplyToMaterial(_material);
                    Debug.Log("[WSM3D] Voxel material resolved via inline 'WSM3D/OpaqueVertexColor'.");
                    return true;
                }

            foreach (var name in candidates)
            {
                Shader? s = shaderLookup.TryGetValue(name, out Shader? resolved) ? resolved : null;
                if (s == null) continue;
                Material m = new Material(s) { name = "WSM3D.Voxel.Placeholder" };
                m.enableInstancing = true;
                if (MeshInstanceBatcher.UseFallbackPath || Core.savedSettings.UseBRG)
                {
                    m.enableInstancing = false;
                }
                // Force opaque alpha-cutout on transparent shaders (Sprites/Default
                // especially) so voxel cubes render with vertex colors but stop
                // showing all inner faces. ZWrite ON + One/Zero blend + AlphaTest
                // keyword + Cutoff = solid voxel pixels visible, transparent
                // pixels punched out, no see-through.
                try
                {
                    m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    m.SetInt("_ZWrite", 1);
                    m.DisableKeyword("_ALPHABLEND_ON");
                    m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    // CRITICAL: Standard shader's alpha-test branch samples
                    // tex2D(_MainTex, uv).a × _Color.a. _MainTex is NOT set on
                    // this material → tex2D returns alpha=0 → every fragment
                    // fails the Cutoff=0.5 test → 100% invisible.
                    // Now that renderQueue is Geometry+1 (opaque pass) we don't
                    // need AlphaTest — disable the keyword so fragments aren't
                    // discarded for lacking a _MainTex they don't need.
                    m.DisableKeyword("_ALPHATEST_ON");
                    m.SetFloat("_Cutoff", 0.0f);
                    // Opaque-Geometry + 1 (queue 2001) instead of AlphaTest (2450)
                    // so voxel meshes render in the OPAQUE pass right after terrain
                    // (queue 2000). At AlphaTest queue we were rendering AFTER all
                    // transparent passes — terrain wasn't covering us but the depth
                    // buffer post-pass interactions made meshes invisible at this
                    // camera altitude. Geometry+1 = same opaque pass, just sorted
                    // after terrain so we don't z-fight ties with biome cubes.
                    m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 1;
                }
                catch { /* shader doesn't have these props — fine */ }
                // Belt+suspenders: always set _MainTex to a 1x1 white texture and
                // _Color to white so ANY shader that samples _MainTex.a in its
                // alpha-test / opaque path gets alpha=1 (passes any cutoff). Without
                // this, Standard (and possibly URP Lit) shaders default _MainTex to
                // null → tex2D returns alpha=0 → entire mesh discarded.
                try
                {
                    m.SetTexture("_MainTex", UnityEngine.Texture2D.whiteTexture);
                    m.SetColor("_Color", UnityEngine.Color.white);
                    m.SetTexture("_BaseMap", UnityEngine.Texture2D.whiteTexture);
                    m.SetColor("_BaseColor", UnityEngine.Color.white);
                    // FORCE EMISSION = white. Standard shader is LIT — without
                    // ambient/directional light hitting these meshes, they render
                    // BLACK regardless of _Color. Emission bypasses lighting:
                    // pixels emit the _EmissionColor value directly into the
                    // framebuffer. Combined with the _MainTex=white above,
                    // every voxel renders as pure white = visible against any
                    // background.
                    // RE-ENABLE EMISSION at 0.5 brightness. User screenshot at
                    // alpha.8 close-zoom showed actors rendering BLACK (Standard
                    // shader unlit because WorldBox scene has no directional/ambient
                    // light reaching the voxel layer). Without emission they're
                    // invisible-against-grass-tile-dark. With emission=white they
                    // override per-actor color. 0.5 grey emission is the compromise:
                    // self-emit enough light to see against grass, but leave headroom
                    // for per-instance _Color tints via MaterialPropertyBlock to
                    // actually shift the visible color.
                    m.EnableKeyword("_EMISSION");
                    // BLACK-VOXEL FIX: Standard shader is LIT; without enough scene light,
                    // every voxel pixel computes to ~0 → black. Available shaders here are
                    // only 'Standard' and 'Sprites/Default'; Particles/* and URP/* all returned
                    // MISSING from Shader.Find. Bump emission to 1.5 (super-bright) so it
                    // dominates the unlit contribution + actually makes voxels visible.
                    // Without per-vertex emission texture we can't tint emission per-pixel,
                    // but this at least lifts everything off black floor.
                    m.SetColor("_EmissionColor", new UnityEngine.Color(1.5f, 1.5f, 1.5f, 1f));
                    m.globalIlluminationFlags = UnityEngine.MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                catch { }
                ConfigureVoxelMaterial(m, name);
                ConfigureVertexColorShaderMode(m, name);
                McPackLoader.ApplyToMaterial(m);
                LogVoxelMaterialPassDetails(m, name);
                _material = m;
                Debug.Log($"[WSM3D] Voxel material resolved via '{name}'.");
                return true;
            }
            Debug.LogWarning("[WSM3D] No usable shader found; voxel renderer disabled.");
            return false;
        }


        // Attempt to construct an inline opaque vertex-color shader at runtime.
        // Returns null if Unity refuses to compile it (older Unity versions).
        static Material? TryCompileInlineVoxelShader()
        {
            try
            {
                // First check the bundle-loaded shaders cache (Shader.Find
                // doesn't see AssetBundle shaders unless they're Always-Included).
                Shader? existing = null;
                if (WorldSphereMod.Core.Sphere.LoadedShaders.TryGetValue("OpaqueVertexColor", out var bundled) && bundled != null)
                {
                    existing = bundled;
                    Debug.Log("[WSM3D] Voxel shader resolved via Core.Sphere.LoadedShaders cache.");
                }
                if (existing == null) existing = Shader.Find("WSM3D/OpaqueVertexColor");
                if (existing != null)
                {
                    Material inlineMaterial = new Material(existing) { name = "WSM3D.Voxel.OpaqueVertexColor" };
                    ConfigureVoxelMaterial(inlineMaterial, "WSM3D/OpaqueVertexColor");
                    ConfigureVertexColorShaderMode(inlineMaterial, "WSM3D/OpaqueVertexColor");
                    McPackLoader.ApplyToMaterial(inlineMaterial);
                    return inlineMaterial;
                }
                // Unity 2022 doesn't have a public runtime ShaderLab compile API.
                // The .shader source lives at WorldSphereMod/AssetBundles/Shaders/
                // OpaqueVertexColor.shader. Bake step: open Unity 2022.3 project,
                // import that .shader, build AssetBundle 'worldsphere' platform-aware.
                // Until baked, falls through to Standard + emission boost (visible).
                return null;
            }
            catch { return null; }
        }

        static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int _smoothnessId = Shader.PropertyToID("_Smoothness");
        static readonly int _metallicId = Shader.PropertyToID("_Metallic");
        static readonly int _cubemapId = Shader.PropertyToID("_Cubemap");
        static readonly int _cullId = Shader.PropertyToID("_Cull");

        /// <summary>
        /// Configure whichever URP path we selected for the current run:
        ///  - URP Simple Lit: supports per-vertex color tint path better than full Lit.
        ///  - URP Lit: keep this as fallback for stronger BRDF, but also set tint/roughness
        ///    and probe inputs where supported.
        ///  - URP Unlit/Particles: keep a pure unlit pipeline and only set base tint so
        ///    per-instance color multiplies as expected.
        /// </summary>
        static void ConfigureVoxelMaterial(Material material, string shaderName)
        {
            material.SetInt(_cullId, 0);

            bool isLit = shaderName == "Universal Render Pipeline/Lit" || shaderName == "Universal Render Pipeline/Simple Lit";
            bool isUnlit = shaderName == "Universal Render Pipeline/Unlit" ||
                           shaderName == "Universal Render Pipeline/Particles/Unlit";

            if (isLit)
            {
                material.SetColor(_baseColorId, Color.white);
                material.SetFloat(_smoothnessId, 0.2f);
                material.SetFloat(_metallicId, 0.0f);

                // Cubemap probe hookup is best-effort: set only when the active
                // scene skybox provides a true Cubemap texture directly.
                if (RenderSettings.skybox != null && RenderSettings.skybox.mainTexture is Cubemap skyCubemap)
                {
                    material.SetTexture(_cubemapId, skyCubemap);
                    Debug.Log("[WSM3D] Voxel material configured with skybox cubemap reflection probe.");
                }
                else
                {
                    Debug.Log("[WSM3D] Voxel material resolved without cubemap probe; using fallback ambient diffuse.");
                }
                return;
            }

            if (isUnlit)
            {
                // Unlit variants do not use metallic/smoothness in this phase; keep
                // base color at white so <see cref=\"_InstanceColor\"/> remains the
                // effective tint multiplier.
                material.SetColor(_baseColorId, Color.white);
                return;
            }

            // Keep non-URP fallbacks clean and deterministic: don't assume URP-lit property names.
            material.color = Color.white;
        }

        static void ConfigureVertexColorShaderMode(Material material, string shaderName)
        {
            if (material == null || material.shader == null) return;

            if (shaderName == "Particles/Standard Surface" ||
                shaderName == "Particles/Standard Unlit")
            {
                material.EnableKeyword("_VERTEX_COLOR_ON");
                return;
            }

            material.DisableKeyword("_VERTEX_COLOR_ON");
        }

        static void LogVoxelMaterialPassDetails(Material material, string shaderName)
        {
            if (_materialDebugLogged) return;
            _materialDebugLogged = true;

            if (material == null)
            {
                Debug.LogWarning("[WSM3D] Voxel material diagnostics skipped: material is null.");
                return;
            }

            string shaderNameSafe = material.shader != null ? material.shader.name : "<null shader>";
            string keywords = material.shaderKeywords != null && material.shaderKeywords.Length > 0
                ? string.Join(", ", material.shaderKeywords)
                : "<none>";

            string renderType = material.GetTag("RenderType", true, "<none>");
            string queueTag = material.GetTag("Queue", false, "<none>");
            Debug.Log($"[WSM3D][MATERIAL] VOXEL sourceCandidate='{shaderName}' resolvedShader='{shaderNameSafe}' passCount={material.passCount} renderQueue={material.renderQueue} renderType={renderType} queueOverride={queueTag}");
            Debug.Log($"[WSM3D][MATERIAL] VOXEL shaderKeywords=[{keywords}]");
            if (renderType == "<none>" || string.IsNullOrEmpty(renderType))
            {
                material.SetOverrideTag("RenderType", "Opaque");
                Debug.LogWarning($"[WSM3D][MATERIAL] VOXEL sourceCandidate='{shaderName}' missing RenderType; forced override to 'Opaque'. renderQueue={material.renderQueue} renderType={material.GetTag("RenderType", true, "<none>")}");
            }

            for (int pass = 0; pass < material.passCount; pass++)
            {
                string passName = material.GetPassName(pass);
                Debug.Log($"[WSM3D][MATERIAL] VOXEL pass[{pass}] name='{passName}'");
            }
        }

        /// <summary>Per-frame submission. Matrix should already include scale.</summary>
        public static bool Submit(Mesh mesh, Matrix4x4 trs, Color tint)
        {
            // Removed: if (InstancingBroken) return false. Once instancing throws,
            // MeshInstanceBatcher.Flush has a working Graphics.DrawMesh fallback path.
            // Pre-empting Submit here used to permanently disable voxel rendering after
            // the first instancing exception. Now we always submit; Flush picks the right path.
            if (_material == null && !EnsureMaterial()) return false;
            MeshInstanceBatcher.Submit(mesh, _material!, trs, tint);
            return true;
        }

        public static Material? GetResolvedMaterial()
        {
            return EnsureMaterial() ? _material : null;
        }

        /// <summary>Issue all batched draw calls. Call once per frame after submissions.</summary>
        public static void Flush()
        {
            if (_material == null) return;
            Camera flushCamera = ResolveFlushCamera();
            LogActorVoxelSubmitDiagnostics(flushCamera);

            if (!Core.savedSettings.ProfilerDump)
            {
                MeshInstanceBatcher.Flush();
                VoxelMeshCache.Tick();
                return;
            }

            var totalSw = Stopwatch.StartNew();
            var batchSw = Stopwatch.StartNew();
            MeshInstanceBatcher.Flush();
            batchSw.Stop();
            Debug.Log($"[WSM3D][PERF] VoxelRender.Flush.MeshInstanceBatcher={batchSw.Elapsed.TotalMilliseconds:F3}ms");

            var cacheSw = Stopwatch.StartNew();
            VoxelMeshCache.Tick();
            cacheSw.Stop();
            Debug.Log($"[WSM3D][PERF] VoxelRender.Flush.VoxelMeshCache.Tick={cacheSw.Elapsed.TotalMilliseconds:F3}ms");

            totalSw.Stop();
            Debug.Log($"[WSM3D][PERF] VoxelRender.Flush total={totalSw.Elapsed.TotalMilliseconds:F3}ms");
        }

        static void LogActorVoxelSubmitDiagnostics(Camera? camera)
        {
            if (_actorVoxelSubmitTranslations.Count == 0) return;

            Debug.Log($"[WSM3D][DIAG] Actor-voxel TRS.GetColumn(3) first {_actorVoxelSubmitTranslations.Count} submissions:");
            for (int i = 0; i < _actorVoxelSubmitTranslations.Count; i++)
            {
                Debug.Log($"[WSM3D][DIAG]  sample[{i}] trsPos={_actorVoxelSubmitTranslations[i]}");
            }

            LogCameraFrustumBounds(camera);
            _actorVoxelSubmitTranslations.Clear();
        }

        static void LogCameraFrustumBounds(Camera? cam)
        {
            if (cam == null) return;

            Vector3 nearBL = cam.ViewportToWorldPoint(new Vector3(0f, 0f, cam.nearClipPlane));
            Vector3 nearBR = cam.ViewportToWorldPoint(new Vector3(1f, 0f, cam.nearClipPlane));
            Vector3 nearTL = cam.ViewportToWorldPoint(new Vector3(0f, 1f, cam.nearClipPlane));
            Vector3 nearTR = cam.ViewportToWorldPoint(new Vector3(1f, 1f, cam.nearClipPlane));
            Vector3 farBL = cam.ViewportToWorldPoint(new Vector3(0f, 0f, cam.farClipPlane));
            Vector3 farBR = cam.ViewportToWorldPoint(new Vector3(1f, 0f, cam.farClipPlane));
            Vector3 farTL = cam.ViewportToWorldPoint(new Vector3(0f, 1f, cam.farClipPlane));
            Vector3 farTR = cam.ViewportToWorldPoint(new Vector3(1f, 1f, cam.farClipPlane));

            Vector3 min = Vector3.Min(Vector3.Min(nearBL, nearBR), Vector3.Min(nearTL, nearTR));
            min = Vector3.Min(min, Vector3.Min(farBL, farBR));
            min = Vector3.Min(min, Vector3.Min(farTL, farTR));
            Vector3 max = Vector3.Max(Vector3.Max(nearBL, nearBR), Vector3.Max(nearTL, nearTR));
            max = Vector3.Max(max, Vector3.Max(farBL, farBR));
            max = Vector3.Max(max, Vector3.Max(farTL, farTR));

            Debug.Log($"[WSM3D][DIAG] Camera frustum {cam.name}: pos={cam.transform.position} near={cam.nearClipPlane:F2} far={cam.farClipPlane:F2} fov={cam.fieldOfView:F2} aspect={cam.aspect:F4} ortho={cam.orthographic} orthoSize={cam.orthographicSize:F3} boundsMin={min} boundsMax={max}");
        }

        static Camera? ResolveFlushCamera()
        {
            if (CameraManager.MainCamera != null && CameraManager.MainCamera.enabled) return CameraManager.MainCamera;
            return Camera.main;
        }

        // ---------------------------------------------------------------------
        // Harmony hooks. Registered automatically via Patcher.PatchAll on the
        // existing Core.Patch() pass because [HarmonyPatch] is declared here.

        [Phase(nameof(SavedSettings.VoxelEntities))]
        [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.precalculateRenderDataParallel))]
        public static class ActorVoxelEmit
        {
            [HarmonyPostfix]
            public static void EmitVoxels(ActorManager __instance)
            {
                Tools.ClearTileHeightSmoothCache();
                if (!Core.IsWorld3D || !Core.savedSettings.VoxelEntities) return;
                if (!EnsureMaterial()) return;

                var rd = __instance.render_data;
                var arr = __instance.visible_units.array;
                int n = __instance.visible_units.count;
                for (int i = 0; i < n; i++)
                {
                    Actor a = arr[i];
                    if (a == null || a.asset == null) continue;
                    // Per-asset opt-out: the existing v1 API hands designers a way to
                    // mark assets as "perp" (ground-aligned billboard). Those keep
                    // sprite rendering for now — they tend to be flat decals (arrows,
                    // ground markers) where voxelization adds nothing.
                    if (Constants.PerpActors.ContainsKey(a.asset.id)) continue;
                    // GATE REMOVED (codex plate-78 diff): upstream may set has_normal_render=false
                    // for actors that should still get voxelized (e.g. all actors after the first
                    // created settlement per user observation). Buildings have no such gate;
                    // matching that here. If we DO need to skip, fix it post-voxelize.
                    // if (!rd.has_normal_render[i]) continue;

                    Vector3 cullPos = rd.positions[i];
                    if (cullPos.z < Constants.ZDisplacement * 0.5f)
                    {
                        cullPos = cullPos.To3DTileHeight(false);
                    }
                    float radius = 2f;
                    if (!WorldSphereMod.LOD.FrustumCuller.IsVisible(cullPos, radius))
                    {
                        continue;
                    }
                    WorldSphereMod.LOD.LodTier tier = WorldSphereMod.LOD.LodSelector.Select(cullPos, a.GetHashCode());

                    if (Core.savedSettings.SkeletalAnimation && tier != WorldSphereMod.LOD.LodTier.Impostor)
                    {
                        WorldSphereMod.Rig.RigType rigType = ResolveRigType(a.asset.id);
                        if (rigType != WorldSphereMod.Rig.RigType.None)
                        {
                            Vector3 skPos = rd.positions[i];
                            Vector3 skPosBeforeLift = skPos;
                            Vector3 skRot = rd.rotations[i];
                            Vector3 skScl = rd.scales[i];
                            if (rd.flip_x_states[i]) skScl.x = -skScl.x;
                            if (skPos.z < Constants.ZDisplacement * 0.5f)
                            {
                                skPos = skPos.To3DTileHeight(false);
                            }
                            // Match the ActorVoxelEmit Y-lift so skinned actors aren't
                            // embedded inside the terrain/water voxel. SubmitSkinnedActor
                            // uses skPos as the rig root position; raise it by half the
                            // expected actor height (use scl.y * VoxelScaleMultiplier as
                            // rough actor height estimate; / 2 for center→bottom shift).
                            float skHalfHeight = Mathf.Abs(skScl.y) * Core.savedSettings.VoxelScaleMultiplier * 0.5f;
                            skPos.y += skHalfHeight;
                            LogActorSubmitDiagnostic("skeletal", ref _actorSkeletalDiagnosticLogged, a, rd.main_sprites[i], skPosBeforeLift, skPos, rd.colors[i]);
                            if (WorldSphereMod.Rig.RigDriver.SubmitSkinnedActor(
                                    a, skPos, Quaternion.Euler(0f, skRot.y, 0f), skScl, rd.colors[i], rigType))
                            {
                                rd.has_normal_render[i] = false;
                            }
                            continue;
                        }
                    }

                    Sprite sp = rd.main_sprites[i];
                    if (sp == null) continue;

                    if (tier == WorldSphereMod.LOD.LodTier.Impostor)
                    {
                        bool submitted = false;
                        Mesh? im = WorldSphereMod.LOD.ImpostorBillboard.GetOrCreate(sp);
                        Material? imMat = WorldSphereMod.LOD.ImpostorBillboard.GetMaterial();
                        if (im == null || im.vertexCount == 0 || imMat == null) continue;
                        Vector3 imPos = rd.positions[i];
                        Vector3 imPosBeforeLift = imPos;
                        Vector3 imScl = rd.scales[i];
                        if (rd.flip_x_states[i]) imScl.x = -imScl.x;
                        if (imPos.z < Constants.ZDisplacement * 0.5f)
                        {
                            imPos = imPos.To3DTileHeight(false);
                        }
                        LogActorSubmitDiagnostic("impostor", ref _actorImpostorDiagnosticLogged, a, sp, imPosBeforeLift, imPos, rd.colors[i]);
                        Quaternion br = WorldSphereMod.LOD.ImpostorBillboard.GetFacingRotation(imPos);
                        Matrix4x4 imTrs = Matrix4x4.TRS(imPos, br, imScl);
                        MeshInstanceBatcher.Submit(im, imMat, imTrs, rd.colors[i]);
                        submitted = true;
                        if (submitted)
                        {
                            rd.has_normal_render[i] = false;
                        }
                        continue;
                    }

                    Mesh m = VoxelMeshCache.Get(sp, -1, true);
                    if (m == null || m.vertexCount == 0) continue;

                    Vector3 pos = rd.positions[i];
                    Vector3 posBeforeLift = pos;
                    if (pos.z < Constants.ZDisplacement * 0.5f)
                    {
                        pos = pos.To3DTileHeight(false);
                    }
                    LogActorSubmitDiagnostic("voxel", ref _actorVoxelDiagnosticLogged, a, sp, posBeforeLift, pos, rd.colors[i]);
                    SanityTestCube.CaptureFirstActorPos(pos);
                    Vector3 rot = rd.rotations[i];
                    Vector3 scl = rd.scales[i];
                    if (rd.flip_x_states[i]) scl.x = -scl.x;
                    scl.z = scl.x;
                    scl *= Core.savedSettings.VoxelScaleMultiplier;
                    // Lift the mesh CENTER up by half the world-space mesh height so the
                    // mesh BOTTOM sits ON the terrain surface instead of being embedded
                    // inside the terrain/water voxel cube (which sits at y~2-3, exactly
                    // where Tools.To3DTileHeight(false) puts the actor center). Without
                    // this, half the actor mesh is hidden inside the cube and at
                    // strategy-zoom altitudes it reads as 100% invisible.
                    float halfHeight = m.bounds.size.y * 0.5f * scl.y;
                    pos.y += halfHeight;
                    LogFirstActorPos(posBeforeLift, pos, scl);
                    // Z/X axes encode sprite-billboard lean; on a 3D mesh they topple the body. Yaw only here; lean returns in Phase 6 as a spine-bone tilt.
                    Matrix4x4 trs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, rot.y, 0f), scl);
                    RecordActorVoxelTrs(trs);
                    // Hide the sprite quad for this actor — we drew the 3D mesh instead.
                    if (Submit(m, trs, rd.colors[i]))
                    {
                        rd.has_normal_render[i] = false;
                        TraceActorColorSample("voxel", i, rd.colors[i], a, sp, posBeforeLift, pos, rot, scl);
                    }
                }
            }

            static WorldSphereMod.Rig.RigType ResolveRigType(string assetId)
            {
                return Constants.ResolveActorRig(assetId);
            }

            static void LogActorSubmitDiagnostic(string path, ref bool logged, Actor actor, Sprite? sprite, Vector3 beforeLift, Vector3 afterLift, Color tint)
            {
                if (logged) return;
                logged = true;
                string assetId = actor != null && actor.asset != null ? actor.asset.id : "<null>";
                string spriteName = sprite != null ? sprite.name : "<null>";
                Debug.Log($"[WSM3D] Actor {path} submit sample asset={assetId} sprite={spriteName} posBeforeLift={beforeLift} posAfterLift={afterLift} color={tint} alpha={tint.a}");
            }

            static void TraceActorColorSample(
                string path,
                int index,
                Color tint,
                Actor actor,
                Sprite? sprite,
                Vector3 rawPos,
                Vector3 liftedPos,
                Vector3 rotation,
                Vector3 scale)
            {
                if (_actorVoxelColorSampleCount >= 3) return;
                if (path != "voxel") return;

                _actorVoxelColorSampleCount++;
                string actorId = actor != null && actor.asset != null ? actor.asset.id : "<null>";
                string spriteName = sprite != null ? sprite.name : "<null>";
                Debug.Log($"[WSM3D][DIAG] Actor voxel color sample {_actorVoxelColorSampleCount}/3 asset={actorId} sprite={spriteName} index={index} rawPos={rawPos} liftedPos={liftedPos} rotY={rotation.y:F3} scale={scale} color={tint}");
            }

            static void LogFirstActorPos(Vector3 rawPos, Vector3 liftedPos, Vector3 scl)
            {
                if (_firstActorPosLogged) return;
                _firstActorPosLogged = true;
                Debug.Log($"[WSM3D] First-actor pos: raw={rawPos}, lifted={liftedPos}, scl={scl}");
            }

            static void RecordActorVoxelTrs(Matrix4x4 trs)
            {
                if (_actorVoxelSubmitTranslations.Count >= 5) return;
                Vector4 pos = trs.GetColumn(3);
                _actorVoxelSubmitTranslations.Add(new Vector3(pos.x, pos.y, pos.z));
            }
        }

        // Phase 1 fallback for buildings. Phase 2's procgen building meshes override
        // this when SavedSettings.ProceduralBuildings flips on; until then, when the
        // player turns Voxel Entities on, voxelizing the building sprite is the best
        // we can do for 3D buildings without procgen.
        [Phase(nameof(SavedSettings.VoxelEntities))]
        [HarmonyPatch(typeof(BuildingManager), nameof(BuildingManager.precalculateRenderDataParallel))]
        public static class BuildingVoxelEmit
        {
            static bool _buildingVoxelEmitSubmitLogged;

            [HarmonyPostfix]
            public static void EmitVoxels(BuildingManager __instance)
            {
                if (!Core.IsWorld3D || !Core.savedSettings.VoxelEntities) return;
                if (Core.savedSettings.ProceduralBuildings) return;
                if (!EnsureMaterial()) return;

                var rd = __instance.render_data;
                var arr = __instance._array_visible_buildings;
                int n = __instance._visible_buildings_count;
                for (int i = 0; i < n; i++)
                {
                    Building b = arr[i];
                    if (b == null || b.asset == null) continue;
                    if (Constants.PerpBuildings.ContainsKey(b.asset.id)) continue;

                    Vector3 cullPos = rd.positions[i];
                    if (cullPos.z < Constants.ZDisplacement * 0.5f)
                    {
                        cullPos = cullPos.To3DTileHeight(false);
                    }
                    float radius = 3f;
                    if (!WorldSphereMod.LOD.FrustumCuller.IsVisible(cullPos, radius))
                    {
                        continue;
                    }
                    WorldSphereMod.LOD.LodTier tier = WorldSphereMod.LOD.LodSelector.Select(cullPos, b.GetHashCode());

                    Sprite sp = rd.main_sprites[i];
                    if (sp == null) continue;

                    if (tier == WorldSphereMod.LOD.LodTier.Impostor)
                    {
                        bool submitted = false;
                        Mesh? im = WorldSphereMod.LOD.ImpostorBillboard.GetOrCreate(sp);
                        Material? imMat = WorldSphereMod.LOD.ImpostorBillboard.GetMaterial();
                        // Impostor mesh build failed: fall through to vanilla
                        // sprite (don't zero scales — that's the "hide the
                        // sprite because we drew our own mesh" path, which
                        // we didn't actually do here).
                        if (im == null || im.vertexCount == 0 || imMat == null) continue;
                        Vector3 imPos = rd.positions[i];
                        Vector3 imScl = rd.scales[i];
                        if (rd.flip_x_states[i]) imScl.x = -imScl.x;
                        if (imPos.z < Constants.ZDisplacement * 0.5f)
                        {
                            imPos = imPos.To3DTileHeight(false);
                        }
                        Quaternion br = WorldSphereMod.LOD.ImpostorBillboard.GetFacingRotation(imPos);
                        Matrix4x4 imTrs = Matrix4x4.TRS(imPos, br, imScl);
                        MeshInstanceBatcher.Submit(im, imMat, imTrs, rd.colors[i]);
                        submitted = true;
                        if (submitted)
                        {
                            rd.scales[i] = Vector3.zero;
                        }
                        continue;
                    }

                    Mesh m = VoxelMeshCache.Get(sp);
                    if (m == null || m.vertexCount == 0) continue;

                    Vector3 pos = rd.positions[i];
                    if (pos.z < Constants.ZDisplacement * 0.5f)
                    {
                        pos = pos.To3DTileHeight(false);
                    }
                    Vector3 rot = rd.rotations[i];
                    Vector3 scl = rd.scales[i];
                    if (rd.flip_x_states[i]) scl.x = -scl.x;
                    scl.z = scl.x;
                    // Lift mesh center up by half world-space height (same fix as
                    // ActorVoxelEmit): without it, mesh center sits at To3DTileHeight,
                    // which embeds half the mesh inside the terrain/foundation voxel.
                    scl *= Core.savedSettings.VoxelScaleMultiplier;
                    // Clamp building voxel sprite height to prevent excessive vertical scale
                    // (e.g. 5-10 px * 16 = 80-160 uu).
                    scl.x = Mathf.Sign(scl.x) * Mathf.Min(Mathf.Abs(scl.x), BuildingMaxScale);
                    scl.y = Mathf.Min(scl.y, BuildingMaxScale);
                    scl.z = Mathf.Min(scl.z, BuildingMaxScale);
                    float bldHalfHeight = m.bounds.size.y * 0.5f * scl.y;
                    pos.y += bldHalfHeight;
                    Matrix4x4 trs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, rot.y, 0f), scl);
                    if (!_buildingVoxelEmitSubmitLogged)
                    {
                        _buildingVoxelEmitSubmitLogged = true;
                        Debug.Log($"[WSM3D] BuildingVoxelEmit first submit mesh.bounds.size={m.bounds.size}, scaledBoundsSize={Vector3.Scale(m.bounds.size, scl)}");
                    }
                    // BuildingRenderData has no has_normal_render. Suppressing via scales[i]=0
                    // hides the sprite quad without nulling main_sprites (downstream
                    // calculateColoredSprite() chokes on null). Shadow sprite still draws as a
                    // ground decal under the 3D mesh — fine until Phase 5 ships real shadows.
                    if (Submit(m, trs, rd.colors[i]))
                    {
                        rd.scales[i] = Vector3.zero;
                    }
                }
            }
        }

        // Phase 1b: dropped items. No render_data[] — read Drop transform + SpriteRenderer.
        [Phase(nameof(SavedSettings.VoxelEntities))]
        [HarmonyPatch(typeof(Drop), nameof(Drop.updatePosition))]
        public static class DropVoxelEmit
        {
            [HarmonyPostfix]
            public static void EmitVoxel(Drop __instance)
            {
                if (!Core.IsWorld3D)
                {
                    return;
                }

                SpriteRenderer? sr = __instance._sprite_renderer;
                if (!Core.savedSettings.VoxelEntities)
                {
                    if (sr != null)
                    {
                        sr.enabled = true;
                    }

                    return;
                }

                if (!__instance.active || sr == null)
                {
                    return;
                }

                if (!EnsureMaterial())
                {
                    return;
                }

                Sprite? sp = sr.sprite;
                if (sp == null)
                {
                    return;
                }

                Vector3 cullPos = __instance.transform.position;
                if (!WorldSphereMod.LOD.FrustumCuller.IsVisible(cullPos, 1.5f))
                {
                    sr.enabled = true;
                    return;
                }

                WorldSphereMod.LOD.LodTier tier = WorldSphereMod.LOD.LodSelector.Select(cullPos, __instance.GetHashCode());
                Color tint = sr.color;

                if (tier == WorldSphereMod.LOD.LodTier.Impostor)
                {
                    Mesh? im = WorldSphereMod.LOD.ImpostorBillboard.GetOrCreate(sp);
                    Material? imMat = WorldSphereMod.LOD.ImpostorBillboard.GetMaterial();
                    if (im == null || im.vertexCount == 0 || imMat == null)
                    {
                        sr.enabled = true;
                        return;
                    }

                    Vector3 imPos = cullPos;
                    float imScale = Mathf.Max(__instance._scale, 0.01f) * Core.savedSettings.VoxelScaleMultiplier;
                    Vector3 imScl = new Vector3(imScale, imScale, imScale);
                    Quaternion br = WorldSphereMod.LOD.ImpostorBillboard.GetFacingRotation(imPos);
                    MeshInstanceBatcher.Submit(im, imMat, Matrix4x4.TRS(imPos, br, imScl), tint);
                    sr.enabled = false;
                    return;
                }

                Mesh? mesh = VoxelMeshCache.Get(sp, -1, true);
                if (mesh == null || mesh.vertexCount == 0)
                {
                    sr.enabled = true;
                    return;
                }

                Vector3 pos = __instance.transform.position;
                float scale = Mathf.Max(__instance._scale, 0.01f) * Core.savedSettings.VoxelScaleMultiplier;
                Vector3 scl = new Vector3(scale, scale, scale);
                scl.z = scl.x;
                float halfHeight = mesh.bounds.size.y * 0.5f * scl.y;
                pos.y += halfHeight;
                float yaw = __instance.transform.eulerAngles.y;
                Matrix4x4 trs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), scl);
                if (Submit(mesh, trs, tint))
                {
                    sr.enabled = false;
                }
                else
                {
                    sr.enabled = true;
                }
            }
        }

        // Phase 1b: projectiles. Postfix on drawProjectiles after vanilla + SetProjectile transpiler.
        [Phase(nameof(SavedSettings.VoxelEntities))]
        [HarmonyPatch(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawProjectiles))]
        public static class ProjectileVoxelEmit
        {
            [HarmonyPostfix]
            public static void EmitVoxels(QuantumSpriteAsset pAsset)
            {
                if (!Core.IsWorld3D)
                {
                    return;
                }

                if (!Core.savedSettings.VoxelEntities)
                {
                    RestoreProjectileSprites(pAsset);
                    return;
                }

                if (!EnsureMaterial())
                {
                    return;
                }

                if (World.world == null || World.world.projectiles == null)
                {
                    return;
                }

                List<Projectile> list = World.world.projectiles.list;
                if (list == null)
                {
                    return;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    Projectile projectile = list[i];
                    if (projectile == null || !projectile._alive || projectile.asset == null)
                    {
                        continue;
                    }

                    Sprite? sprite = ResolveProjectileSprite(projectile);
                    if (sprite == null)
                    {
                        continue;
                    }

                    Vector3 pos = BuildProjectileWorldPosition(projectile);
                    if (!WorldSphereMod.LOD.FrustumCuller.IsVisible(pos, 1.5f))
                    {
                        continue;
                    }

                    WorldSphereMod.LOD.LodTier tier = WorldSphereMod.LOD.LodSelector.Select(pos, projectile.GetHashCode());
                    Color tint = new Color(1f, 1f, 1f, projectile.getAlpha());
                    bool perp = Constants.PerpProjectiles.ContainsKey(projectile.asset.id);

                    if (tier == WorldSphereMod.LOD.LodTier.Impostor)
                    {
                        Mesh? im = WorldSphereMod.LOD.ImpostorBillboard.GetOrCreate(sprite);
                        Material? imMat = WorldSphereMod.LOD.ImpostorBillboard.GetMaterial();
                        if (im == null || im.vertexCount == 0 || imMat == null)
                        {
                            continue;
                        }

                        float imScale = Mathf.Max(projectile.getCurrentScale(), 0.01f) * Core.savedSettings.VoxelScaleMultiplier;
                        Vector3 imScl = new Vector3(imScale, imScale, imScale);
                        Quaternion br = WorldSphereMod.LOD.ImpostorBillboard.GetFacingRotation(pos);
                        MeshInstanceBatcher.Submit(im, imMat, Matrix4x4.TRS(pos, br, imScl), tint);
                        SuppressProjectileSprite(pAsset, pos);
                        continue;
                    }

                    Mesh? mesh = VoxelMeshCache.Get(sprite, -1, true);
                    if (mesh == null || mesh.vertexCount == 0)
                    {
                        continue;
                    }

                    float scale = Mathf.Max(projectile.getCurrentScale(), 0.01f) * Core.savedSettings.VoxelScaleMultiplier;
                    Vector3 scl = new Vector3(scale, scale, scale);
                    scl.z = scl.x;
                    float halfHeight = mesh.bounds.size.y * 0.5f * scl.y;
                    pos.y += halfHeight;
                    Quaternion rot = perp
                        ? projectile.rotation
                        : Quaternion.Euler(0f, projectile.rotation.eulerAngles.y, 0f);
                    Matrix4x4 trs = Matrix4x4.TRS(pos, rot, scl);
                    if (Submit(mesh, trs, tint))
                    {
                        SuppressProjectileSprite(pAsset, pos);
                    }
                }
            }

            static Sprite? ResolveProjectileSprite(Projectile projectile)
            {
                ProjectileAsset asset = projectile.asset;
                if (asset.frames == null || asset.frames.Length == 0)
                {
                    return null;
                }

                if (asset.animated)
                {
                    return AnimationHelper.getSpriteFromList(
                        projectile.GetHashCode(),
                        asset.frames,
                        asset.animation_speed);
                }

                return asset.frames[0];
            }

            static Vector3 BuildProjectileWorldPosition(Projectile projectile)
            {
                Vector2 flat = projectile.getTransformedPositionWithHeight();
                Vector3 pos = new Vector3(flat.x, flat.y, projectile.getCurrentHeight());
                return pos.To3DTileHeight(true);
            }

            static void SuppressProjectileSprite(QuantumSpriteAsset pAsset, Vector3 worldPos)
            {
                if (pAsset?.group_system?._sprites == null)
                {
                    return;
                }

                GroupSpriteObject[] sprites = pAsset.group_system._sprites;
                for (int i = 0; i < sprites.Length; i++)
                {
                    GroupSpriteObject? obj = sprites[i];
                    if (obj == null || obj.sprite_renderer == null || !obj.sprite_renderer.enabled)
                    {
                        continue;
                    }

                    if (Vector3.SqrMagnitude(obj.m_transform.position - worldPos) > 0.25f)
                    {
                        continue;
                    }

                    obj.sprite_renderer.enabled = false;
                    return;
                }
            }

            static void RestoreProjectileSprites(QuantumSpriteAsset pAsset)
            {
                if (pAsset?.group_system?._sprites == null)
                {
                    return;
                }

                GroupSpriteObject[] sprites = pAsset.group_system._sprites;
                for (int i = 0; i < sprites.Length; i++)
                {
                    GroupSpriteObject? obj = sprites[i];
                    if (obj?.sprite_renderer != null)
                    {
                        obj.sprite_renderer.enabled = true;
                    }
                }
            }
        }
    }

    /// <summary>
    /// MonoBehaviour driver that calls <see cref="VoxelRender.Flush"/> once per frame
    /// in <c>LateUpdate</c>, after every render-data calculation has run. Attached to
    /// the mod's root GameObject in <c>Core.Init</c>.
    /// </summary>
    public sealed class VoxelFrameDriver : MonoBehaviour
    {
        static bool _lastSkeletalState = false;
        const float kCameraLookupInterval = 0.05f;
        const int kPerfSampleWindowFrames = 60;
        static float _nextCameraLookup = 0f;
        static int _perfFrameCounter;
        static float _perfDeltaTimeSum;

        void OnEnable()
        {
            // Survive scene transitions — re-parent to a dedicated root GameObject
            // owned by WSM3D + apply DontDestroyOnLoad. If we just call DDoL on
            // the current parent, Unity silently no-ops for non-root nodes.
            try
            {
                if (transform.parent != null) transform.SetParent(null, worldPositionStays: false);
                UnityEngine.Object.DontDestroyOnLoad(gameObject);
            } catch { }
            MeshInstanceBatcher.SetMainThread();
            // Force per-instance fallback only when explicitly requested. The
            // Standard material path now keeps INSTANCING_ON in sync before DrawMeshInstanced.
            MeshInstanceBatcher.SetFallbackPath(Core.savedSettings != null && Core.savedSettings.ForceFallbackDrawPath);
            WorldSphereMod.Lighting.SunDriver.BindMainCamera(CameraManager.MainCamera);
        }

        static float _telemetryLastTime;
        static int _instancingTelemetryFrame;

        /// <summary>Per-frame voxel/FX driver; invoked from MapBox.renderStuff Harmony hook so it survives scene transitions.</summary>
        public static void TickPerFrame()
        {
            if (Core.ClearVoxelMeshCacheOnFirstFrame)
            {
                Core.ClearVoxelMeshCacheOnFirstFrame = false;
                try { VoxelMeshCache.Clear(); } catch (System.Exception ex) { Debug.LogWarning("[WSM3D] First-frame VoxelMeshCache.Clear failed: " + ex.Message); }
            }

            float deltaTime = Time.deltaTime;
            _perfFrameCounter++;
            _perfDeltaTimeSum += deltaTime;
            if (_perfFrameCounter >= kPerfSampleWindowFrames)
            {
                float avgFrameTime = _perfDeltaTimeSum / kPerfSampleWindowFrames;
                float avgFps = avgFrameTime > 0f ? 1f / avgFrameTime : 0f;
                Debug.Log($"[WSM3D][Perf] frameDeltaMs={deltaTime * 1000f:F2} avg60FrameDeltaMs={avgFrameTime * 1000f:F2} avg60Fps={avgFps:F1}");
                _perfFrameCounter = 0;
                _perfDeltaTimeSum = 0f;
            }

            _instancingTelemetryFrame++;
            if (_instancingTelemetryFrame >= 60)
            {
                _instancingTelemetryFrame = 0;
                Debug.Log($"[WSM3D][Telemetry] InstancingEfficiency={MeshInstanceBatcher.InstancingEfficiency:F4} FrameBucketCount={MeshInstanceBatcher.FrameBucketCount} FrameInstances={MeshInstanceBatcher.FrameInstances}");
            }

            // Log-based telemetry every 10s — bypasses bridge for steady-state observability
            // even when bridge is in scene-transition known-issue state.
            float now = Time.realtimeSinceStartup;
            if (now - _telemetryLastTime > 10f)
            {
                _telemetryLastTime = now;
                Debug.Log($"[WSM3D][Telemetry] frameMs={Time.unscaledDeltaTime*1000:F2} drawCalls={MeshInstanceBatcher.FrameDrawCalls} instances={MeshInstanceBatcher.FrameInstances} cacheSize={VoxelMeshCache.Count} cacheHits={VoxelMeshCache.HitCount} cacheMisses={VoxelMeshCache.MissCount} gcMB={(System.GC.GetTotalMemory(false) / 1048576f):F1}");
            }

            WorldSphereMod.Voxel.VoxelMeshCache.BeginFrame();
            WorldSphereMod.LOD.ImpostorBillboard.Tick();

            bool hasRenderWork = Core.savedSettings.VoxelEntities || Core.savedSettings.ProceduralBuildings || Core.savedSettings.CrossedQuadFoliage;
            if (hasRenderWork)
            {
                WorldSphereMod.LOD.FrustumCuller.UpdatePlanes();
            }

            WorldSphereMod.Rig.RigCache.Tick();
            WorldSphereMod.Rig.RigCache.DrainPendingDestroy();
            if (Core.savedSettings.SkeletalAnimation)
            {
                if (_lastSkeletalState == false)
                {
                    _lastSkeletalState = true;
                }
                WorldSphereMod.Rig.RigDriver.Update();
            }
            else if (_lastSkeletalState)
            {
                // Edge transition true->false. Dispose stale SkinnedMeshRenderer
                // instances ONCE so they don't animate with garbage bone matrices
                // (dragonfly-legs bug). Per-frame Clear would freeze the load.
                WorldSphereMod.Rig.RigDriver.Clear();
                _lastSkeletalState = false;
            }

            // Flush runs in LateUpdate after all emit postfixes for this frame.

            WorldSphereMod.Voxel.VoxelMeshCache.PumpQueuedBuilds(1);
            WorldSphereMod.Voxel.VoxelMeshCache.DrainCompletedBuilds(8);

            if (Core.savedSettings.DebugSanityCube)
            {
                SanityTestCube.Draw();
            }

            if (Core.savedSettings.ProceduralBuildings)
            {
                WorldSphereMod.ProcGen.ProcGenCache.DrainPendingDestroy();
            }

            if (Core.savedSettings.CrossedQuadFoliage)
            {
                WorldSphereMod.Foliage.CrossedQuadMeshCache.DrainPendingDestroy();
            }

            if (Core.savedSettings.MeshWater)
            {
                WorldSphereMod.Water.WaterRender.UpdateLifecycle();
            }

            WorldSphereMod.Terrain.MountainSlopeSurface.EnsureActive();

            if (Time.time >= _nextCameraLookup)
            {
                WorldSphereMod.Lighting.SunDriver.BindMainCamera(CameraManager.MainCamera);
                if (Core.savedSettings.SSAOEnabled)
                {
                    WorldSphereMod.PostFx.ScreenSpaceAO.EnsureCreated();
                }
                if (Core.savedSettings.SSGIEnabled)
                {
                    WorldSphereMod.PostFx.ScreenSpaceGI.EnsureCreated();
                }
                _nextCameraLookup = Time.time + kCameraLookupInterval;
            }

            WorldSphereMod.Lighting.SunDriver.Update();

            WorldSphereMod.Fx.DecalPool.Tick();

            WorldSphereMod.Fx.Environmental.Tick();

            // CRITICAL: do NOT call ApplySetting every frame -- PostFxController logs on each
            // invocation. Track last applied values and reconcile only on change (bridge/API
            // edits, load-order races). ApplyPhaseToggle also invokes ApplySetting immediately.
            bool currentPostFX = Core.savedSettings.PostFX;
            if (currentPostFX != _lastAppliedPostFX)
            {
                _lastAppliedPostFX = currentPostFX;
                WorldSphereMod.Fx.PostFxController.ApplySetting(currentPostFX);
            }

            bool currentSSAO = Core.savedSettings.SSAOEnabled;
            if (currentSSAO != _lastAppliedSSAOEnabled)
            {
                _lastAppliedSSAOEnabled = currentSSAO;
                WorldSphereMod.PostFx.ScreenSpaceAO.ApplySetting(currentSSAO);
            }

            bool currentSSGI = Core.savedSettings.SSGIEnabled;
            if (currentSSGI != _lastAppliedSSGIEnabled)
            {
                _lastAppliedSSGIEnabled = currentSSGI;
                WorldSphereMod.PostFx.ScreenSpaceGI.ApplySetting(currentSSGI);
            }
        }

        // Pre-emit work runs from MapBox.renderStuff (Harmony); flush must run after emit
        // postfixes, so LateUpdate remains the end-of-frame sink.
        void LateUpdate()
        {
            if (MeshInstanceBatcher.HasPendingSubmissions)
            {
                VoxelRender.Flush();
                VoxelMeshCache.DrainPendingDestroy();
            }
        }

        static bool _lastAppliedPostFX = false;
        static bool _lastAppliedSSAOEnabled = false;
        static bool _lastAppliedSSGIEnabled = false;
    }
}
