using System;
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
        /// <summary>Phase 7: fired when worldspace UI detects HP loss on a visible actor.</summary>
        public static event System.Action<Actor, int>? OnActorDamaged;

        public static void NotifyActorDamaged(Actor actor, int damageAmount)
        {
            if (actor == null || damageAmount <= 0) return;
            OnActorDamaged?.Invoke(actor, damageAmount);
        }

        const float BuildingMaxScale = 3.0f;
        internal static Material? _material;
        static bool _materialAttempted;
        static bool _materialProbeLogged;
        static bool _materialDebugLogged;
        static bool _firstActorPosLogged;
        static int _actorVoxelColorSampleCount;
        static bool _actorVoxelDiagnosticLogged;
        static bool _actorSkeletalDiagnosticLogged;
        static bool _actorSpriteDrawDiagLogged;
        static int _actorDrawDiagThrottle;
        static bool _normalRenderDiagLogged;
        static readonly List<Vector3> _actorVoxelSubmitTranslations = new(5);
        static readonly HashSet<int> _normalCheckedMeshes = new HashSet<int>();
        static readonly Dictionary<ActorSpriteCardMeshKey, Mesh> _actorSpriteCardMeshes = new();
        static readonly Dictionary<Texture2D, Material> _actorSpriteCardMaterials = new();
        static readonly Dictionary<ActorSpriteCardBatchKey, List<Matrix4x4>> _actorSpriteCardBatches = new(); // BACK (emit fills)
        // FRONT buffer: the per-frame flush redraws this every frame. Replaced wholesale at emit-end.
        static readonly Dictionary<ActorSpriteCardBatchKey, List<Matrix4x4>> _actorSpriteCardFront = new();
        // CUMULATIVE actor draw counter — NOTHING else resets this (unlike MeshInstanceBatcher.
        // FrameDrawCalls which the batcher zeroes mid-frame, corrupting the bridge reading). Lets us
        // measure whether the actor flush actually draws every frame: sample twice, delta should
        // grow by ~1/frame steadily if drawing stably, or stay flat/jump if flickering.
        public static long ActorDrawCallsCumulative;
        public static int ActorFrontCount;
        // Render-foundation machine handle: vert count of the last representative
        // actor voxel mesh submitted (excludes the 8-vert SanityTestCube). The bridge
        // reads this to confirm real volume (>8 verts) vs a flat 4-vert sprite quad,
        // without pixels. Set on Submit; cheap (one int write).
        public static int LastActorMeshVertCount;
        static readonly object _actorSpriteCardBatchLock = new();
        static readonly Matrix4x4[] _actorSpriteCardMatrices = new Matrix4x4[1023];
        const int MaxSpriteCardInstancedBatch = 1023;
        static readonly int _actorSpriteCardMainTexId = Shader.PropertyToID("_MainTex");
        static readonly int _actorSpriteCardColorId = Shader.PropertyToID("_Color");
        static readonly int _actorSpriteCardBaseColorId = Shader.PropertyToID("_BaseColor");

        public static event Action<Actor, int> OnActorDamaged;
        public static void NotifyActorDamaged(Actor a, int damage) => OnActorDamaged?.Invoke(a, damage);

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
            if (_material != null) UnityEngine.Object.Destroy(_material);
            _material = null;
            _materialAttempted = false;
            _materialProbeLogged = false;
            SanityTestCube.Reset();
            _materialDebugLogged = false;
            _firstActorPosLogged = false;
            _actorVoxelDiagnosticLogged = false;
            _actorVoxelColorSampleCount = 0;
            _actorSkeletalDiagnosticLogged = false;
            _actorSpriteDrawDiagLogged = false;
            _actorVoxelSubmitTranslations.Clear();
            _normalCheckedMeshes.Clear();
            ClearActorSpriteCardState();
            _flushDiagLogged = false;
            _submitDiagLogged = false;
            ActorVoxelEmit.ResetDiag();
            BuildingVoxelEmit.ResetDiag();
            // Clear render-error telemetry + queued markers on world reload — stale counts
            // from a prior world would otherwise pollute /diag/errors.
            RenderErrorRegistry.Reset();
            RenderErrorMarkers.Reset();
        }

        static void ClearActorSpriteCardState()
        {
            lock (_actorSpriteCardBatchLock)
            {
                foreach (var kv in _actorSpriteCardMeshes)
                {
                    if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
                }
                _actorSpriteCardMeshes.Clear();

                foreach (var kv in _actorSpriteCardMaterials)
                {
                    if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
                }
                _actorSpriteCardMaterials.Clear();

                foreach (var batch in _actorSpriteCardBatches.Values)
                {
                    batch.Clear();
                }
                _actorSpriteCardBatches.Clear();
            }
        }

        /// <summary>
        /// Resolve a material capable of rendering the voxel mesh's per-vertex colors.
        /// RENDER-FOUNDATION CHANGE: prefer the built-in shader fallback chain
        /// (Mobile/VertexLit, Standard, Mobile/Diffuse, Diffuse) so the actor
        /// surface no longer depends on the broken wsm3d-shaders AssetBundle.
        /// </summary>
        public static bool EnsureMaterial()
        {
            if (_materialAttempted || _material != null)
            {
                if (MeshInstanceBatcher.UseFallbackPath && _material != null && _material.enableInstancing)
                {
                    _material.enableInstancing = false;
                }
                return _material != null;
            }
            _materialAttempted = true;

            // 60f1 ships a STRIPPED shader set: Particles/*, URP/*, Unlit/* all
            // return null at runtime, so reaching for them produced null → magenta
            // voxels. Walk the BUILT-IN chain defined in Core.Sphere
            // (Mobile/VertexLit → Standard → Mobile/Diffuse → Diffuse) — no bundle
            // dependency.
            string[] candidates = Core.Sphere.BuiltInShaderFallbacks;
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
                Material skybox = RenderSettings.skybox;
                if (skybox != null && skybox.HasProperty("_MainTex") && skybox.mainTexture is Cubemap skyCubemap)
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

        static void EnsureMeshNormals(Mesh mesh)
        {
            if (mesh == null) return;
            int id = mesh.GetInstanceID();
            if (_normalCheckedMeshes.Contains(id)) return;
            _normalCheckedMeshes.Add(id);

            try
            {
                Vector3[] normals = mesh.normals;
                if (normals == null || normals.Length != mesh.vertexCount)
                {
                    mesh.RecalculateNormals();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[WSM3D] Voxel mesh normal check failed: " + ex.Message);
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
            EnsureMeshNormals(mesh);
            if (mesh != null && mesh.name != "WSM3D.SanityTestCube")
                LastActorMeshVertCount = mesh.vertexCount;
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

        readonly struct ActorSpriteCardMeshKey : IEquatable<ActorSpriteCardMeshKey>
        {
            public readonly int TextureId;
            public readonly int X;
            public readonly int Y;
            public readonly int Width;
            public readonly int Height;
            public readonly int ScaleKey;

            public ActorSpriteCardMeshKey(Texture2D tex, Sprite sprite, float scaleMultiplier)
            {
                TextureId = tex != null ? tex.GetInstanceID() : 0;
                Rect r = sprite != null ? sprite.rect : default;
                X = Mathf.RoundToInt(r.x);
                Y = Mathf.RoundToInt(r.y);
                Width = Mathf.Max(1, Mathf.RoundToInt(r.width));
                Height = Mathf.Max(1, Mathf.RoundToInt(r.height));
                ScaleKey = Mathf.Max(1, Mathf.RoundToInt(scaleMultiplier * 10000f));
            }

            public bool Equals(ActorSpriteCardMeshKey other)
            {
                return TextureId == other.TextureId &&
                       X == other.X &&
                       Y == other.Y &&
                       Width == other.Width &&
                       Height == other.Height &&
                       ScaleKey == other.ScaleKey;
            }

            public override bool Equals(object? obj) => obj is ActorSpriteCardMeshKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + TextureId;
                    hash = hash * 31 + X;
                    hash = hash * 31 + Y;
                    hash = hash * 31 + Width;
                    hash = hash * 31 + Height;
                    hash = hash * 31 + ScaleKey;
                    return hash;
                }
            }
        }

        readonly struct ActorSpriteCardBatchKey : IEquatable<ActorSpriteCardBatchKey>
        {
            public readonly Mesh Mesh;
            public readonly Material Material;

            public ActorSpriteCardBatchKey(Mesh mesh, Material material)
            {
                Mesh = mesh;
                Material = material;
            }

            public bool Equals(ActorSpriteCardBatchKey other)
            {
                return Mesh == other.Mesh && Material == other.Material;
            }

            public override bool Equals(object? obj) => obj is ActorSpriteCardBatchKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + (Mesh != null ? Mesh.GetInstanceID() : 0);
                    hash = hash * 31 + (Material != null ? Material.GetInstanceID() : 0);
                    return hash;
                }
            }
        }

        static Mesh? GetActorSpriteCardMesh(Sprite? sprite, float scaleMultiplier)
        {
            if (sprite == null || sprite.texture == null)
            {
                return null;
            }

            ActorSpriteCardMeshKey key = new(sprite.texture, sprite, scaleMultiplier);
            if (_actorSpriteCardMeshes.TryGetValue(key, out Mesh cachedMesh))
            {
                return cachedMesh;
            }

            Rect rect = sprite.rect;
            // Size the card in WORLD units like the vanilla sprite render: pixels / pixelsPerUnit.
            // Raw rect.width(px) * scaleMultiplier produced ~45-tile-wide giants (px not divided by PPU).
            float ppu = Mathf.Max(1f, sprite.pixelsPerUnit);
            float scale = scaleMultiplier;
            float width = (rect.width / ppu) * scale;
            float height = (rect.height / ppu) * scale;
            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;

            Texture2D tex = sprite.texture;
            float texWidth = Mathf.Max(1f, tex.width);
            float texHeight = Mathf.Max(1f, tex.height);
            Vector2 uvMin = new Vector2(rect.x / texWidth, rect.y / texHeight);
            Vector2 uvMax = new Vector2((rect.x + rect.width) / texWidth, (rect.y + rect.height) / texHeight);

            Mesh mesh = new Mesh
            {
                name = $"WSM3D.ActorCard_{tex.name}_{rect.x}_{rect.y}_{rect.width}_{rect.height}"
            };
            mesh.vertices = new[]
            {
                new Vector3(-halfWidth, -halfHeight, 0f),
                new Vector3(halfWidth, -halfHeight, 0f),
                new Vector3(-halfWidth, halfHeight, 0f),
                new Vector3(halfWidth, halfHeight, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(uvMin.x, uvMin.y),
                new Vector2(uvMax.x, uvMin.y),
                new Vector2(uvMin.x, uvMax.y),
                new Vector2(uvMax.x, uvMax.y),
            };
            mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
            mesh.normals = new[]
            {
                Vector3.forward,
                Vector3.forward,
                Vector3.forward,
                Vector3.forward
            };
            mesh.RecalculateBounds();

            _actorSpriteCardMeshes[key] = mesh;
            return mesh;
        }

        static Material? GetActorSpriteCardMaterial(Texture2D? texture)
        {
            if (texture == null)
            {
                return null;
            }

            if (_actorSpriteCardMaterials.TryGetValue(texture, out Material existing))
            {
                return existing;
            }

            Shader? shader = Core.Sphere.ResolveShader("");
            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader)
            {
                name = $"WSM3D.ActorSpriteCard.{texture.name}",
                enableInstancing = true
            };
            material.EnableKeyword("INSTANCING_ON");
            material.SetTexture(_actorSpriteCardMainTexId, texture);
            material.SetColor(_actorSpriteCardColorId, Color.white);
            material.SetColor(_actorSpriteCardBaseColorId, Color.white);
            _actorSpriteCardMaterials[texture] = material;
            return material;
        }

        static bool TryQueueActorSpriteCardRender(Material? material, Mesh mesh, Matrix4x4 trs)
        {
            if (material == null || mesh == null || mesh.vertexCount == 0)
            {
                return false;
            }

            EnsureMeshNormals(mesh);

            ActorSpriteCardBatchKey key = new(mesh, material);
            lock (_actorSpriteCardBatchLock)
            {
                if (!_actorSpriteCardBatches.TryGetValue(key, out var list))
                {
                    list = new List<Matrix4x4>(16);
                    _actorSpriteCardBatches[key] = list;
                }

                list.Add(trs);
                string meshName = mesh != null ? mesh.name : "<null-mesh>";
                string materialName = material != null ? material.name : "<null-material>";
                // (CARD-QUEUE diag removed — fired per-actor-per-frame = 522K Debug.Log calls,
                //  flooded the WorldBox console overlay and tanked FPS. Chain confirmed working.)
            }
            return true;
        }

        static Quaternion GetActorCardRotation(Vector3 position)
        {
            Camera? cam = CameraManager.MainCamera;
            Vector3 toCamera = cam != null
                ? (cam.transform.position - position)
                : Vector3.back;
            if (toCamera.sqrMagnitude < 0.0001f)
            {
                toCamera = Vector3.back;
            }

            // Slightly tilt toward the camera for the intended "card" look.
            Quaternion facing = Quaternion.LookRotation(toCamera, Vector3.up);
            return facing * Quaternion.Euler(-12f, 0f, 0f);
        }

        internal static void FlushQueuedActorSpriteCards()
        {
            // (CARD-FLUSH-ENTER diag removed — per-frame log spam.)

            List<KeyValuePair<ActorSpriteCardBatchKey, List<Matrix4x4>>> batches;
            lock (_actorSpriteCardBatchLock)
            {
                // Draw the FRONT buffer (populated at emit-end). Never cleared here — redraws
                // every frame so actors persist across the intermittent-emit gap (no flicker).
                ActorFrontCount = _actorSpriteCardFront.Count;
                if (_actorSpriteCardFront.Count == 0) return;
                batches = new List<KeyValuePair<ActorSpriteCardBatchKey, List<Matrix4x4>>>(_actorSpriteCardFront);
            }

            int flushedBatches = 0;
            int totalMatrices = 0;
            string diagMaterialName = "<none>";
            string diagMeshName = "<none>";
            int diagMeshVerts = 0;

            foreach (KeyValuePair<ActorSpriteCardBatchKey, List<Matrix4x4>> pair in batches)
            {
                Mesh mesh = pair.Key.Mesh;
                Material material = pair.Key.Material;
                if (mesh == null || material == null)
                {
                    pair.Value.Clear();
                    continue;
                }

                List<Matrix4x4> matrices = pair.Value;
                int total = matrices.Count;
                int start = 0;
                while (start < total)
                {
                    int count = Mathf.Min(MaxSpriteCardInstancedBatch, total - start);
                    matrices.CopyTo(start, _actorSpriteCardMatrices, 0, count);
                    diagMaterialName = material != null ? material.name : "<null>";
                    diagMeshName = mesh != null ? mesh.name : "<null>";
                    diagMeshVerts = mesh != null ? mesh.vertexCount : 0;
                    try
                    {
                        Graphics.DrawMeshInstanced(mesh, 0, material, _actorSpriteCardMatrices, count);
                        MeshInstanceBatcher.FrameDrawCalls++;
                        ActorDrawCallsCumulative++;
                        flushedBatches++;
                        totalMatrices += count;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[WSM3D] Actor sprite-card DrawMeshInstanced failed: {ex.GetType().Name}: {ex.Message}");
                        for (int i = start; i < start + count; i++)
                        {
                            try
                            {
                                Graphics.DrawMesh(mesh, _actorSpriteCardMatrices[i - start], material, 0, null);
                                MeshInstanceBatcher.FrameDrawCalls++;
                                totalMatrices++;
                            }
                            catch (System.Exception fallbackEx)
                            {
                                Debug.LogError($"[WSM3D] Actor sprite-card DrawMesh fallback failed: {fallbackEx.GetType().Name}: {fallbackEx.Message}");
                            }
                        }
                    }

                    // ACTOR-DRAW-DIAG removed — confirmed actors use voxel-organicblob meshVerts=164
                    // (real 3D volume, not the flat card). Per-frame Debug.Log floods the console
                    // overlay; gate any future re-add behind ProfilerDump + a mesh-name-change check.
                    _ = _actorDrawDiagThrottle; _ = diagMaterialName; _ = diagMeshName; _ = diagMeshVerts;
                    start += count;
                }

                // DO NOT clear matrices here — the front buffer is replaced wholesale on the
                // next emit (line ~1184 in ActorVoxelEmit.EmitVoxels). Clearing after every
                // flush means non-emit frames find an empty front buffer and skip the
                // DrawMeshInstanced call, producing intermittent drawCalls=0 in the bridge
                // telemetry. The intent (comment at L770) is "redraws every frame so actors
                // persist across the intermittent-emit gap (no flicker)" — the clear defeats
                // that and was the regression root cause.
            }
        }

        // ---------------------------------------------------------------------
        // Harmony hooks. Registered automatically via Patcher.PatchAll on the
        // existing Core.Patch() pass because [HarmonyPatch] is declared here.

        [Phase(nameof(SavedSettings.VoxelEntities))]
        [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.precalculateRenderDataParallel))]
        public static class ActorVoxelEmit
        {
            static bool _actorLodDiagLogged;
            public static bool EmitVoxelsCalled;
            public static int LastVisibleUnitsCount;
            public static int LastFrustumCullerPassCount;
            public static int LastBatcherSubmitCount;
            static bool _emitDiagLogged;
            static int _emitDiagFrameCounter;
            static bool _emitDiagSawNonZero;
            static bool _billboardDiagLogged;

            public static void ResetDiag()
            {
                _emitDiagLogged = false;
                _emitDiagFrameCounter = 0;
                _emitDiagSawNonZero = false;
                _billboardDiagLogged = false;
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
                // FLICKER FIX (double-buffer): emit fills the BACK buffer (_actorSpriteCardBatches),
                // cleared here at start. At emit END we atomically copy it to the FRONT buffer
                // (_actorSpriteCardFront) which the per-frame flush redraws EVERY frame. Because
                // emit runs intermittently (precalculateRenderDataParallel postfix) but the flush
                // + DrawMeshInstanced run every frame, the front buffer must never go empty between
                // emits — so we only REPLACE front when a fresh emit completes, never clear it mid-gap.
                lock (_actorSpriteCardBatchLock)
                {
                    foreach (var b in _actorSpriteCardBatches.Values) b.Clear();
                }
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
                            Mesh dm = VoxelMeshCache.Get(dSp, ShapeHint.OrganicBlob, true, VoxelEntityType.Actor);
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
                    if (!_billboardDiagLogged)
                    {
                        _billboardDiagLogged = true;
                        int matSkipped = 1;
                        Debug.Log($"[WSM3D][BILLBOARD-DIAG] type=Actor processed={__instance.visible_units.count} skipped={__instance.visible_units.count} reason=(nullMaterial={matSkipped},lodImpostor=0,scaleZero=0) visibleUnitsCount={__instance.visible_units.count} frustumCullerPassCount=0 batcherSubmitCount=0");
                    }
                    RenderErrorRegistry.Record(RenderErrorType.MaterialNull, "ActorManager",
                        "EnsureMaterial() returned no usable voxel material", Vector3.zero);
                    return;
                }
                if (!_matDiagLogged)
                {
                    _matDiagLogged = true;
                    Material? matDiag = GetResolvedMaterial();
                    string matName = matDiag?.name ?? "<null>";
                    string shaderName = matDiag?.shader != null ? matDiag.shader.name : "<null shader>";
                    Debug.Log($"[WSM3D][MAT-DIAG] material={matName} shader={shaderName}");
                }

                var rd = __instance.render_data;
                var arr = __instance.visible_units.array;
                int n = __instance.visible_units.count;
                LastVisibleUnitsCount = n;
                LastFrustumCullerPassCount = 0;
                LastBatcherSubmitCount = 0;
                // DIAG-SUBMIT path counters — find where the 8 meshOk actors are being dropped
                int dsNullActor = 0, dsPerpSkipped = 0, dsFrustumFail = 0;
                int dsTierCull = 0, dsTierVoxel = 0;
                int dsSkeletalAttempt = 0, dsSkeletalSubmitOk = 0, dsSkeletalSubmitFail = 0;
                int dsCullMeshNull = 0;
                int dsSpriteNull = 0, dsVoxelMeshNull = 0, dsVoxelSubmitAttempt = 0, dsVoxelSubmitOk = 0, dsVoxelSubmitFail = 0;
                int diagActors = 0;
                int diagAlreadySuppressed = 0;
                int diagSubmitAttempt = 0;
                int diagSkipNull = 0;
                int diagHasNormalRenderSetFalse = 0;
                for (int i = 0; i < n; i++)
                {
                    Actor a = arr[i];
                    if (a == null || a.asset == null)
                    {
                        dsNullActor++;
                        continue;
                    }

                    diagActors++;
                    bool suppressAlready = !rd.has_normal_render[i];
                    if (suppressAlready) diagAlreadySuppressed++;
                    rd.has_normal_render[i] = false;
                    if (!rd.has_normal_render[i]) diagHasNormalRenderSetFalse++;

                    // VOXEL-OR-INVISIBLE (user, 2026-05-30): the legacy PerpActors opt-out
                    // used to `continue` here, which LEFT has_normal_render[i] true and so
                    // re-exposed the vanilla 2D billboard for perp-marked assets (palms,
                    // ferns, bats, ground decals). That violated voxel-or-invisible — those
                    // entities reappeared as flat sprites (BUG A). Perp assets now fall
                    // through to the SAME voxel-or-cull path as every other actor: they
                    // either render a real voxel mesh or nothing. We only bump the
                    // diagnostic counter; we do NOT skip the emit.
                    bool isPerp = Constants.PerpActors.ContainsKey(a.asset.id);
                    if (isPerp) dsPerpSkipped++;
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
                    // Use fixed baseline height for LOD — rd.scales[i].y is the tiny vanilla
                    // sprite render scale (~0.05), not the voxel mesh height. Fixed baseline
                    // of 0.5 * VoxelScaleMultiplier * ActorVoxelScaleFactor matches LodSelector default.
                    float actorEntityHeight = 0.5f * Core.savedSettings.VoxelScaleMultiplier * Core.savedSettings.ActorVoxelScaleFactor;
                    WorldSphereMod.LOD.LodTier tier = WorldSphereMod.LOD.LodSelector.Select(cullPos, a.GetHashCode(), actorEntityHeight);
                    if (!_actorLodDiagLogged) {
                        float dist = (cullPos - CameraManager.MainCamera.transform.position).magnitude;
                        Debug.Log($"[WSM3D][ACTOR-LOD-DIAG] entityH={actorEntityHeight:F2} dist={dist:F1} thr={WorldSphereMod.LOD.LodSelector.VoxelThreshold:F3} tier={tier}");
                        _actorLodDiagLogged = true;
                    }
                    // Two-tier ladder: Voxel (near, emit mesh) or Cull (far, draw nothing).
                    if (tier == WorldSphereMod.LOD.LodTier.Cull) dsTierCull++;
                    else dsTierVoxel++;

                    if (Core.savedSettings.SkeletalAnimation && tier != WorldSphereMod.LOD.LodTier.Cull)
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

                    if (tier == WorldSphereMod.LOD.LodTier.Cull)
                    {
                        // FAR TIER = CULL, NOT BILLBOARD. The user explicitly prefers seeing
                        // NOTHING over a flat fallback tier. Keep the LOD distance logic (so near
                        // objects still voxelize) but the far tier draws nothing. Sprite is
                        // already suppressed above, so the object is invisible at distance.
                        rd.scales[i] = Vector3.zero;
                        dsTierCull++;
                        dsCullMeshNull++;
                        continue;
                    }

                    float actorScale = Core.savedSettings.VoxelScaleMultiplier * Core.savedSettings.ActorVoxelScaleFactor;
                    // REAL 3D VOLUME (not flat card): route through the OrganicBlob voxelizer that
                    // extrudes the sprite into an actual rounded 3D volume (per-pixel, kMinDepthScale
                    // 0.60 = real depth). The earlier "sprite-card" flat 4-vert quad (GetActorSpriteCardMesh)
                    // made every entity a same-width slab (trees thin, grass odd) — that was a regression
                    // of this proven path (d186ad45). Mesh is unit-sized; scl applies actorScale below.
                    Mesh m = VoxelMeshCache.Get(sp, ShapeHint.OrganicBlob, true, VoxelEntityType.Actor);
                    // Sprite-card mesh not ready yet (cache miss) is not expected in
                    // current implementation, but handle defensively as a skipped submit.
                    if (m == null || m.vertexCount == 0)
                    {
                        dsVoxelMeshNull++;
                        diagSkipNull++;
                        Vector3 errPos = rd.positions[i];
                        if (errPos.z < Constants.ZDisplacement * 0.5f) errPos = errPos.To3DTileHeight(false);
                        RecordActorError(RenderErrorType.VoxelNotReady,
                            a, sp != null ? "sprite-card mesh null/empty (cache build pending) sprite=" + sp.name : "sprite-card mesh null/empty", errPos);
                        continue;
                    }
                    dsVoxelSubmitAttempt++;
                    diagSubmitAttempt++;

                    Vector3 pos = rd.positions[i];
                    Vector3 posBeforeLift = pos;
                    if (pos.z < Constants.ZDisplacement * 0.5f)
                    {
                        pos = pos.To3DTileHeight(false);
                    }
                    LogActorSubmitDiagnostic("voxel", ref _actorVoxelDiagnosticLogged, a, sp, posBeforeLift, pos, rd.colors[i]);
                    SanityTestCube.CaptureFirstActorPos(pos);
                    Vector3 rot = rd.rotations[i];
                    // Real 3D volume: upright (Y-up), no camera-facing billboard. Uniform actorScale.
                    Quaternion faceRot = Quaternion.identity;
                    Vector3 scl = new Vector3(actorScale, actorScale, actorScale);
                    if (rd.flip_x_states[i]) scl.x = -scl.x;
                    float halfHeight = m.bounds.size.y * 0.5f * actorScale;
                    pos.y += halfHeight;
                    LogFirstActorPos(posBeforeLift, pos, scl);
                    Matrix4x4 trs = Matrix4x4.TRS(pos, faceRot, scl);
                    RecordActorVoxelTrs(trs);
                    if (Submit(m, trs, rd.colors[i]))
                    {
                        LastBatcherSubmitCount++;
                        ActorDrawCallsCumulative++;
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
                if (!_billboardDiagLogged)
                {
                    _billboardDiagLogged = true;
                    int skipped = dsNullActor + dsPerpSkipped + dsFrustumFail + dsSpriteNull + dsVoxelMeshNull + dsVoxelSubmitFail + dsSkeletalSubmitFail + dsTierImpostor;
                    Debug.Log($"[WSM3D][BILLBOARD-DIAG] type=Actor processed={n} skipped={skipped} reason=(nullMaterial=0,lodImpostor={dsTierImpostor},scaleZero=0,nullActor={dsNullActor},perp={dsPerpSkipped},frustumFail={dsFrustumFail},spriteNull={dsSpriteNull},meshNull={dsVoxelMeshNull},submitFail={dsVoxelSubmitFail + dsSkeletalSubmitFail}) visibleUnitsCount={LastVisibleUnitsCount} frustumCullerPassCount={LastFrustumCullerPassCount} batcherSubmitCount={LastBatcherSubmitCount}");
                }
                // DIAG-SUBMIT one-shot path report — answers "where did the meshOk actors go?"
                if (Core.savedSettings.ProfilerDump && (!_emitDiagSawNonZero || _emitDiagFrameCounter < 3))
                {
                    _emitDiagFrameCounter++;
                        Debug.Log($"[WSM3D][DIAG-SUBMIT] EmitVoxels paths n={n} nullActor={dsNullActor} perpSkip={dsPerpSkipped} frustumFail={dsFrustumFail} frustumPass={LastFrustumCullerPassCount} | tier(Cull={dsTierCull} Voxel={dsTierVoxel}) | skel(attempt={dsSkeletalAttempt} ok={dsSkeletalSubmitOk} fail={dsSkeletalSubmitFail}) | spriteNull={dsSpriteNull} | cull(meshNull={dsCullMeshNull}) | voxel(meshNull={dsVoxelMeshNull} attempt={dsVoxelSubmitAttempt} ok={dsVoxelSubmitOk} fail={dsVoxelSubmitFail}) | LastBatcherSubmitCount={LastBatcherSubmitCount} SkeletalAnimation={Core.savedSettings.SkeletalAnimation}");
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
            static bool _buildingBillboardDiagLogged;
            // Per-frame budget cycling: tracks where we left off in the visible
            // buildings array so we process the next slice each frame.
            static int _budgetOffset;
            // IDLE FAST-PATH (#208 fps): skip the per-frame emit when the visible-buildings
            // identity hash is unchanged AND no building moved across the last two frames.
            // Most idle frames have a static camera + no-spawn world; previous behavior ran
            // the full budget cycle every frame (e.g. 200/200 of 1891 visible buildings) which
            // is the dominant frame cost on wip/208-height-fix when the camera is still.
            // The hash is a cheap 64-bit rolling xor of the first/middle/last building IDs +
            // the visible-count — robust to budget cycling (only flips when a building is
            // added/removed/visibly moved) and never false-skips on a moving camera (camera
            // doesn't affect visible_buildings_count or the array contents, only positions).
            static ulong _lastVisibleBuildingsHash;
            static int _lastVisibleBuildingsCount;
            static int _skippedStaticFrames;
            public static int SkippedStaticFrames => _skippedStaticFrames;

            static ulong ComputeVisibleBuildingsHash(Building[] arr, int n)
            {
                if (arr == null || n <= 0) return 0;
                // FNV-1a-ish 64-bit: cheap, robust to permutation, and only walks 3 sample
                // positions + the count. Sampling the head/middle/tail means a new building
                // appearing anywhere in the array still changes the hash next frame.
                ulong h = 1469598103934665603UL;
                ulong id0 = GetBuildingAssetHash(arr[0]);
                h = (h ^ id0) * 1099511628211UL;
                if (n > 1)
                {
                    int mid = n >> 1;
                    Building bMid = arr[mid];
                    Building bLast = arr[n - 1];
                    ulong idMid = GetBuildingAssetHash(bMid);
                    ulong idLast = GetBuildingAssetHash(bLast);
                    h = (h ^ idMid) * 1099511628211UL;
                    h = (h ^ idLast) * 1099511628211UL;
                }
                h = (h ^ (ulong)n) * 1099511628211UL;
                return h;
            }

            static ulong GetBuildingAssetHash(Building building)
            {
                if (building == null || building.asset == null || string.IsNullOrEmpty(building.asset.id))
                {
                    return 0UL;
                }
                return (ulong)building.asset.id.GetHashCode();
            }

            public static void ResetDiag()
            {
                _buildingVoxelEmitSubmitLogged = false;
                _buildingEmitDiagLogged = false;
                _buildingBillboardDiagLogged = false;
                _budgetOffset = 0;
                _lastVisibleBuildingsHash = 0;
                _lastVisibleBuildingsCount = -1;
                _skippedStaticFrames = 0;
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
                    if (!_buildingBillboardDiagLogged)
                    {
                        _buildingBillboardDiagLogged = true;
                        int visible = __instance._visible_buildings_count;
                        Debug.Log($"[WSM3D][BILLBOARD-DIAG] type=Building processed={visible} skipped={visible} reason=(materialNull=1,lodImpostor=0,scaleZero=0) visibleUnitsCount={visible} frustumCullerPassCount=0 batcherSubmitCount=0");
                    }
                    RenderErrorRegistry.Record(RenderErrorType.MaterialNull, "BuildingManager",
                        "EnsureMaterial() returned no usable voxel material", Vector3.zero);
                    return;
                }

                var rd = __instance.render_data;
                var arr = __instance._array_visible_buildings;
                int n = __instance._visible_buildings_count;
                int processed = 0;
                int skippedNull = 0;
                int skippedPerp = 0;
                int skippedFrustum = 0;
                int skippedLod = 0;
                int skippedSpriteNull = 0;
                int skippedScaleZero = 0;
                int skippedMeshNull = 0;
                int submitCount = 0;
                int frustumPass = 0;

                // IDLE FAST-PATH (#208 fps): when the visible-building set is unchanged
                // (no spawn / despawn / building moved into frustum or out), skip the
                // budget-cycled re-emit entirely. The bucket fleet rendered last frame is
                // still alive in the batcher and re-drawn by VoxelRender.Flush each frame
                // via the same instance matrices — no visual loss, big CPU save in idle.
                // Re-emit fires when the count differs OR the sampled hash changes, so
                // moving buildings or new spawns still re-emit on the next frame.
                if (arr != null && n > 0 && n == _lastVisibleBuildingsCount)
                {
                    ulong h = ComputeVisibleBuildingsHash(arr, n);
                    if (h == _lastVisibleBuildingsHash)
                    {
                        _skippedStaticFrames++;
                        return;
                    }
                }

                // Compute the new hash unconditionally so the next frame's check is correct
                // (covers the first frame and any frame where count or hash changed).
                if (arr != null && n > 0)
                {
                    _lastVisibleBuildingsHash = ComputeVisibleBuildingsHash(arr, n);
                }
                _lastVisibleBuildingsCount = n;
                int processed = 0;
                int skippedNull = 0;
                int skippedPerp = 0;
                int skippedFrustum = 0;
                int skippedLod = 0;
                int skippedSpriteNull = 0;
                int skippedScaleZero = 0;
                int skippedMeshNull = 0;
                int submitCount = 0;
                int frustumPass = 0;

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
                    if (b == null || b.asset == null)
                    {
                        skippedNull++;
                        continue;
                    }

                    processed++;
                    if (Constants.PerpBuildings.ContainsKey(b.asset.id))
                    {
                        skippedPerp++;
                        continue;
                    }

                    Vector3 inScale = rd.scales[i];
                    if (inScale.sqrMagnitude <= 0.000001f)
                    {
                        skippedScaleZero++;
                    }

                    Vector3 cullPos = rd.positions[i];
                    if (cullPos.z < Constants.ZDisplacement * 0.5f)
                    {
                        skippedNull++;
                        continue;
                    }

                    processed++;
                    if (Constants.PerpBuildings.ContainsKey(b.asset.id))
                    {
                        skippedPerp++;
                        continue;
                    }

                    Vector3 inScale = rd.scales[i];
                    if (inScale.sqrMagnitude <= 0.000001f)
                    {
                        skippedScaleZero++;
                    }

                    // Derive the 3D cull position from the building's tile coords rather
                    // than rd.positions[i] — the parallel calculatebuildindata3D pass may
                    // not have completed when this Postfix runs (Parallel.For race), leaving
                    // rd.positions stale from the prior frame. Tile coords are always valid.
                    // (#208 frustumFail=200 root cause: stale/wrong positions in rd.positions)
                    Vector3 cullPos;
                    if (b.current_position != null)
                    {
                        Vector2 tp = b.current_position;
                        cullPos = Tools.To3D(new Vector3(tp.x, tp.y, 0), Tools.GetTileHeightSmooth(new Vector3(tp.x, tp.y, 0)));
                    }
                    else
                    {
                        cullPos = rd.positions[i];
                        if (cullPos.z < Constants.ZDisplacement * 0.5f)
                            cullPos = cullPos.To3DTileHeight(false);
                    }
                    // One-shot DIAG: log the actual cullPos for the first building so the
                    // lifted coordinate is visible in Player.log. (#208)
                    if (!_buildingBillboardDiagLogged && i == start)
                        Debug.Log($"[WSM3D][FRUSTUM-DIAG] building[0] tile={b.current_position} cullPos={cullPos} rdPos={rd.positions[i]}");
                    float radius = 3f * Mathf.Max(1f, Core.savedSettings.VoxelScaleMultiplier * 0.5f);
                    if (!WorldSphereMod.LOD.FrustumCuller.IsVisible(cullPos, radius))
                    {
                        skippedFrustum++;
                        continue;
                    }
                    frustumPass++;
                    WorldSphereMod.LOD.LodTier tier = WorldSphereMod.LOD.LodSelector.Select(cullPos, b.GetHashCode());

                    Sprite sp = rd.main_sprites[i];
                    if (sp == null)
                    {
                        RenderErrorRegistry.Record(RenderErrorType.SpriteNull,
                            b.asset != null ? b.asset.id : "<building>", "main_sprites[i] is null", cullPos);
                        skippedSpriteNull++;
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

                    if (tier == WorldSphereMod.LOD.LodTier.Cull)
                    {
                        // FAR TIER = CULL. Sprite already suppressed → building invisible at
                        // distance rather than a flat impostor billboard.
                        skippedLod++;
                        continue;
                    }

                    // Near tier: emit the full voxel mesh via the shared VoxelMeshCache.
                    Mesh m = VoxelMeshCache.Get(sp);
                    // Not ready / empty → invisible until built. Sprite already suppressed.
                    if (m == null || m.vertexCount == 0)
                    {
                        RenderErrorRegistry.Record(RenderErrorType.VoxelNotReady,
                            b.asset != null ? b.asset.id : "<building>",
                            "voxel mesh null/empty (async build pending) sprite=" + sp.name, cullPos);
                        skippedMeshNull++;
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
                    scl *= Core.savedSettings.VoxelScaleMultiplier * Core.savedSettings.BuildingVoxelScaleFactor;
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
                        submitCount++;
                    }
                }
                if (!_buildingBillboardDiagLogged)
                {
                    _buildingBillboardDiagLogged = true;
                    int skipped = skippedNull + skippedPerp + skippedFrustum + skippedLod + skippedSpriteNull + skippedScaleZero + skippedMeshNull;
                    Debug.Log($"[WSM3D][BILLBOARD-DIAG] type=Building processed={processed} skipped={skipped} reason=(materialNull=0,lodImpostor={skippedLod},scaleZero={skippedScaleZero},spriteNull={skippedSpriteNull},meshNull={skippedMeshNull},frustumFail={skippedFrustum},null={skippedNull},perp={skippedPerp}) visibleUnitsCount={n} frustumCullerPassCount={frustumPass} batcherSubmitCount={submitCount}");
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

                if (tier == WorldSphereMod.LOD.LodTier.Cull)
                {
                    // FAR TIER = CULL. Sprite suppressed → drop invisible at distance, never
                    // a flat fallback tier.
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

                    if (tier == WorldSphereMod.LOD.LodTier.Cull)
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
        static bool _ensurePatchesDone;   // guards the one-shot EnsurePhasePatches call (#208)

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
                double tFrustumUpdate = 0.0;
                double tRigTick = 0.0;
                double tRigDrain = 0.0;
                double tRigUpdate = 0.0;
                double tRigClear = 0.0;
                double tPumpQueuedBuilds = 0.0;
                double tDrainCompletedBuilds = 0.0;
                double tSanityDraw = 0.0;
                double tProcGenDrain = 0.0;
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
                        if (Core.savedSettings.ProfilerDump)
                        {
                            Debug.Log($"[WSM3D][Perf] frameDeltaMs={deltaTimePerf * 1000f:F2} avg60FrameDeltaMs={avgFrameTime * 1000f:F2} avg60Fps={avgFps:F1}");
                        }
                        _perfFrameCounter = 0;
                        _perfDeltaTimeSum = 0f;
                    }
                });

                _instancingTelemetryFrame++;
                if (_instancingTelemetryFrame >= 60)
                {
                    _instancingTelemetryFrame = 0;
                    if (Core.savedSettings.ProfilerDump)
                    {
                        Debug.Log($"[WSM3D][Telemetry] InstancingEfficiency={MeshInstanceBatcher.InstancingEfficiency:F4} FrameBucketCount={MeshInstanceBatcher.FrameBucketCount} FrameInstances={MeshInstanceBatcher.FrameInstances}");
                    }
                }

                float nowPerf = Time.realtimeSinceStartup;
                if (nowPerf - _telemetryLastTime > 10f)
                {
                    _telemetryLastTime = nowPerf;
                    if (Core.savedSettings.ProfilerDump)
                    {
                        Debug.Log($"[WSM3D][Telemetry] frameMs={Time.unscaledDeltaTime * 1000:F2} drawCalls={MeshInstanceBatcher.FrameDrawCalls} instances={MeshInstanceBatcher.FrameInstances} cacheSize={VoxelMeshCache.Count} cacheHits={VoxelMeshCache.HitCount} cacheMisses={VoxelMeshCache.MissCount} submits={VoxelRender._submitDiagCount} gcMB={(System.GC.GetTotalMemory(false) / 1048576f):F1}");
                    }
                    VoxelRender._submitDiagCount = 0;
                }

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
                    $"FrustumUpdate={tFrustumUpdate:F2}ms " +
                    $"RigTick={tRigTick:F2}ms " +
                    $"RigDrain={tRigDrain:F2}ms " +
                    $"RigUpdate={tRigUpdate:F2}ms " +
                    $"RigClear={tRigClear:F2}ms " +
                    $"PumpQueuedBuilds={tPumpQueuedBuilds:F2}ms " +
                    $"DrainCompletedBuilds={tDrainCompletedBuilds:F2}ms " +
                    $"SanityDraw={tSanityDraw:F2}ms " +
                    $"ProcGenDrain={tProcGenDrain:F2}ms " +
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
            // Belt-and-suspenders: re-apply phase patches on the FIRST tick only, in case
            // NML skipped PostInit on a save-load path. _ensurePatchesDone is a dedicated
            // one-shot flag so this never fires on subsequent frames — EnsurePhasePatches
            // calls ApplyPhaseToggle which calls VoxelMeshCache.Clear() as a side-effect,
            // and running that every frame was the cause of voxelCacheSize=0 + frameMs=400.
            // (#208 voxel-cache-clear-every-frame root cause)
            if (!_ensurePatchesDone)
            {
                _ensurePatchesDone = true;
                try { Core.EnsurePhasePatches(); }
                catch (System.Exception ex) { Debug.LogWarning("[WSM3D] TickPerFrame EnsurePhasePatches failed: " + ex.Message); }
            }

            float deltaTime = Time.deltaTime;
            _perfFrameCounter++;
            _perfDeltaTimeSum += deltaTime;
            if (_perfFrameCounter >= kPerfSampleWindowFrames)
            {
                float avgFrameTime = _perfDeltaTimeSum / kPerfSampleWindowFrames;
                float avgFps = avgFrameTime > 0f ? 1f / avgFrameTime : 0f;
                if (Core.savedSettings.ProfilerDump)
                {
                    Debug.Log($"[WSM3D][Perf] frameDeltaMs={deltaTime * 1000f:F2} avg60FrameDeltaMs={avgFrameTime * 1000f:F2} avg60Fps={avgFps:F1}");
                }
                _perfFrameCounter = 0;
                _perfDeltaTimeSum = 0f;
            }

            _instancingTelemetryFrame++;
            if (_instancingTelemetryFrame >= 60)
            {
                _instancingTelemetryFrame = 0;
                if (Core.savedSettings.ProfilerDump)
                {
                    Debug.Log($"[WSM3D][Telemetry] InstancingEfficiency={MeshInstanceBatcher.InstancingEfficiency:F4} FrameBucketCount={MeshInstanceBatcher.FrameBucketCount} FrameInstances={MeshInstanceBatcher.FrameInstances}");
                }
            }

            // Log-based telemetry every 10s — bypasses bridge for steady-state observability
            // even when bridge is in scene-transition known-issue state.
            float now = Time.realtimeSinceStartup;
            if (now - _telemetryLastTime > 10f)
            {
                _telemetryLastTime = now;
                if (Core.savedSettings.ProfilerDump)
                {
                    Debug.Log($"[WSM3D][Telemetry] frameMs={Time.unscaledDeltaTime * 1000:F2} drawCalls={MeshInstanceBatcher.FrameDrawCalls} instances={MeshInstanceBatcher.FrameInstances} cacheSize={VoxelMeshCache.Count} cacheHits={VoxelMeshCache.HitCount} cacheMisses={VoxelMeshCache.MissCount} submits={VoxelRender._submitDiagCount} gcMB={(System.GC.GetTotalMemory(false) / 1048576f):F1}");
                }
                VoxelRender._submitDiagCount = 0;
            }

            WorldSphereMod.Voxel.VoxelMeshCache.BeginFrame();

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
            VoxelRender.FlushQueuedActorSpriteCards();
            if (Core.savedSettings?.ProceduralBuildings == true)
            {
                Material? procBuildingMaterial = VoxelRender.GetResolvedMaterial();
                WorldSphereMod.ProcGen.BuildingProcRender.ProcMeshEmit.FlushQueuedBuildingDraws(
                    procBuildingMaterial,
                    out _,
                    out _);
            }
            VoxelMeshCache.DrainPendingDestroy();

            // Per-frame Submit/Flush diagnostic. Snapshot live counters and reset.
            int submitCount = Interlocked.Exchange(ref MeshInstanceBatcher._submitCountThisFrame, 0);
            int flushCount = Interlocked.Exchange(ref MeshInstanceBatcher._flushCountThisFrame, 0);
            MeshInstanceBatcher.LastFrameSubmitCount = submitCount;
            MeshInstanceBatcher.LastFrameFlushCount = flushCount;
            _submitFlushDiagFrame++;
            if (_submitFlushDiagFrame % 60 == 0 && Core.savedSettings.ProfilerDump)
            {
                if (Core.savedSettings.ProfilerDump)
                {
                    Debug.Log($"[WSM3D][SubmitFlushDiag] frame={_submitFlushDiagFrame} submits={submitCount} flushes={flushCount} submitsBeforeFlush={submitsBeforeFlush} hadPending={hadPending} drawCalls={MeshInstanceBatcher.FrameDrawCalls} instances={MeshInstanceBatcher.FrameInstances} buckets={MeshInstanceBatcher.FrameBucketCount}");
                }
            }

            Bridge.BridgeServer.RefreshTelemetryCache();
        }

        static bool _lastAppliedPostFX = false;
        static bool _lastAppliedSSAOEnabled = false;
        static bool _lastAppliedSSGIEnabled = false;
    }
}
