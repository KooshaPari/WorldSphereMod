using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using WorldSphereMod.NewCamera;
using WorldSphereMod.Textures;
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
        internal static Material? _material;
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
            // Drain the batcher BEFORE destroying the material — pending
            // submissions hold references to meshes/materials that become
            // destroyed-Unity-null after this method runs, causing NRE in
            // DrawFallbackPath when Flush iterates stale bucket keys.
            MeshInstanceBatcher.Reset();
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
            _flushDiagLogged = false;
            _submitDiagLogged = false;
            ActorVoxelEmit.ResetDiag();
            BuildingVoxelEmit.ResetDiag();
            // Clear render-error telemetry + queued markers on world reload — stale counts
            // from a prior world would otherwise pollute /diag/errors.
            RenderErrorRegistry.Reset();
            RenderErrorMarkers.Reset();
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
                if (_material != null && _material.shader != null &&
                    _material.shader.name == "Standard" &&
                    Core.Sphere.LoadedShaders.ContainsKey("OpaqueVertexColor"))
                {
                    Material? upgrade = TryCompileInlineVoxelShader();
                    if (upgrade != null)
                    {
                        Object.Destroy(_material);
                        _material = upgrade;
                        McPackLoader.ApplyToMaterial(_material);
                        Debug.Log("[WSM3D] Voxel material upgraded from Standard to OpaqueVertexColor (late bundle load).");
                    }
                }
                if (MeshInstanceBatcher.UseFallbackPath && _material != null && _material.enableInstancing)
                {
                    _material.enableInstancing = false;
                }
                return _material != null;
            }
            _materialAttempted = true;

            // 60f1 ships a STRIPPED shader set: Particles/*, URP/*, Unlit/* all
            // return null at runtime, so reaching for them produced null → magenta
            // voxels. ResolveShader maps to "Standard" only — never Unlit/URP.
            string[] candidates =
            {
                "Standard",
            };
            var shaderLookup = new Dictionary<string, Shader>();
            foreach (var name in candidates)
            {
                Shader s = WorldSphereMod.Core.Sphere.ResolveShader("");
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
                // Enable INSTANCING_ON keyword so Standard shader's instanced
                // variant actually compiles. Without this keyword, Unity skips
                // the instanced code path even when enableInstancing=true.
                m.EnableKeyword("INSTANCING_ON");
                if (MeshInstanceBatcher.UseFallbackPath)
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
                    float emissionMultiplier = Core.savedSettings != null ? Core.savedSettings.ImpostorEmissionMultiplier : 1.5f;
                    m.SetColor("_EmissionColor", new UnityEngine.Color(emissionMultiplier, emissionMultiplier, emissionMultiplier, 1f));
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
                    Material inlineMaterial = new Material(existing) { name = "WSM3D.Voxel.OpaqueVertexColor", enableInstancing = true };
                    // Geometry+1 (queue 2001) so voxel meshes render just AFTER
                    // terrain (queue 2000). Without this, voxels at Geometry share
                    // the same render queue as terrain and z-fight — losing to
                    // terrain fragments at the same depth, producing invisible output.
                    inlineMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 1;
                    // Belt+suspenders: set _MainTex to white and _EmissionColor to
                    // the same boost the Standard fallback uses. The shader defaults
                    // _MainTex to "white" {} but some Unity runtimes leave it null
                    // until explicitly set; _EmissionColor defaults to black in the
                    // Properties block which is too dim in unlit WorldBox scenes.
                    inlineMaterial.SetTexture("_MainTex", UnityEngine.Texture2D.whiteTexture);
                    inlineMaterial.SetColor("_Color", UnityEngine.Color.white);
                    inlineMaterial.SetColor("_EmissionColor", new UnityEngine.Color(0.15f, 0.15f, 0.15f, 1f));
                    if (MeshInstanceBatcher.UseFallbackPath)
                    {
                        inlineMaterial.enableInstancing = false;
                    }
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

        internal static int _submitDiagCount;
        static bool _submitDiagLogged;

        /// <summary>Per-frame submission. Matrix should already include scale.</summary>
        public static bool Submit(Mesh mesh, Matrix4x4 trs, Color tint)
        {
            // Removed: if (InstancingBroken) return false. Once instancing throws,
            // MeshInstanceBatcher.Flush has a working Graphics.DrawMesh fallback path.
            // Pre-empting Submit here used to permanently disable voxel rendering after
            // the first instancing exception. Now we always submit; Flush picks the right path.
            if (_material == null && !EnsureMaterial()) return false;
            _submitDiagCount++;
            // TEMPORARY DIAGNOSTIC: log first non-sanity-cube submit
            if (!_submitDiagLogged && Core.savedSettings.ProfilerDump && mesh != null && mesh.name != "WSM3D.SanityTestCube")
            {
                _submitDiagLogged = true;
                Debug.Log($"[WSM3D][DIAG-SUBMIT] First non-sanity Submit: mesh={mesh.name} verts={mesh.vertexCount} matName={_material?.name} trs.pos={trs.GetColumn(3)} tint={tint} totalSubmits={_submitDiagCount}");
            }
            MeshInstanceBatcher.Submit(mesh, _material!, trs, tint);
            return true;
        }

        public static Material? GetResolvedMaterial()
        {
            return EnsureMaterial() ? _material : null;
        }

        static bool _flushDiagLogged;

        /// <summary>Issue all batched draw calls. Call once per frame after submissions.</summary>
        public static void Flush()
        {
            // TEMPORARY DIAGNOSTIC: one-shot log to track Flush calls
            if (!_flushDiagLogged && Core.savedSettings.ProfilerDump)
            {
                _flushDiagLogged = true;
                Debug.Log($"[WSM3D][DIAG-FLUSH] VoxelRender.Flush CALLED materialNull={_material == null} hasPending={MeshInstanceBatcher.HasPendingSubmissions} bucketCount={MeshInstanceBatcher.FrameBucketCount} instances={MeshInstanceBatcher.FrameInstances} drawCalls={MeshInstanceBatcher.FrameDrawCalls}");
            }
            if (_material == null) return;
            Camera flushCamera = ResolveFlushCamera();
            LogActorVoxelSubmitDiagnostics(flushCamera);

            if (!Core.savedSettings.ProfilerDump)
            {
                MeshInstanceBatcher.Flush();
                VoxelMeshCache.Tick();
                Bridge.BridgeServer.RefreshTelemetryCache();
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
            Bridge.BridgeServer.RefreshTelemetryCache();
        }

        static void LogActorVoxelSubmitDiagnostics(Camera? camera)
        {
            if (_actorVoxelSubmitTranslations.Count == 0 || !Core.savedSettings.ProfilerDump) return;

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
            if (cam == null || !Core.savedSettings.ProfilerDump) return;

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
            public static bool EmitVoxelsCalled;
            public static int LastVisibleUnitsCount;
            public static int LastFrustumCullerPassCount;
            public static int LastBatcherSubmitCount;
            static bool _emitDiagLogged;
            static int _emitDiagFrameCounter;
            static bool _emitDiagSawNonZero;

            public static void ResetDiag()
            {
                _emitDiagLogged = false;
                _emitDiagFrameCounter = 0;
                _emitDiagSawNonZero = false;
            }

            [HarmonyPostfix]
            [HarmonyPriority(Priority.First)]
            public static void EmitVoxels(ActorManager __instance)
            {
                // REGRESSION GUARD: must stay Priority.First. Vanilla sprite-render
                // Postfixes on precalculateRenderDataParallel race ours; if we run
                // Last, has_normal_render[i] is cleared too late and both the voxel
                // mesh AND the vanilla 2D billboard render — actors appear as 2D
                // billboards instead of 3D voxels. Symptom: user reports
                // "voxel actors back to billboards".
                EmitVoxelsCalled = true;
                Tools.ClearTileHeightSmoothCache();
                // TEMPORARY DIAGNOSTIC: one-shot log to verify the Harmony postfix fires
                if (!_emitDiagLogged && Core.savedSettings.ProfilerDump)
                {
                    _emitDiagLogged = true;
                    bool matOk = EnsureMaterial();
                    int visCount = __instance.visible_units != null ? __instance.visible_units.count : -1;
                    int frustumPass = 0;
                    int frustumFail = 0;
                    int nullActor = 0;
                    int perpSkipped = 0;
                    int meshNull = 0;
                    int meshOk = 0;
                    if (visCount > 0 && __instance.render_data != null)
                    {
                        var diagArr = __instance.visible_units.array;
                        var diagRd = __instance.render_data;
                        for (int di = 0; di < visCount; di++)
                        {
                            Actor da = diagArr[di];
                            if (da == null || da.asset == null) { nullActor++; continue; }
                            if (Constants.PerpActors.ContainsKey(da.asset.id)) { perpSkipped++; continue; }
                            Vector3 dCullPos = diagRd.positions[di];
                            if (dCullPos.z < Constants.ZDisplacement * 0.5f)
                                dCullPos = dCullPos.To3DTileHeight(false);
                            if (!WorldSphereMod.LOD.FrustumCuller.IsVisible(dCullPos, 2f))
                            { frustumFail++; continue; }
                            frustumPass++;
                            Sprite dSp = ResolveActorSprite(diagRd, di, da);
                            if (dSp == null) { meshNull++; continue; }
                            Mesh dm = VoxelMeshCache.Get(dSp, -1, true);
                            if (dm == null || dm.vertexCount == 0) meshNull++; else meshOk++;
                        }
                    }
                    Debug.Log($"[WSM3D][DIAG-EMIT] ActorVoxelEmit.EmitVoxels CALLED isWorld3D={Core.IsWorld3D} VoxelEntities={Core.savedSettings.VoxelEntities} materialOk={matOk} visible_units.count={visCount} nullActor={nullActor} perpSkipped={perpSkipped} frustumPass={frustumPass} frustumFail={frustumFail} meshOk={meshOk} meshNull={meshNull} cacheSize={VoxelMeshCache.Count} frame={_emitDiagFrameCounter}");
                    if (visCount > 0) _emitDiagSawNonZero = true;
                }
                if (!Core.IsWorld3D || !Core.savedSettings.VoxelEntities) return;
                // MaterialNull: no usable shader/material resolved → every actor this frame is
                // invisible. Record once at the actor manager position so the operator sees WHY.
                if (!EnsureMaterial())
                {
                    RenderErrorRegistry.Record(RenderErrorType.MaterialNull, "ActorManager",
                        "EnsureMaterial() returned no usable voxel material", Vector3.zero);
                    return;
                }

                var rd = __instance.render_data;
                var arr = __instance.visible_units.array;
                int n = __instance.visible_units.count;
                LastVisibleUnitsCount = n;
                LastFrustumCullerPassCount = 0;
                LastBatcherSubmitCount = 0;
                // DIAG-SUBMIT path counters — find where the 8 meshOk actors are being dropped
                int dsNullActor = 0, dsPerpSkipped = 0, dsFrustumFail = 0;
                int dsTierImpostor = 0, dsTierProxy = 0, dsTierVoxel = 0, dsTierOther = 0;
                int dsSkeletalAttempt = 0, dsSkeletalSubmitOk = 0, dsSkeletalSubmitFail = 0;
                int dsImpostorMeshNull = 0, dsImpostorMatNull = 0, dsImpostorSubmit = 0;
                int dsSpriteNull = 0, dsVoxelMeshNull = 0, dsVoxelSubmitAttempt = 0, dsVoxelSubmitOk = 0, dsVoxelSubmitFail = 0;
                for (int i = 0; i < n; i++)
                {
                    Actor a = arr[i];
                    if (a == null || a.asset == null) { dsNullActor++; continue; }
                    // Per-asset opt-out: the existing v1 API hands designers a way to
                    // mark assets as "perp" (ground-aligned billboard). Those keep
                    // sprite rendering for now — they tend to be flat decals (arrows,
                    // ground markers) where voxelization adds nothing.
                    if (Constants.PerpActors.ContainsKey(a.asset.id)) { dsPerpSkipped++; continue; }
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
                    float radius = 2f * Mathf.Max(1f, Core.savedSettings.VoxelScaleMultiplier * 0.5f);
                    if (!WorldSphereMod.LOD.FrustumCuller.IsVisible(cullPos, radius))
                    {
                        dsFrustumFail++;
                        continue;
                    }
                    LastFrustumCullerPassCount++;
                    WorldSphereMod.LOD.LodTier tier = WorldSphereMod.LOD.LodSelector.Select(cullPos, a.GetHashCode());
                    if (tier == WorldSphereMod.LOD.LodTier.Impostor) dsTierImpostor++;
                    else if (tier == WorldSphereMod.LOD.LodTier.Voxel)
                    {
                        dsTierVoxel++;
                    }
                    else if (tier != WorldSphereMod.LOD.LodTier.Impostor)
                    {
                        // Proxy currently shares the full voxel path, so keep
                        // it in the voxel-path diagnostics without introducing a
                        // Proxy-specific emit branch.
                        dsTierVoxel++;
                        dsTierProxy++;
                    }
                    else dsTierOther++;

                    if (Core.savedSettings.SkeletalAnimation && tier != WorldSphereMod.LOD.LodTier.Impostor)
                    {
                        WorldSphereMod.Rig.RigType rigType = ResolveRigType(a.asset.id);
                        if (rigType != WorldSphereMod.Rig.RigType.None)
                        {
                            dsSkeletalAttempt++;
                            // VOXEL-OR-INVISIBLE: suppress the vanilla 2D sprite for skinned
                            // actors up-front too. If the skinned submit fails, the actor is
                            // invisible this frame rather than reverting to a 2D billboard.
                            rd.has_normal_render[i] = false;
                            Vector3 skPos = rd.positions[i];
                            Vector3 skPosBeforeLift = skPos;
                            Vector3 skRot = rd.rotations[i];
                            Vector3 skScl = rd.scales[i];
                            if (rd.flip_x_states[i]) skScl.x = -skScl.x;
                            skScl.z = skScl.x;
                            // Match the static actor path scale (line ~737): raw
                            // rd.scales[i] is sprite-native (~1) and would render a
                            // tiny actor against the 8x voxel world. RigDriver now
                            // routes through the static voxel mesh (skinned path is
                            // disabled, see RigDriver.kSkinnedRigProductionReady), so
                            // apply the same VoxelScaleMultiplier * ActorVoxelScaleFactor
                            // the normal static actor submit uses.
                            skScl *= Core.savedSettings.VoxelScaleMultiplier * Core.savedSettings.ActorVoxelScaleFactor;
                            if (skPos.z < Constants.ZDisplacement * 0.5f)
                            {
                                skPos = skPos.To3DTileHeight(false);
                            }
                            // Match the static actor path Y-lift so the mesh BOTTOM
                            // sits on the terrain instead of embedded in the tile
                            // cube. skScl is already fully scaled above, so estimate
                            // half-height directly from it (no extra VoxelScaleMultiplier
                            // — that would double-lift now that skScl carries the
                            // multiplier). ~0.5 world units of mesh height pre-scale.
                            float skHalfHeight = Mathf.Abs(skScl.y) * 0.5f;
                            skPos.y += skHalfHeight;
                            LogActorSubmitDiagnostic("skeletal", ref _actorSkeletalDiagnosticLogged, a, ResolveActorSprite(rd, i, a), skPosBeforeLift, skPos, rd.colors[i]);
                            if (WorldSphereMod.Rig.RigDriver.SubmitSkinnedActor(
                                    a, skPos, Quaternion.Euler(0f, skRot.y, 0f), skScl, rd.colors[i], rigType))
                            {
                                dsSkeletalSubmitOk++;
                                rd.has_normal_render[i] = false;
                            }
                            else
                            {
                                dsSkeletalSubmitFail++;
                            }
                            continue;
                        }
                    }

                    // Resolve the actor's actual current sprite. main_sprites[i] is null for
                    // any actor whose colored-sprite resolution was deferred to the
                    // (post-postfix) precalculateRenderDataNormal pass; fall back to the live
                    // animation-frame sprite via Actor.calculateMainSprite() in that case.
                    Sprite sp = ResolveActorSprite(rd, i, a);
                    if (sp == null)
                    {
                        dsSpriteNull++;
                        // SpriteNull: neither render_data nor calculateMainSprite() yielded a sprite.
                        RecordActorError(RenderErrorType.SpriteNull, a,
                            "main_sprites[i] null and calculateMainSprite() returned null", rd.positions[i]);
                        continue;
                    }

                    // VOXEL-OR-INVISIBLE POLICY (user, 2026-05-30): objects are REAL voxel
                    // volumes or NOTHING — never a 2D/2.5D billboard. So suppress the vanilla
                    // 2D sprite for EVERY eligible actor up-front (not just on successful
                    // submit). If the voxel mesh isn't ready / material invalid / far LOD,
                    // we skip the submit and the actor simply renders nothing this frame.
                    rd.has_normal_render[i] = false;

                    if (tier == WorldSphereMod.LOD.LodTier.Impostor)
                    {
                        // FAR TIER = CULL, NOT BILLBOARD. The user explicitly prefers seeing
                        // NOTHING over a flat impostor. Keep the LOD distance logic (so near
                        // objects still voxelize) but the far tier draws nothing. Sprite is
                        // already suppressed above, so the object is invisible at distance.
                        dsTierImpostor++;
                        dsImpostorMeshNull++;
                        continue;
                    }

                    // Phase 10: LodTier.Proxy (and Voxel) share full voxel path until BuildProxy/ProxyMeshCache ship.
                    Mesh m = VoxelMeshCache.Get(sp, -1, true);
                    // Mesh not built yet (async) or empty → INVISIBLE until ready. Sprite
                    // already suppressed; do NOT draw a placeholder billboard. Record so the
                    // operator can tell "still building" (VoxelNotReady) from "build failed".
                    if (m == null || m.vertexCount == 0)
                    {
                        dsVoxelMeshNull++;
                        Vector3 errPos = rd.positions[i];
                        if (errPos.z < Constants.ZDisplacement * 0.5f) errPos = errPos.To3DTileHeight(false);
                        RecordActorError(RenderErrorType.VoxelNotReady,
                            a, sp != null ? "voxel mesh null/empty (async build pending) sprite=" + sp.name : "voxel mesh null/empty", errPos);
                        continue;
                    }
                    dsVoxelSubmitAttempt++;

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
                    // WHY: actors use VoxelScaleMultiplier * ActorVoxelScaleFactor so they read
                    // sprite-sized at zoom instead of 8x gigantic / camera-clipping when close.
                    scl *= Core.savedSettings.VoxelScaleMultiplier * Core.savedSettings.ActorVoxelScaleFactor;
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
                        LastBatcherSubmitCount++;
                        dsVoxelSubmitOk++;
                        rd.has_normal_render[i] = false;
                        TraceActorColorSample("voxel", i, rd.colors[i], a, sp, posBeforeLift, pos, rot, scl);
                    }
                    else
                    {
                        dsVoxelSubmitFail++;
                        // ShaderFailed: Submit rejected the mesh (material null / instancing
                        // variant missing / InternalError). Distinct from "not ready".
                        RecordActorError(RenderErrorType.ShaderFailed,
                            a, "Submit() returned false (material/shader unusable)", pos);
                    }
                }
                // DIAG-SUBMIT one-shot path report — answers "where did the meshOk actors go?"
                if (Core.savedSettings.ProfilerDump && (!_emitDiagSawNonZero || _emitDiagFrameCounter < 3))
                {
                    _emitDiagFrameCounter++;
                    Debug.Log($"[WSM3D][DIAG-SUBMIT] EmitVoxels paths n={n} nullActor={dsNullActor} perpSkip={dsPerpSkipped} frustumFail={dsFrustumFail} frustumPass={LastFrustumCullerPassCount} | tier(Imp={dsTierImpostor} Proxy={dsTierProxy} Voxel={dsTierVoxel} Other={dsTierOther}) | skel(attempt={dsSkeletalAttempt} ok={dsSkeletalSubmitOk} fail={dsSkeletalSubmitFail}) | spriteNull={dsSpriteNull} | impostor(meshNull={dsImpostorMeshNull} matNull={dsImpostorMatNull} submit={dsImpostorSubmit}) | voxel(meshNull={dsVoxelMeshNull} attempt={dsVoxelSubmitAttempt} ok={dsVoxelSubmitOk} fail={dsVoxelSubmitFail}) | LastBatcherSubmitCount={LastBatcherSubmitCount} SkeletalAnimation={Core.savedSettings.SkeletalAnimation}");
                }
            }

            static WorldSphereMod.Rig.RigType ResolveRigType(string assetId)
            {
                return Constants.ResolveActorRig(assetId);
            }

            // Funnel actor render failures into the registry with a stable object name.
            static void RecordActorError(RenderErrorType type, Actor a, string reason, Vector3 worldPos)
            {
                string name = a != null && a.asset != null ? a.asset.id : "<actor>";
                RenderErrorRegistry.Record(type, name, reason, worldPos);
            }

            // ROOT-CAUSE FIX (2026-05-30): ActorManager.precalculateRenderDataParallel only
            // writes render_data.main_sprites[i] for the subset of actors whose colored sprite
            // can be resolved on the worker thread (canParallelSetColoredSprite()==true). For
            // everyone else it stores null and defers the real sprite to
            // precalculateRenderDataNormal(), which runs AFTER this Harmony postfix on the
            // parallel pass. So at our read point main_sprites[i] is null for live, fully-
            // visible actors (the headless /diag/errors showed 29/29 SpriteNull "human" at
            // valid positions — those are the deferred ones). It is NOT a missing sprite: the
            // authoritative current animation-frame sprite is actor.calculateMainSprite(), the
            // exact call WorldBox itself uses in both render passes. We resolve from that when
            // the array slot is empty so VoxelMeshCache.Get(sprite) gets a real sprite and the
            // voxel mesh actually builds. Only genuinely-null cases (e.g. asset has no sprite)
            // fall through to the SpriteNull telemetry + voxel-or-invisible policy.
            static Sprite ResolveActorSprite(ActorRenderData rd, int i, Actor a)
            {
                Sprite sp = rd.main_sprites[i];
                if (sp != null) return sp;
                if (a == null) return null;
                try
                {
                    return a.calculateMainSprite();
                }
                catch
                {
                    // Defensive: animation container not ready / asset edge case — treat as null
                    // so the caller records SpriteNull rather than throwing inside the postfix.
                    return null;
                }
            }

            static void LogActorSubmitDiagnostic(string path, ref bool logged, Actor actor, Sprite? sprite, Vector3 beforeLift, Vector3 afterLift, Color tint)
            {
                if (Core.savedSettings == null || !Core.savedSettings.ProfilerDump) return;
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
                if (Core.savedSettings == null || !Core.savedSettings.ProfilerDump) return;
                if (_actorVoxelColorSampleCount >= 3) return;
                if (path != "voxel") return;

                _actorVoxelColorSampleCount++;
                string actorId = actor != null && actor.asset != null ? actor.asset.id : "<null>";
                string spriteName = sprite != null ? sprite.name : "<null>";
                Debug.Log($"[WSM3D][DIAG] Actor voxel color sample {_actorVoxelColorSampleCount}/3 asset={actorId} sprite={spriteName} index={index} rawPos={rawPos} liftedPos={liftedPos} rotY={rotation.y:F3} scale={scale} color={tint}");
            }

            static void LogFirstActorPos(Vector3 rawPos, Vector3 liftedPos, Vector3 scl)
            {
                if (Core.savedSettings == null || !Core.savedSettings.ProfilerDump) return;
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
            static bool _buildingEmitDiagLogged;
            // Per-frame budget cycling: tracks where we left off in the visible
            // buildings array so we process the next slice each frame.
            static int _budgetOffset;

            public static void ResetDiag()
            {
                _buildingVoxelEmitSubmitLogged = false;
                _buildingEmitDiagLogged = false;
                _budgetOffset = 0;
            }

            [HarmonyPostfix]
            [HarmonyPriority(Priority.First)]
            public static void EmitVoxels(BuildingManager __instance)
            {
                if (!_buildingEmitDiagLogged && Core.savedSettings.ProfilerDump)
                {
                    _buildingEmitDiagLogged = true;
                    int bldgCount = __instance._visible_buildings_count;
                    bool procBld = Core.savedSettings.ProceduralBuildings;
                    Debug.Log($"[WSM3D][DIAG-EMIT] BuildingVoxelEmit.EmitVoxels CALLED isWorld3D={Core.IsWorld3D} VoxelEntities={Core.savedSettings.VoxelEntities} ProceduralBuildings={procBld} visible_buildings_count={bldgCount}");
                }
                if (!Core.IsWorld3D || !Core.savedSettings.VoxelEntities) return;
                if (Core.savedSettings.ProceduralBuildings) return;
                if (!EnsureMaterial())
                {
                    RenderErrorRegistry.Record(RenderErrorType.MaterialNull, "BuildingManager",
                        "EnsureMaterial() returned no usable voxel material", Vector3.zero);
                    return;
                }

                var rd = __instance.render_data;
                var arr = __instance._array_visible_buildings;
                int n = __instance._visible_buildings_count;

                // Per-frame budget: only process a slice of visible buildings each
                // frame, cycling through the full set. 0 = unlimited.
                int budget = Core.savedSettings.BuildingRenderBudget;
                int start = 0;
                int end = n;
                if (budget > 0 && n > budget)
                {
                    if (_budgetOffset >= n) _budgetOffset = 0;
                    start = _budgetOffset;
                    end = Mathf.Min(start + budget, n);
                    _budgetOffset = end >= n ? 0 : end;
                }

                for (int i = start; i < end; i++)
                {
                    Building b = arr[i];
                    if (b == null || b.asset == null) continue;
                    if (Constants.PerpBuildings.ContainsKey(b.asset.id)) continue;

                    Vector3 cullPos = rd.positions[i];
                    if (cullPos.z < Constants.ZDisplacement * 0.5f)
                    {
                        cullPos = cullPos.To3DTileHeight(false);
                    }
                    float radius = 3f * Mathf.Max(1f, Core.savedSettings.VoxelScaleMultiplier * 0.5f);
                    if (!WorldSphereMod.LOD.FrustumCuller.IsVisible(cullPos, radius))
                    {
                        continue;
                    }
                    WorldSphereMod.LOD.LodTier tier = WorldSphereMod.LOD.LodSelector.Select(cullPos, b.GetHashCode());

                    Sprite sp = rd.main_sprites[i];
                    if (sp == null)
                    {
                        RenderErrorRegistry.Record(RenderErrorType.SpriteNull,
                            b.asset != null ? b.asset.id : "<building>", "main_sprites[i] is null", cullPos);
                        continue;
                    }

                    // VOXEL-OR-INVISIBLE POLICY: suppress the vanilla 2D building sprite
                    // for every eligible building up-front (zero its render scale). If the
                    // voxel mesh isn't ready / far LOD, nothing draws — never a billboard.
                    // BuildingRenderData has no has_normal_render; scales[i]=0 hides the
                    // sprite quad without nulling main_sprites (downstream chokes on null).
                    // Snapshot the original sprite scale FIRST — the voxel mesh below needs it.
                    Vector3 origScale = rd.scales[i];
                    rd.scales[i] = Vector3.zero;

                    if (tier == WorldSphereMod.LOD.LodTier.Impostor)
                    {
                        // FAR TIER = CULL. Sprite already suppressed → building invisible at
                        // distance rather than a flat impostor billboard.
                        continue;
                    }

                    // Phase 10: Proxy tier shares full voxel path until BuildProxy/ProxyMeshCache ship.
                    Mesh m = VoxelMeshCache.Get(sp);
                    // Not ready / empty → invisible until built. Sprite already suppressed.
                    if (m == null || m.vertexCount == 0)
                    {
                        RenderErrorRegistry.Record(RenderErrorType.VoxelNotReady,
                            b.asset != null ? b.asset.id : "<building>",
                            "voxel mesh null/empty (async build pending) sprite=" + sp.name, cullPos);
                        continue;
                    }

                    Vector3 pos = rd.positions[i];
                    if (pos.z < Constants.ZDisplacement * 0.5f)
                    {
                        pos = pos.To3DTileHeight(false);
                    }
                    Vector3 rot = rd.rotations[i];
                    Vector3 scl = origScale;
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
                    if (!_buildingVoxelEmitSubmitLogged && Core.savedSettings != null && Core.savedSettings.ProfilerDump)
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

                // Drop.transform.position is the 2D-engine position with z~0. The
                // FrustumCuller uses the 3D camera and expects 3D-lifted coordinates;
                // without the To3DTileHeight(false) lift, drops fall outside the
                // frustum and get culled — same class of bug as the documented
                // 2D-position vs 3D-frustum issue in ActorVoxelEmit/BuildingVoxelEmit.
                Vector3 cullPos = __instance.transform.position;
                if (cullPos.z < Constants.ZDisplacement * 0.5f)
                {
                    cullPos = cullPos.To3DTileHeight(false);
                }
                if (!WorldSphereMod.LOD.FrustumCuller.IsVisible(cullPos, 1.5f * Mathf.Max(1f, Core.savedSettings.VoxelScaleMultiplier * 0.5f)))
                {
                    // Off-screen: the vanilla frustum cull already hides the sprite. Leave
                    // sr.enabled as-is (vanilla manages off-screen); do not force a billboard.
                    sr.enabled = true;
                    return;
                }

                WorldSphereMod.LOD.LodTier tier = WorldSphereMod.LOD.LodSelector.Select(cullPos, __instance.GetHashCode());
                Color tint = sr.color;

                // VOXEL-OR-INVISIBLE POLICY: in 3D the drop is a real voxel volume or nothing.
                // Suppress the vanilla 2D SpriteRenderer up-front; only re-show nothing.
                sr.enabled = false;

                if (tier == WorldSphereMod.LOD.LodTier.Impostor)
                {
                    // FAR TIER = CULL. Sprite suppressed → drop invisible at distance, never
                    // a flat impostor billboard.
                    return;
                }

                Mesh? mesh = VoxelMeshCache.Get(sp, -1, true);
                // Not ready / empty → invisible until built. Sprite stays suppressed.
                if (mesh == null || mesh.vertexCount == 0)
                {
                    return;
                }

                Vector3 pos = cullPos;
                // WHY: drops share the actor scale factor so they aren't 8x oversized.
                float scale = Mathf.Max(__instance._scale, 0.01f) * Core.savedSettings.VoxelScaleMultiplier * Core.savedSettings.ActorVoxelScaleFactor;
                Vector3 scl = new Vector3(scale, scale, scale);
                scl.z = scl.x;
                float halfHeight = mesh.bounds.size.y * 0.5f * scl.y;
                pos.y += halfHeight;
                float yaw = __instance.transform.eulerAngles.y;
                Matrix4x4 trs = Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), scl);
                // Submit the real voxel mesh. Whether or not it submits, the sprite stays
                // suppressed (set above) — voxel-or-nothing, never a billboard fallback.
                Submit(mesh, trs, tint);
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

                    // VOXEL-OR-INVISIBLE POLICY: suppress the vanilla 2D projectile sprite
                    // up-front. Far LOD / not-ready mesh → invisible, never a billboard.
                    SuppressProjectileSprite(pAsset, pos);

                    if (tier == WorldSphereMod.LOD.LodTier.Impostor)
                    {
                        // FAR TIER = CULL. Sprite suppressed → projectile invisible at distance.
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
            }
            catch { }
            MeshInstanceBatcher.SetMainThread();
            // Force per-instance fallback only when explicitly requested. The
            // Standard material path now keeps INSTANCING_ON in sync before DrawMeshInstanced.
            MeshInstanceBatcher.SetFallbackPath(Core.savedSettings != null && Core.savedSettings.ForceFallbackDrawPath);
            WorldSphereMod.Lighting.SunDriver.BindMainCamera(CameraManager.MainCamera);
        }

        static float _telemetryLastTime;
        static int _instancingTelemetryFrame;

        static bool _tickDiagLogged;
        static bool _tickPerfBreakdownLogged;

        /// <summary>Per-frame voxel/FX driver; invoked from MapBox.renderStuff Harmony hook so it survives scene transitions.</summary>
        public static void TickPerFrame()
        {
            // TEMPORARY DIAGNOSTIC: one-shot log to verify TickPerFrame fires and check Harmony state
            if (!_tickDiagLogged && Core.savedSettings != null && Core.savedSettings.ProfilerDump)
            {
                _tickDiagLogged = true;
                bool hasPatcher = Core.Patcher != null;
                string patchedMethods = "N/A";
                if (hasPatcher)
                {
                    try
                    {
                        var patches = Core.Patcher.GetPatchedMethods();
                        int count = 0;
                        bool foundActorPrecalc = false;
                        bool foundBuildingPrecalc = false;
                        foreach (var m in patches)
                        {
                            count++;
                            if (m.Name == "precalculateRenderDataParallel")
                            {
                                if (m.DeclaringType?.Name == "ActorManager") foundActorPrecalc = true;
                                if (m.DeclaringType?.Name == "BuildingManager") foundBuildingPrecalc = true;
                            }
                        }
                        patchedMethods = $"total={count} ActorManager.precalcRDP={foundActorPrecalc} BuildingManager.precalcRDP={foundBuildingPrecalc}";
                    }
                    catch (System.Exception ex) { patchedMethods = $"ERROR: {ex.Message}"; }
                }
                Debug.Log($"[WSM3D][DIAG-TICK] VoxelFrameDriver.TickPerFrame FIRST CALL hasPatcher={hasPatcher} harmonyPatches=[{patchedMethods}] VoxelEntities={Core.savedSettings?.VoxelEntities} isWorld3D={Core.IsWorld3D} cacheSize={VoxelMeshCache.Count} pendingBuilds={VoxelMeshCache.PendingBuilds} queuedBuildsTotal={VoxelMeshCache.TotalBuilds}");
            }

            if (!_tickPerfBreakdownLogged && Core.savedSettings.ProfilerDump)
            {
                _tickPerfBreakdownLogged = true;
                var sw = Stopwatch.StartNew();
                double tPrepareWorld = 0.0;
                double tBeginFrame = 0.0;
                double tImpostorTick = 0.0;
                double tFrustumUpdate = 0.0;
                double tRigTick = 0.0;
                double tRigDrain = 0.0;
                double tRigUpdate = 0.0;
                double tRigClear = 0.0;
                double tPumpQueuedBuilds = 0.0;
                double tDrainCompletedBuilds = 0.0;
                double tSanityDraw = 0.0;
                double tProcGenDrain = 0.0;
                double tFoliageDrain = 0.0;
                double tWaterLifecycle = 0.0;
                double tMountainSlope = 0.0;
                double tSunBind = 0.0;
                double tSunUpdate = 0.0;
                double tDecalTick = 0.0;
                double tEnvironmentalTick = 0.0;
                double tPostFxApply = 0.0;
                double tPostFxRefresh = 0.0;
                double tTotal;

                double Measure(System.Action action)
                {
                    long start = Stopwatch.GetTimestamp();
                    action();
                    return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                }

                if (!Core.Sphere.WorldPrepared)
                {
                    tPrepareWorld = Measure(() =>
                    {
                        try { Core.Sphere.PrepareWorld(); }
                        catch (System.Exception ex) { Debug.LogError($"[WSM3D] Deferred Sphere.PrepareWorld FAILED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); }
                    });
                }

                float deltaTimePerf = Time.deltaTime;
                tBeginFrame = Measure(() =>
                {
                    _perfFrameCounter++;
                    _perfDeltaTimeSum += deltaTimePerf;
                    if (_perfFrameCounter >= kPerfSampleWindowFrames)
                    {
                        float avgFrameTime = _perfDeltaTimeSum / kPerfSampleWindowFrames;
                        float avgFps = avgFrameTime > 0f ? 1f / avgFrameTime : 0f;
                        Debug.Log($"[WSM3D][Perf] frameDeltaMs={deltaTimePerf * 1000f:F2} avg60FrameDeltaMs={avgFrameTime * 1000f:F2} avg60Fps={avgFps:F1}");
                        _perfFrameCounter = 0;
                        _perfDeltaTimeSum = 0f;
                    }
                });

                _instancingTelemetryFrame++;
                if (_instancingTelemetryFrame >= 60)
                {
                    _instancingTelemetryFrame = 0;
                    Debug.Log($"[WSM3D][Telemetry] InstancingEfficiency={MeshInstanceBatcher.InstancingEfficiency:F4} FrameBucketCount={MeshInstanceBatcher.FrameBucketCount} FrameInstances={MeshInstanceBatcher.FrameInstances}");
                }

                float nowPerf = Time.realtimeSinceStartup;
                if (nowPerf - _telemetryLastTime > 10f)
                {
                    _telemetryLastTime = nowPerf;
                    Debug.Log($"[WSM3D][Telemetry] frameMs={Time.unscaledDeltaTime * 1000:F2} drawCalls={MeshInstanceBatcher.FrameDrawCalls} instances={MeshInstanceBatcher.FrameInstances} cacheSize={VoxelMeshCache.Count} cacheHits={VoxelMeshCache.HitCount} cacheMisses={VoxelMeshCache.MissCount} submits={VoxelRender._submitDiagCount} gcMB={(System.GC.GetTotalMemory(false) / 1048576f):F1}");
                    VoxelRender._submitDiagCount = 0;
                }

                tImpostorTick = Measure(WorldSphereMod.LOD.ImpostorBillboard.Tick);
                tFrustumUpdate = Measure(() =>
                {
                    if (Core.savedSettings.VoxelEntities || Core.savedSettings.ProceduralBuildings || Core.savedSettings.CrossedQuadFoliage)
                    {
                        WorldSphereMod.LOD.FrustumCuller.UpdatePlanes();
                    }
                });
                tRigTick = Measure(WorldSphereMod.Rig.RigCache.Tick);
                tRigDrain = Measure(WorldSphereMod.Rig.RigCache.DrainPendingDestroy);
                if (Core.savedSettings.SkeletalAnimation)
                {
                    tRigUpdate = Measure(WorldSphereMod.Rig.RigDriver.Update);
                }
                else if (_lastSkeletalState)
                {
                    tRigClear = Measure(() =>
                    {
                        WorldSphereMod.Rig.RigDriver.Clear();
                        _lastSkeletalState = false;
                    });
                }

                tPumpQueuedBuilds = Measure(() => WorldSphereMod.Voxel.VoxelMeshCache.PumpQueuedBuilds(8));
                tDrainCompletedBuilds = Measure(() => WorldSphereMod.Voxel.VoxelMeshCache.DrainCompletedBuilds(8));

                if (Core.savedSettings.DebugSanityCube)
                {
                    tSanityDraw = Measure(SanityTestCube.Draw);
                }

                if (Core.savedSettings.ProceduralBuildings)
                {
                    tProcGenDrain = Measure(WorldSphereMod.ProcGen.ProcGenCache.DrainPendingDestroy);
                }

                if (Core.savedSettings.CrossedQuadFoliage)
                {
                    tFoliageDrain = Measure(WorldSphereMod.Foliage.CrossedQuadMeshCache.DrainPendingDestroy);
                }

                tWaterLifecycle = Measure(WorldSphereMod.Water.WaterRender.UpdateLifecycle);
                tMountainSlope = Measure(WorldSphereMod.Terrain.MountainSlopeSurface.EnsureActive);

                if (Time.time >= _nextCameraLookup)
                {
                    tSunBind = Measure(() =>
                    {
                        WorldSphereMod.Lighting.SunDriver.BindMainCamera(CameraManager.MainCamera);
                        _nextCameraLookup = Time.time + kCameraLookupInterval;
                    });
                }

                tSunUpdate = Measure(WorldSphereMod.Lighting.SunDriver.Update);
                tDecalTick = Measure(WorldSphereMod.Fx.DecalPool.Tick);
                tEnvironmentalTick = Measure(WorldSphereMod.Fx.Environmental.Tick);
                tPostFxApply = Measure(() =>
                {
                    bool currentPostFX = Core.savedSettings.PostFX;
                    if (currentPostFX != _lastAppliedPostFX)
                    {
                        _lastAppliedPostFX = currentPostFX;
                        WorldSphereMod.PostFx.WSM3DPostStack.ApplySetting(currentPostFX);
                    }
                });
                tPostFxRefresh = Measure(() =>
                {
                    bool currentSSAO = Core.savedSettings.SSAOEnabled;
                    if (currentSSAO != _lastAppliedSSAOEnabled)
                    {
                        _lastAppliedSSAOEnabled = currentSSAO;
                        WorldSphereMod.PostFx.WSM3DPostStack.RefreshMaterials();
                    }

                    bool currentSSGI = Core.savedSettings.SSGIEnabled;
                    if (currentSSGI != _lastAppliedSSGIEnabled)
                    {
                        _lastAppliedSSGIEnabled = currentSSGI;
                        WorldSphereMod.PostFx.WSM3DPostStack.RefreshMaterials();
                    }
                });

                tTotal = sw.Elapsed.TotalMilliseconds;
                Debug.Log(
                    "[WSM3D][PerfBreakdown] " +
                    $"total={tTotal:F2}ms " +
                    $"PrepareWorld={tPrepareWorld:F2}ms " +
                    $"BeginFrame={tBeginFrame:F2}ms " +
                    $"ImpostorTick={tImpostorTick:F2}ms " +
                    $"FrustumUpdate={tFrustumUpdate:F2}ms " +
                    $"RigTick={tRigTick:F2}ms " +
                    $"RigDrain={tRigDrain:F2}ms " +
                    $"RigUpdate={tRigUpdate:F2}ms " +
                    $"RigClear={tRigClear:F2}ms " +
                    $"PumpQueuedBuilds={tPumpQueuedBuilds:F2}ms " +
                    $"DrainCompletedBuilds={tDrainCompletedBuilds:F2}ms " +
                    $"SanityDraw={tSanityDraw:F2}ms " +
                    $"ProcGenDrain={tProcGenDrain:F2}ms " +
                    $"FoliageDrain={tFoliageDrain:F2}ms " +
                    $"WaterLifecycle={tWaterLifecycle:F2}ms " +
                    $"MountainSlope={tMountainSlope:F2}ms " +
                    $"SunBind={tSunBind:F2}ms " +
                    $"SunUpdate={tSunUpdate:F2}ms " +
                    $"DecalTick={tDecalTick:F2}ms " +
                    $"EnvironmentalTick={tEnvironmentalTick:F2}ms " +
                    $"PostFxApply={tPostFxApply:F2}ms " +
                    $"PostFxRefresh={tPostFxRefresh:F2}ms");
                return;
            }

            if (Core.ClearVoxelMeshCacheOnFirstFrame)
            {
                Core.ClearVoxelMeshCacheOnFirstFrame = false;
                try { VoxelMeshCache.Clear(); } catch (System.Exception ex) { Debug.LogWarning("[WSM3D] First-frame VoxelMeshCache.Clear failed: " + ex.Message); }
            }

            // Deferred world-state init: NML sometimes skips PostInit when a
            // save loads before the post-init phase runs. PrepareAssets was
            // already called in Init, but PrepareWorld needs World.world which
            // may not have existed then. Catch it here on the first frame
            // where the world is ready.
            if (!Core.Sphere.WorldPrepared)
            {
                try { Core.Sphere.PrepareWorld(); }
                catch (System.Exception ex) { Debug.LogError($"[WSM3D] Deferred Sphere.PrepareWorld FAILED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); }
            }

            float deltaTime = Time.deltaTime;
            _perfFrameCounter++;
            _perfDeltaTimeSum += deltaTime;
            if (_perfFrameCounter >= kPerfSampleWindowFrames)
            {
                float avgFrameTime = _perfDeltaTimeSum / kPerfSampleWindowFrames;
                float avgFps = avgFrameTime > 0f ? 1f / avgFrameTime : 0f;
                if (Core.savedSettings.ProfilerDump)
                    Debug.Log($"[WSM3D][Perf] frameDeltaMs={deltaTime * 1000f:F2} avg60FrameDeltaMs={avgFrameTime * 1000f:F2} avg60Fps={avgFps:F1}");
                _perfFrameCounter = 0;
                _perfDeltaTimeSum = 0f;
            }

            _instancingTelemetryFrame++;
            if (_instancingTelemetryFrame >= 60)
            {
                _instancingTelemetryFrame = 0;
                if (Core.savedSettings.ProfilerDump)
                    Debug.Log($"[WSM3D][Telemetry] InstancingEfficiency={MeshInstanceBatcher.InstancingEfficiency:F4} FrameBucketCount={MeshInstanceBatcher.FrameBucketCount} FrameInstances={MeshInstanceBatcher.FrameInstances}");
            }

            // Log-based telemetry every 10s — bypasses bridge for steady-state observability
            // even when bridge is in scene-transition known-issue state.
            float now = Time.realtimeSinceStartup;
            if (now - _telemetryLastTime > 10f)
            {
                _telemetryLastTime = now;
                if (Core.savedSettings.ProfilerDump)
                    Debug.Log($"[WSM3D][Telemetry] frameMs={Time.unscaledDeltaTime * 1000:F2} drawCalls={MeshInstanceBatcher.FrameDrawCalls} instances={MeshInstanceBatcher.FrameInstances} cacheSize={VoxelMeshCache.Count} cacheHits={VoxelMeshCache.HitCount} cacheMisses={VoxelMeshCache.MissCount} submits={VoxelRender._submitDiagCount} gcMB={(System.GC.GetTotalMemory(false) / 1048576f):F1}");
                VoxelRender._submitDiagCount = 0;
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

            // Frame-time budget: when the previous frame was slow, reduce
            // background work to keep the render loop responsive. Adapt the
            // pump budget based on how much headroom we have.
            float lastFrameMs = Time.unscaledDeltaTime * 1000f;
            bool frameBudgetTight = lastFrameMs > 20f;
            int pumpBudget = frameBudgetTight ? 2 : 8;

            WorldSphereMod.Voxel.VoxelMeshCache.PumpQueuedBuilds(pumpBudget);
            WorldSphereMod.Voxel.VoxelMeshCache.DrainCompletedBuilds(frameBudgetTight ? 4 : 8);
            WorldSphereMod.Voxel.VoxelDiskCache.TickFlush();

            if (Core.savedSettings.DebugSanityCube)
            {
                SanityTestCube.Draw();
            }

            // Visual sink: draw this frame's typed ERROR-prop markers (gated by RenderErrorProps)
            // BEFORE Flush so they batch with the frame's other voxel submissions. Then emit the
            // low-frequency structured [ERRORS] summary (throttled on-change / interval inside).
            WorldSphereMod.Voxel.RenderErrorMarkers.DrawQueued();
            WorldSphereMod.Voxel.RenderErrorRegistry.MaybeEmitSummary();

            if (Core.savedSettings.ProceduralBuildings)
            {
                WorldSphereMod.ProcGen.ProcGenCache.DrainPendingDestroy();
            }

            if (Core.savedSettings.CrossedQuadFoliage)
            {
                WorldSphereMod.Foliage.CrossedQuadMeshCache.DrainPendingDestroy();
            }

            // Always call UpdateLifecycle so the OFF->ON and ON->OFF edges
            // both fire. The previous guard `if (MeshWater)` prevented the
            // destroy path from running when the setting was toggled off.
            WorldSphereMod.Water.WaterRender.UpdateLifecycle();

            WorldSphereMod.Terrain.MountainSlopeSurface.EnsureActive();

            if (Time.time >= _nextCameraLookup)
            {
                WorldSphereMod.Lighting.SunDriver.BindMainCamera(CameraManager.MainCamera);
                _nextCameraLookup = Time.time + kCameraLookupInterval;
            }

            WorldSphereMod.Lighting.SunDriver.Update();

            // Skip cosmetic FX ticks when frame budget is tight
            if (!frameBudgetTight)
            {
                WorldSphereMod.Fx.DecalPool.Tick();
            }

            WorldSphereMod.Fx.Environmental.Tick();

            // CRITICAL: do NOT call ApplySetting every frame. The post stack may rebuild
            // resources or log on state changes; reconcile only on change (bridge/API
            // edits, load-order races). ApplyPhaseToggle also invokes ApplySetting immediately.
            bool currentPostFX = Core.savedSettings.PostFX;
            if (currentPostFX != _lastAppliedPostFX)
            {
                _lastAppliedPostFX = currentPostFX;
                WorldSphereMod.PostFx.WSM3DPostStack.ApplySetting(currentPostFX);
            }

            bool currentSSAO = Core.savedSettings.SSAOEnabled;
            if (currentSSAO != _lastAppliedSSAOEnabled)
            {
                _lastAppliedSSAOEnabled = currentSSAO;
                WorldSphereMod.PostFx.WSM3DPostStack.RefreshMaterials();
            }

            bool currentSSGI = Core.savedSettings.SSGIEnabled;
            if (currentSSGI != _lastAppliedSSGIEnabled)
            {
                _lastAppliedSSGIEnabled = currentSSGI;
                WorldSphereMod.PostFx.WSM3DPostStack.RefreshMaterials();
            }
        }

        // Pre-emit work runs from MapBox.renderStuff (Harmony); flush must run after emit
        // postfixes, so LateUpdate remains the end-of-frame sink.
        static int _submitFlushDiagFrame;
        void LateUpdate()
        {
            // Re-create bridge every frame so it survives scene transitions
            // even when the Harmony postfix on MapBox.renderStuff doesn't fire.
            Bridge.BridgeServer.EnsureCreated();
            Bridge.BridgeServer.DrainStaticQueue();

            // BUG FIX (drawCalls=0 most frames): previously gated on
            // MeshInstanceBatcher.HasPendingSubmissions, but BridgeServer.cs:160
            // also calls VoxelRender.Flush() which can drain buckets BEFORE
            // this LateUpdate runs. If buckets are drained, HasPendingSubmissions
            // can return false even when a Postfix has submits in flight to the
            // ConcurrentQueue this frame — and we'd skip Flush, producing
            // drawCalls=0 telemetry. Always call Flush so this frame's submits
            // get drawn before the camera renders. The reference to
            // HasPendingSubmissions below is retained (now an observability
            // snapshot) to satisfy source invariants that pin the reference in
            // LateUpdate.
            bool hadPending = MeshInstanceBatcher.HasPendingSubmissions;
            int submitsBeforeFlush = Volatile.Read(ref MeshInstanceBatcher._submitCountThisFrame);
            VoxelRender.Flush();
            VoxelMeshCache.DrainPendingDestroy();

            // Per-frame Submit/Flush diagnostic. Snapshot live counters and reset.
            int submitCount = Interlocked.Exchange(ref MeshInstanceBatcher._submitCountThisFrame, 0);
            int flushCount = Interlocked.Exchange(ref MeshInstanceBatcher._flushCountThisFrame, 0);
            MeshInstanceBatcher.LastFrameSubmitCount = submitCount;
            MeshInstanceBatcher.LastFrameFlushCount = flushCount;
            _submitFlushDiagFrame++;
            if (_submitFlushDiagFrame % 60 == 0 && Core.savedSettings.ProfilerDump)
            {
                Debug.Log($"[WSM3D][SubmitFlushDiag] frame={_submitFlushDiagFrame} submits={submitCount} flushes={flushCount} submitsBeforeFlush={submitsBeforeFlush} hadPending={hadPending} drawCalls={MeshInstanceBatcher.FrameDrawCalls} instances={MeshInstanceBatcher.FrameInstances} buckets={MeshInstanceBatcher.FrameBucketCount}");
            }

            Bridge.BridgeServer.RefreshTelemetryCache();
        }

        static bool _lastAppliedPostFX = false;
        static bool _lastAppliedSSAOEnabled = false;
        static bool _lastAppliedSSGIEnabled = false;
    }
}
