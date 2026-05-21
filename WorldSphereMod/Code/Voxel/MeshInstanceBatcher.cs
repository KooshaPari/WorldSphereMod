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
        const int kBatch = 1023;
        const float kDebugCubeSize = 0.5f;
        static bool UseBrg => Core.savedSettings != null && Core.savedSettings.UseBRG;

        public static long FrameDrawCalls;
        public static long FrameInstances;
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
        static bool _allCamerasLogged;
        static int _mainThreadId;
        static Mesh? _debugCubeMesh;

        public static void SetMainThread()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static void Submit(Mesh mesh, Material mat, Matrix4x4 matrix, Color tint)
        {
            if (UseBrg && MeshInstanceBatcherBRG.TrySubmit(mesh, mat, matrix, tint))
            {
                return;
            }

            if (mesh == null || mat == null) return;

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

            foreach (var kv in _buckets)
            {
                var bucket = kv.Value;
                int total = bucket.Matrices.Count;
                FrameInstances += total;
                if (_useFallbackPath)
                {
                    DrawFallbackPath(kv.Key, bucket, total, resolvedLayer, renderCamera, shadows, receive);
                    bucket.Matrices.Clear();
                    bucket.Colors.Clear();
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

                    if (!CanUseInstancedDraw(kv.Key.Material, out string disableReason))
                    {
                        if (!_instancingErrorLogged)
                        {
                            _instancingErrorLogged = true;
                            Debug.LogError(disableReason);
                        }
                        DrawFallbackPath(kv.Key, bucket, total, resolvedLayer, renderCamera, shadows, receive, offset);
                        break;
                    }

                    bucket.Block.Clear();
                    bucket.Block.SetVectorArray(_colorProp, bucket.ColScratch);
                    try
                    {
                        if (verboseDrawLogging)
                        {
                            Vector4 p = bucket.MatScratch[0].GetColumn(3);
                            Debug.Log($"[WSM3D][DIAG] DrawMeshInstanced mesh={kv.Key.Mesh?.name ?? "<null>"} material={kv.Key.Material?.name ?? "<null>"} shader={kv.Key.Material?.shader?.name ?? "<null>"} enableInstancing={(kv.Key.Material != null && kv.Key.Material.enableInstancing)} count={n} offset={offset} layer={resolvedLayer} shadows={shadows} receiveShadows={receive} firstPos=({p.x:F3}, {p.y:F3}, {p.z:F3}) fallback={_useFallbackPath}");
                        }

                        Graphics.DrawMeshInstanced(
                            kv.Key.Mesh, 0, kv.Key.Material,
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
                            string matName = kv.Key.Material != null ? kv.Key.Material.shader.name : "<null>";
                            Debug.LogError($"[WSM3D] DrawMeshInstanced rejected material; falling back to per-instance Graphics.DrawMesh. Voxel render perf is degraded but visible. material={matName}");
                        }

                        _useFallbackPath = true;
                        DrawFallbackPath(kv.Key, bucket, total, resolvedLayer, renderCamera, shadows, receive, offset);
                        break;
                    }
                    catch (System.Exception ex)
                    {
                        if (!_instancingErrorLogged)
                        {
                            _instancingErrorLogged = true;
                            string matName = kv.Key.Material != null ? kv.Key.Material.shader?.name : "<null>";
                            Debug.LogError($"[WSM3D] DrawMeshInstanced threw {ex.GetType().Name}; falling back to per-instance Graphics.DrawMesh. Voxel render perf is degraded but visible. material={matName}");
                        }

                        _useFallbackPath = true;
                        DrawFallbackPath(kv.Key, bucket, total, resolvedLayer, renderCamera, shadows, receive, offset);
                        break;
                    }

                }

                bucket.Matrices.Clear();
                bucket.Colors.Clear();
            }

            if (verboseDrawLogging)
            {
                _verboseDrawLoggingArmed = false;
                _verboseDrawLoggingConsumed = true;
            }
        }

        static int _fallbackDrawDiagFrames = 0;
        static void DrawFallbackPath(Key key, Bucket bucket, int total, int layer, Camera renderCamera, ShadowCastingMode shadows, bool receive, int start = 0)
        {
            int end = Mathf.Min(bucket.Matrices.Count, start + total);
            if (_fallbackDrawDiagFrames < 5)
            {
                _fallbackDrawDiagFrames++;
                Vector4 firstPos = bucket.Matrices.Count > start ? bucket.Matrices[start].GetColumn(3) : new Vector4(0,0,0,0);
                Debug.Log($"[WSM3D][DIAG-FB] DrawFallbackPath entry frame={_fallbackDrawDiagFrames} mesh={key.Mesh?.name ?? "<null>"} material={key.Material?.name ?? "<null>"} bucket.Matrices.Count={bucket.Matrices.Count} start={start} total={total} end={end} firstPos=({firstPos.x:F2},{firstPos.y:F2},{firstPos.z:F2}) layer={layer}");
            }
            for (int i = start; i < end; i++)
            {
                bucket.Block.Clear();
                Vector4 tint = bucket.Colors[i];
                bucket.Block.SetVector(_colorProp, tint);
                bucket.Block.SetColor(_baseColorProp, tint);
                bucket.Block.SetColor(_colorPropUnlit, tint);
                Graphics.DrawMesh(
                    key.Mesh,
                    bucket.Matrices[i],
                    key.Material,
                    layer,
                    null,
                    0,
                    bucket.Block,
                    shadows,
                    receive,
                    null,
                    LightProbeUsage.Off);
                FrameDrawCalls++;

                if (Core.savedSettings.DebugVoxelOutline)
                {
                    Vector4 p = bucket.Matrices[i].GetColumn(3);
                    Matrix4x4 debugTrs = Matrix4x4.TRS(
                        new Vector3(p.x, p.y, p.z),
                        Quaternion.identity,
                        Vector3.one * kDebugCubeSize);
                    Color debugTint = tint.w > 0f
                        ? new Color(tint.x, tint.y, tint.z, tint.w)
                        : new Color(1f, 0f, 1f, 1f);
                    bucket.Block.Clear();
                    bucket.Block.SetVector(_colorProp, debugTint);
                    bucket.Block.SetColor(_baseColorProp, debugTint);
                    bucket.Block.SetColor(_colorPropUnlit, debugTint);
                    Graphics.DrawMesh(
                        GetDebugCubeMesh(),
                        debugTrs,
                        key.Material,
                        layer,
                        null,
                        0,
                        bucket.Block,
                        shadows,
                        receive,
                        null,
                        LightProbeUsage.Off);
                    FrameDrawCalls++;
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
            _verboseDrawLoggingArmed = false;
            _verboseDrawLoggingConsumed = false;
            _renderTargetLogged = false;
            _allCamerasLogged = false;
            FrameDrawCalls = 0;
            FrameInstances = 0;
        }

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

            if (material.shader != null &&
                material.shader.name.StartsWith("Standard", System.StringComparison.Ordinal))
            {
                if (!material.IsKeywordEnabled("INSTANCING_ON"))
                {
                    material.EnableKeyword("INSTANCING_ON");
                }

                if (!material.IsKeywordEnabled("INSTANCING_ON"))
                {
                    reason = $"[WSM3D] DrawMeshInstanced blocked: Standard material '{material.name}' does not expose INSTANCING_ON.";
                    return false;
                }
            }

            return true;
        }
    }
}
