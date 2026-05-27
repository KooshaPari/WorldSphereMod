using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using WorldSphereMod.NewCamera;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// GPU renderer for voxel/procgen meshes. Buckets submissions by
    /// (mesh, material) and flushes them via <see cref="Graphics.DrawMeshInstanced"/> by default,
    /// with automatic fallback to per-instance <see cref="Graphics.DrawMesh"/> if
    /// the build does not expose instancing shader variants.
    /// </summary>
    public static class MeshInstanceBatcher
    {
        struct Key
        {
            public Mesh Mesh;
            public Material Material;
            public override int GetHashCode()
            {
                int m = Mesh != null ? Mesh.GetInstanceID() : 0;
                int x = Material != null ? Material.GetInstanceID() : 0;
                return m * 397 ^ x;
            }
            public override bool Equals(object obj)
            {
                if (obj is Key k) return k.Mesh == Mesh && k.Material == Material;
                return false;
            }
        }

        class Bucket
        {
            public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>(1024);
            public readonly List<Vector4>   Colors = new List<Vector4>(1024);
            public MaterialPropertyBlock Block = new MaterialPropertyBlock();
            // Scratch buffers reused across frames; grown (never shrunk) to current batch
            // size for DrawMeshInstanced fast-path arrays.
            public Matrix4x4[] MatScratch = new Matrix4x4[kBatch];
            public Vector4[] ColScratch = new Vector4[kBatch];
        }

        readonly struct SubmitRecord
        {
            public readonly Mesh Mesh;
            public readonly Material Material;
            public readonly Matrix4x4 Matrix;
            public readonly Vector4 Tint;

            public SubmitRecord(Mesh mesh, Material material, Matrix4x4 matrix, Color tint)
            {
                Mesh = mesh;
                Material = material;
                Matrix = matrix;
                Tint = tint;
            }
        }

        static readonly ConcurrentQueue<SubmitRecord> _pendingSubmissions = new ConcurrentQueue<SubmitRecord>();
        static readonly Dictionary<Key, Bucket> _buckets = new Dictionary<Key, Bucket>(128);
        static readonly int _colorProp = Shader.PropertyToID("_InstanceColor");
        static readonly int _baseColorProp = Shader.PropertyToID("_BaseColor");
        static readonly int _colorPropUnlit = Shader.PropertyToID("_Color");
        static readonly int _emissionProp = Shader.PropertyToID("_EmissionColor");
        static readonly UnityEngine.Color _bakeEmission = new UnityEngine.Color(0.15f, 0.15f, 0.15f, 1f);
    // Scratch array for per-instance _EmissionColor so UNITY_ACCESS_INSTANCED_PROP
    // reads the value correctly (SetColor alone writes a shared value that the
    // instanced cbuffer ignores — falling back to the material default (0,0,0)).
    static Vector4[] _emissionScratch = new Vector4[kBatch];
        const int kBatch = 1023;
        const float kDebugCubeSize = 0.5f;
        static bool UseBrg => Core.savedSettings != null && Core.savedSettings.UseBRG;

        public static long FrameDrawCalls;
        public static long FrameInstances;
        public static long FrameBucketCount;
        public static float InstancingEfficiency => FrameInstances > 0 ? (float)FrameBucketCount / FrameInstances : 0f;
        public static bool UseFallbackPath => _useFallbackPath;

        /// <summary>Force the per-instance Graphics.DrawMesh fallback path on. Use to
        /// diagnose 'instancing reported OK but actors invisible' — bypasses
        /// DrawMeshInstanced entirely so the same draw goes through the
        /// known-good per-instance path. Set once at startup or after Reset.</summary>
        public static void ForceFallbackPath() { _useFallbackPath = true; }
        public static void SetFallbackPath(bool useFallback) => _useFallbackPath = useFallback;
        public static void ArmFallbackDiagOnce() { _fallbackDrawDiagFrames = 4; }
        public static bool InstancingBroken => _instancingErrorLogged;

        public static bool HasPendingSubmissions
        {
            get
            {
                if (UseBrg && MeshInstanceBatcherBRG.IsReady) return MeshInstanceBatcherBRG.HasPendingSubmissions;
                return Volatile.Read(ref _pendingSubmissionCount) > 0 || _buckets.Count > 0;
            }
        }

        static int _pendingSubmissionCount;
        static bool _instancingErrorLogged;
        // Default to instanced rendering; material checks below guard unsupported
        // shader paths before issuing DrawMeshInstanced.
        static bool _useFallbackPath = false;
        static bool _verboseDrawLoggingArmed;
        static bool _verboseDrawLoggingConsumed;
        static bool _renderTargetLogged;
        static bool _flushBucketDiagLogged;
        static bool _allCamerasLogged;
        static int _mainThreadId;
        static Mesh? _debugCubeMesh;

        public static void SetMainThread()
        {
            if (_mainThreadId == 0)
                _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static void Submit(Mesh mesh, Material mat, Matrix4x4 matrix, Color tint)
        {
            // Black-actor regression guard: WorldBox render_data.colors can be
            // near-zero for shadowed/night actors. Per-instance _Color multiplies
            // the voxel mesh's baked vertex colors -- a black tint forces every
            // pixel to (0,0,0) regardless of sprite content. Clamp to a minimum
            // brightness so the baked colors always come through.
            // Reverted callsite-by-callsite in waves 27 and 29; this guard is
            // here at the chokepoint so future parallel-session edits cannot
            // re-introduce the regression.
            float brightness = tint.r + tint.g + tint.b;
            if (brightness < 0.6f)
            {
                tint = Color.white;
            }
            if (UseBrg && MeshInstanceBatcherBRG.TrySubmit(mesh, mat, matrix, tint))
            {
                return;
            }

            // Unity overloads == to return true for destroyed objects, but C#
            // null checks (and ?.) use ReferenceEquals which misses destroyed
            // UnityEngine.Objects. Always use the == operator here.
            if (mesh == null || mat == null) return;

            // Empty-mesh guard ONLY: drop meshes with zero vertices (e.g. cache
            // miss returning placeholder before voxelization completes). The
            // earlier stricter triangle-count guard (commit 1ac068d) was too
            // aggressive — it dropped legitimate meshes too, causing fallback
            // to 2D sprite billboards. Trust upstream mesh validity beyond
            // the vertexCount check.
            if (mesh.vertexCount <= 0) return;

            if (Core.savedSettings.ProfilerDump && !_verboseDrawLoggingArmed && !_verboseDrawLoggingConsumed)
            {
                _verboseDrawLoggingArmed = true;
            }

            if (_mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId)
            {
                AddToBucket(mesh, mat, matrix, tint);
                return;
            }

            _pendingSubmissions.Enqueue(new SubmitRecord(mesh, mat, matrix, tint));
            Interlocked.Increment(ref _pendingSubmissionCount);
        }

        static void DrainPendingSubmissions()
        {
            int drained = 0;
            while (_pendingSubmissions.TryDequeue(out var record))
            {
                AddToBucket(record.Mesh, record.Material, record.Matrix, record.Tint);
                drained++;
            }

            if (drained > 0)
            {
                Interlocked.Add(ref _pendingSubmissionCount, -drained);
            }
        }

        static void AddToBucket(Mesh mesh, Material mat, Matrix4x4 matrix, Vector4 tint)
        {
            var k = new Key { Mesh = mesh, Material = mat };
            if (!_buckets.TryGetValue(k, out var b))
            {
                b = new Bucket();
                _buckets[k] = b;
            }
            b.Matrices.Add(matrix);
            b.Colors.Add(tint);
        }

        public static void Flush(int layer = 0, ShadowCastingMode shadows = ShadowCastingMode.On, bool receive = true)
        {
            if (UseBrg && MeshInstanceBatcherBRG.TryFlush(layer, shadows, receive))
            {
                return;
            }

            DrainPendingSubmissions();

            LogAllCameras();

            Camera renderCamera = ResolveRenderCamera();
            int resolvedLayer = ResolveRenderLayer(layer, renderCamera);
            LogRenderTarget(renderCamera, layer, resolvedLayer, shadows, receive);
            bool verboseDrawLogging = Core.savedSettings.ProfilerDump && _verboseDrawLoggingArmed;

            FrameDrawCalls = 0;
            FrameInstances = 0;
            FrameBucketCount = 0;

            // TEMPORARY DIAGNOSTIC: one-shot bucket inventory at Flush
            if (!_flushBucketDiagLogged && _buckets.Count > 0)
            {
                _flushBucketDiagLogged = true;
                int pendingDrained = Volatile.Read(ref _pendingSubmissionCount);
                Debug.Log($"[WSM3D][DIAG-BATCHER] Flush bucket inventory: buckets={_buckets.Count} pendingSubmissions={pendingDrained} useFallback={_useFallbackPath} instancingBroken={_instancingErrorLogged}");
                foreach (var diagKv in _buckets)
                {
                    string meshName = diagKv.Key.Mesh != null ? diagKv.Key.Mesh.name : "<null>";
                    string matName = diagKv.Key.Material != null ? diagKv.Key.Material.name : "<null>";
                    int count = diagKv.Value.Matrices.Count;
                    Debug.Log($"[WSM3D][DIAG-BATCHER]   bucket mesh={meshName} mat={matName} instances={count}");
                }
            }

            var bucketEnumerator = _buckets.GetEnumerator();
            while (bucketEnumerator.MoveNext())
            {
                KeyValuePair<Key, Bucket> kv = bucketEnumerator.Current;
                var bucket = kv.Value;
                var key = kv.Key;
                Mesh mesh = key.Mesh;
                Material material = key.Material;
                int total = bucket.Matrices.Count;
                FrameInstances += total;

                // Skip buckets whose mesh or material was destroyed by Unity
                // (e.g. VoxelMeshCache.Clear while submissions are in-flight).
                // Unity == null returns true for destroyed objects; C# ?. does not.
                if (mesh == null || material == null)
                {
                    bucket.Matrices.Clear();
                    bucket.Colors.Clear();
                    FrameBucketCount++;
                    continue;
                }

                if (_useFallbackPath)
                {
                    DrawFallbackPath(key, bucket, total, resolvedLayer, renderCamera, shadows, receive);
                    bucket.Matrices.Clear();
                    bucket.Colors.Clear();
                    FrameBucketCount++;
                    continue;
                }

                int offset = 0;
                while (offset < total)
                {
                    int n = Mathf.Min(kBatch, total - offset);
                    if (bucket.MatScratch.Length < n)
                    {
                        bucket.MatScratch = new Matrix4x4[n];
                        bucket.ColScratch = new Vector4[n];
                    }
                    bucket.Matrices.CopyTo(offset, bucket.MatScratch, 0, n);
                    bucket.Colors.CopyTo(offset, bucket.ColScratch, 0, n);

                    if (!CanUseInstancedDraw(material, out string disableReason))
                    {
                        if (!_instancingErrorLogged)
                        {
                            _instancingErrorLogged = true;
                            Debug.LogError(disableReason);
                        }
                        DrawFallbackPath(key, bucket, total, resolvedLayer, renderCamera, shadows, receive, offset);
                        break;
                    }

                    bucket.Block.Clear();
                    bucket.Block.SetVectorArray(_colorProp, bucket.ColScratch);
                    bucket.Block.SetVectorArray(_colorPropUnlit, bucket.ColScratch);
                    // Fill emission scratch array so UNITY_ACCESS_INSTANCED_PROP
                    // reads the correct value. SetColor alone writes a shared
                    // value that the per-instance cbuffer ignores, causing
                    // _EmissionColor to fall back to the material default (0,0,0)
                    // — which makes voxels invisible against dark backgrounds.
                    if (_emissionScratch.Length < n)
                        _emissionScratch = new Vector4[n];
                    Vector4 emV = _bakeEmission;
                    for (int ei = 0; ei < n; ei++)
                        _emissionScratch[ei] = emV;
                    bucket.Block.SetVectorArray(_emissionProp, _emissionScratch);
                    try
                    {
                        if (verboseDrawLogging)
                        {
                            Vector4 p = bucket.MatScratch[0].GetColumn(3);
                            Debug.Log($"[WSM3D][DIAG] DrawMeshInstanced mesh={SafeName(mesh)} material={SafeName(material)} shader={(material != null ? SafeName(material.shader) : "<null>")} enableInstancing={(material != null && material.enableInstancing)} count={n} offset={offset} layer={resolvedLayer} shadows={shadows} receiveShadows={receive} firstPos=({p.x:F3}, {p.y:F3}, {p.z:F3}) fallback={_useFallbackPath}");
                        }

                        Graphics.DrawMeshInstanced(
                            mesh, 0, material,
                            bucket.MatScratch, n, bucket.Block,
                            shadows, receive, resolvedLayer, null, LightProbeUsage.Off);
                        FrameDrawCalls++;
                        offset += n;
                    }
                    catch (System.InvalidOperationException)
                    {
                        if (!_instancingErrorLogged)
                        {
                            _instancingErrorLogged = true;
                            string matName = material != null ? SafeName(material) : "<null>";
                            Debug.LogError($"[WSM3D] DrawMeshInstanced rejected material; falling back to per-instance Graphics.DrawMesh. Voxel render perf is degraded but visible. material={matName}");
                        }

                        _useFallbackPath = true;
                        DrawFallbackPath(key, bucket, total, resolvedLayer, renderCamera, shadows, receive, offset);
                        break;
                    }
                    catch (System.Exception ex)
                    {
                        if (!_instancingErrorLogged)
                        {
                            _instancingErrorLogged = true;
                            string matName = material != null ? SafeName(material) : "<null>";
                            Debug.LogError($"[WSM3D] DrawMeshInstanced threw {ex.GetType().Name}; falling back to per-instance Graphics.DrawMesh. Voxel render perf is degraded but visible. material={matName}");
                        }

                        _useFallbackPath = true;
                        DrawFallbackPath(key, bucket, total, resolvedLayer, renderCamera, shadows, receive, offset);
                        break;
                    }

                }

                    bucket.Matrices.Clear();
                bucket.Colors.Clear();
                FrameBucketCount++;
            }

            // InstancingEfficiency computed on-demand via property getter.

            if (verboseDrawLogging)
            {
                _verboseDrawLoggingArmed = false;
                _verboseDrawLoggingConsumed = true;
            }
        }

        static int _fallbackDrawDiagFrames = 0;
        /// <summary>Safe name accessor that handles destroyed Unity objects
        /// (where ReferenceEquals is non-null but == null returns true).</summary>
        static string SafeName(Object obj)
        {
            if (obj == null) return "<null>";
            try { return obj.name; }
            catch { return "<destroyed>"; }
        }

        // Reusable MPB for fallback path -- avoids per-instance allocation overhead.
        static readonly MaterialPropertyBlock _fallbackBlock = new MaterialPropertyBlock();

        static void DrawFallbackPath(Key key, Bucket bucket, int total, int layer, Camera renderCamera, ShadowCastingMode shadows, bool receive, int start = 0)
        {
            // Guard against destroyed Unity objects that passed through Submit
            // before the mesh/material was destroyed (e.g. VoxelMeshCache.Clear).
            if (key.Mesh == null || key.Material == null) return;

            int end = Mathf.Min(bucket.Matrices.Count, start + total);
            if (_fallbackDrawDiagFrames < 5)
            {
                _fallbackDrawDiagFrames++;
                Vector4 firstPos = bucket.Matrices.Count > start ? bucket.Matrices[start].GetColumn(3) : new Vector4(0,0,0,0);
                Debug.Log($"[WSM3D][DIAG-FB] DrawFallbackPath entry frame={_fallbackDrawDiagFrames} mesh={SafeName(key.Mesh)} material={SafeName(key.Material)} bucket.Matrices.Count={bucket.Matrices.Count} start={start} total={total} end={end} firstPos=({firstPos.x:F2},{firstPos.y:F2},{firstPos.z:F2}) layer={layer}");
            }

            // Cache the last tint to avoid redundant MPB rebuilds when
            // consecutive instances share the same color (common case).
            Vector4 lastTint = new Vector4(-1f, -1f, -1f, -1f);
            bool debugOutline = Core.savedSettings.DebugVoxelOutline;

            for (int i = start; i < end; i++)
            {
                Vector4 tint = bucket.Colors[i];
                if (tint != lastTint)
                {
                    _fallbackBlock.Clear();
                    _fallbackBlock.SetVector(_colorProp, tint);
                    _fallbackBlock.SetColor(_baseColorProp, tint);
                    _fallbackBlock.SetColor(_colorPropUnlit, tint);
                    _fallbackBlock.SetColor(_emissionProp, _bakeEmission);
                    lastTint = tint;
                }
                Graphics.DrawMesh(
                    key.Mesh,
                    bucket.Matrices[i],
                    key.Material,
                    layer,
                    null,
                    0,
                    _fallbackBlock,
                    shadows,
                    receive,
                    null,
                    LightProbeUsage.Off);
                FrameDrawCalls++;

                if (debugOutline)
                {
                    Vector4 p = bucket.Matrices[i].GetColumn(3);
                    Matrix4x4 debugTrs = Matrix4x4.TRS(
                        new Vector3(p.x, p.y, p.z),
                        Quaternion.identity,
                        Vector3.one * kDebugCubeSize);
                    Color debugTint = tint.w > 0f
                        ? new Color(tint.x, tint.y, tint.z, tint.w)
                        : new Color(1f, 0f, 1f, 1f);
                    _fallbackBlock.Clear();
                    _fallbackBlock.SetVector(_colorProp, debugTint);
                    _fallbackBlock.SetColor(_baseColorProp, debugTint);
                    _fallbackBlock.SetColor(_colorPropUnlit, debugTint);
                    Graphics.DrawMesh(
                        GetDebugCubeMesh(),
                        debugTrs,
                        key.Material,
                        layer,
                        null,
                        0,
                        _fallbackBlock,
                        shadows,
                        receive,
                        null,
                        LightProbeUsage.Off);
                    FrameDrawCalls++;
                    lastTint = new Vector4(-1f, -1f, -1f, -1f); // force MPB rebuild next iteration
                }
            }
        }

        static Mesh GetDebugCubeMesh()
        {
            if (_debugCubeMesh != null) return _debugCubeMesh;

            var mesh = new Mesh { name = "WSM3D.DebugVoxelCube" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f,  0.5f, -0.5f),
                new Vector3(-0.5f,  0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f,  0.5f,  0.5f),
                new Vector3(-0.5f,  0.5f,  0.5f),
            };
            mesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                2, 3, 7, 2, 7, 6,
                1, 2, 6, 1, 6, 5,
                3, 0, 4, 3, 4, 7,
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _debugCubeMesh = mesh;
            return _debugCubeMesh;
        }

        static Camera ResolveRenderCamera()
        {
            if (CameraManager.MainCamera != null && CameraManager.MainCamera.enabled)
            {
                return CameraManager.MainCamera;
            }

            return Camera.main;
        }

        static int ResolveRenderLayer(int layer, Camera cam)
        {
            if (layer != 0) return layer;

            if (cam == null) return 0;

            int mask = cam.cullingMask;
            if (mask == 0) return 0;

            int resolved = 0;
            while (((mask >> resolved) & 1) == 0 && resolved < 31)
            {
                resolved++;
            }

            return resolved;
        }

        static void LogAllCameras()
        {
            if (_allCamerasLogged) return;
            _allCamerasLogged = true;
            var cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                var cam = cameras[i];
                string name = cam != null ? cam.name : "<null>";
                bool enabled = cam != null && cam.enabled;
                bool active = cam != null && cam.isActiveAndEnabled;
                int cullingMask = cam != null ? cam.cullingMask : 0;
                float depth = cam != null ? cam.depth : 0f;
                string targetTexture = cam != null && cam.targetTexture != null ? cam.targetTexture.name : "<none>";
                Debug.Log($"[WSM3D] Flush cameras[{i}] name={name} enabled={enabled} isActiveAndEnabled={active} cullingMask=0x{cullingMask:X8} depth={depth} targetTexture={targetTexture}");
            }
        }

        static void LogRenderTarget(Camera cam, int requestedLayer, int resolvedLayer, ShadowCastingMode shadows, bool receive)
        {
            if (_renderTargetLogged) return;
            _renderTargetLogged = true;
            string camName = cam != null ? cam.name : "<null>";
            int camLayer = cam != null ? cam.gameObject.layer : -1;
            int mask = cam != null ? cam.cullingMask : 0;
            Debug.Log($"[WSM3D] MeshInstanceBatcher render target camera=ALL (was bound to CameraManager.MainCamera; resolved={camName}) cameraLayer={camLayer} cullingMask=0x{mask:X8} requestedLayer={requestedLayer} resolvedLayer={resolvedLayer} lightProbes=Off shadows={shadows} receiveShadows={receive}");
        }

        public static void Reset()
        {
            MeshInstanceBatcherBRG.Reset();
            while (_pendingSubmissions.TryDequeue(out _))
            {
            }

            Interlocked.Exchange(ref _pendingSubmissionCount, 0);
            _buckets.Clear();
            _useFallbackPath = false;
            _instancingErrorLogged = false;
            _standardInstancingAttempted = false;
            _verboseDrawLoggingArmed = false;
            _verboseDrawLoggingConsumed = false;
            _renderTargetLogged = false;
            _flushBucketDiagLogged = false;
            _allCamerasLogged = false;
            FrameDrawCalls = 0;
            FrameInstances = 0;
            FrameBucketCount = 0;
        }

        static bool _standardInstancingAttempted;

        static bool CanUseInstancedDraw(Material material, out string reason)
        {
            reason = null;
            if (material == null)
            {
                reason = "[WSM3D] DrawMeshInstanced blocked: material is null.";
                return false;
            }

            if (!SystemInfo.supportsInstancing)
            {
                reason = "[WSM3D] DrawMeshInstanced blocked: SystemInfo.supportsInstancing is false.";
                return false;
            }

            if (!material.enableInstancing)
            {
                reason = $"[WSM3D] DrawMeshInstanced blocked: material '{material.name}' has instancing disabled.";
                return false;
            }

            // Standard shader CAN support GPU instancing when enableInstancing is true
            // and the runtime has INSTANCING_ON. The previous unconditional block was
            // forcing every draw through per-instance Graphics.DrawMesh fallback,
            // causing 700ms+ frames. Now we let it through -- if DrawMeshInstanced
            // actually throws, the catch block in Flush sets _useFallbackPath.
            if (material.shader != null &&
                material.shader.name.StartsWith("Standard", System.StringComparison.Ordinal))
            {
                if (!_standardInstancingAttempted)
                {
                    _standardInstancingAttempted = true;
                    // Enable the INSTANCING_ON keyword that Standard shader needs
                    material.EnableKeyword("INSTANCING_ON");
                    Debug.Log($"[WSM3D][PERF] Allowing Standard shader instancing (enableInstancing={material.enableInstancing})");
                }
            }

            return true;
        }
    }
}
