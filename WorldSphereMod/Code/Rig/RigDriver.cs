using System;
using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.Rig
{
    /// <summary>
    /// Phase 6 Step 7: per-actor skinned submission. When the GPU capability probe
    /// passes, runs <c>VoxelSkin.compute</c> once per (sprite,rig) base mesh per
    /// frame and submits a CPU-side proxy <see cref="Mesh"/> whose position stream
    /// is overwritten from the dispatch output. The CPU bind-pose shortcut from
    /// Step 5 remains as the fallback when compute is unavailable or any GPU
    /// operation throws — first failure flips the entire actor stream to CPU and
    /// logs once.
    /// </summary>
    public static class RigDriver
    {
        static readonly Dictionary<long, Mesh> _skinnedMeshCache = new Dictionary<long, Mesh>();
        static readonly Dictionary<long, GpuMesh> _gpu = new Dictionary<long, GpuMesh>();

        static ComputeShader? _skinCS;
        static int _kernel;
        static bool _gpuProbed;
        static bool _gpuOK;
        static bool _gpuStubLogged;
        static bool _gpuFailureLogged;

        static GraphicsBuffer? _matricesBuf;
        static readonly float[] _matricesScratch = new float[HumanoidRig.Bones.Length * 16];

        // Shader property IDs — resolved lazily on first dispatch.
        static int _idVertices, _idBoneIndices, _idBoneMatrices, _idSkinnedVertices, _idVertexCount;
        static bool _idsResolved;

        struct GpuMesh
        {
            public GraphicsBuffer Vertices;       // StructuredBuffer<float3>, length = vertexCount
            public GraphicsBuffer BoneIndices;    // StructuredBuffer<uint>,   length = vertexCount
            public GraphicsBuffer Skinned;        // RWStructuredBuffer<float3>, length = vertexCount
            public Mesh Proxy;                    // CPU-readable mesh whose vertex positions we overwrite per frame
            public Vector3[] Scratch;             // Reusable readback buffer
            public int VertexCount;
        }

        static void EnsureGpu()
        {
            if (_gpuProbed) return;
            _gpuProbed = true;
            _skinCS = Resources.Load<ComputeShader>("Shaders/VoxelSkin");
            if (_skinCS == null) return;
            _kernel = _skinCS.FindKernel("CSMain");
            _gpuOK = SystemInfo.supportsComputeShaders && _kernel >= 0;
        }

        static void ResolveShaderIds()
        {
            if (_idsResolved) return;
            _idVertices        = Shader.PropertyToID("_Vertices");
            _idBoneIndices     = Shader.PropertyToID("_BoneIndices");
            _idBoneMatrices    = Shader.PropertyToID("_BoneMatrices");
            _idSkinnedVertices = Shader.PropertyToID("_SkinnedVertices");
            _idVertexCount     = Shader.PropertyToID("_VertexCount");
            _idsResolved = true;
        }

        public static bool SubmitSkinnedActor(
            Actor a, Vector3 pos, Quaternion rot, Vector3 scl, Color tint,
            RigType rigType)
        {
            if (a == null || a.asset == null) return false;
            Sprite? sp = a.calculateMainSprite();
            if (sp == null) return false;
            if (MeshInstanceBatcher.InstancingBroken) return false;

            SkinnedVoxelMesh svm = RigCache.GetOrBuild(sp, rigType);
            if (svm.BaseMesh == null) return false;

            EnsureGpu();

            // Cast to uint before shifting so the int's sign bit doesn't sign-extend across
            // the long, which would collide sprite IDs whose bit 23 differs.
            long key = ((long)(uint)sp.GetInstanceID() << 8) | (byte)rigType;
            Mesh? toSubmit = null;

            // GPU dispatch path is disabled pending Phase 6 Step 9 (per-actor DrawProcedural).
            // The current per-(sprite,rig) proxy mesh is overwritten by every actor sharing the
            // same key in the same frame — the batcher reads vertices at Flush time, so all
            // instances see whichever actor was processed last. Forcing _gpuOK=false routes
            // every humanoid through the correct CPU bind-pose path until DrawProcedural lands.
            _gpuOK = false;

            if (_gpuOK && rigType == RigType.Humanoid && svm.BoneIndices != null && svm.BaseMesh.vertexCount > 0)
            {
                if (!_gpuStubLogged)
                {
                    _gpuStubLogged = true;
                    Debug.Log("[WSM3D] Compute skinning online; dispatching VoxelSkin per humanoid actor.");
                }
                try
                {
                    AnimationFrameData? fd = a.getAnimationFrameData();
                    toSubmit = DispatchSkin(key, svm, fd);
                }
                catch (Exception ex)
                {
                    if (!_gpuFailureLogged)
                    {
                        _gpuFailureLogged = true;
                        Debug.LogWarning($"[WSM3D] GPU skinning failed, falling back to CPU bind-pose: {ex.Message}");
                    }
                    _gpuOK = false;
                    toSubmit = null;
                }
            }

            if (toSubmit == null)
            {
                // CPU bind-pose fallback (Step 5 behaviour).
                if (!_skinnedMeshCache.TryGetValue(key, out Mesh? skinned))
                {
                    skinned = svm.BaseMesh;
                    _skinnedMeshCache[key] = skinned;
                }
                toSubmit = skinned;
            }

            Matrix4x4 trs = Matrix4x4.TRS(pos, rot, scl);
            return VoxelRender.Submit(toSubmit, trs, tint);
        }

        static Mesh DispatchSkin(long key, SkinnedVoxelMesh svm, AnimationFrameData? fd)
        {
            ResolveShaderIds();

            if (!_gpu.TryGetValue(key, out GpuMesh g))
            {
                g = BuildGpuMesh(svm);
                _gpu[key] = g;
            }

            EnsureMatricesBuffer();
            // Matrix4x4 is column-major in Unity; HLSL float4x4 with mul(M, v) expects
            // row-major-ish semantics but Unity's compute path already matches the C#
            // convention when copied as 16 contiguous floats. SetMatrices is not
            // available on GraphicsBuffer pre-2022, so flatten manually into a scratch
            // float[]. Step 8 swaps the bind-pose stub for the AnimationFrameData
            // projection from HumanoidRig.Evaluate.
            var matrices = HumanoidRig.Evaluate(fd, 1f);
            int boneCount = matrices.Length;
            for (int b = 0; b < boneCount; b++)
            {
                Matrix4x4 m = matrices[b];
                int o = b * 16;
                _matricesScratch[o +  0] = m.m00; _matricesScratch[o +  1] = m.m01; _matricesScratch[o +  2] = m.m02; _matricesScratch[o +  3] = m.m03;
                _matricesScratch[o +  4] = m.m10; _matricesScratch[o +  5] = m.m11; _matricesScratch[o +  6] = m.m12; _matricesScratch[o +  7] = m.m13;
                _matricesScratch[o +  8] = m.m20; _matricesScratch[o +  9] = m.m21; _matricesScratch[o + 10] = m.m22; _matricesScratch[o + 11] = m.m23;
                _matricesScratch[o + 12] = m.m30; _matricesScratch[o + 13] = m.m31; _matricesScratch[o + 14] = m.m32; _matricesScratch[o + 15] = m.m33;
            }
            _matricesBuf!.SetData(_matricesScratch, 0, 0, boneCount * 16);

            _skinCS!.SetBuffer(_kernel, _idVertices,        g.Vertices);
            _skinCS!.SetBuffer(_kernel, _idBoneIndices,     g.BoneIndices);
            _skinCS!.SetBuffer(_kernel, _idBoneMatrices,    _matricesBuf!);
            _skinCS!.SetBuffer(_kernel, _idSkinnedVertices, g.Skinned);
            _skinCS!.SetInt(_idVertexCount, g.VertexCount);

            int groups = (g.VertexCount + 63) / 64;
            _skinCS!.Dispatch(_kernel, groups, 1, 1);

            // TODO Step 8: replace synchronous GetData+SetVertices with
            // Graphics.DrawProcedural(g.Skinned, ...) to keep the skinned positions
            // entirely on the GPU. Current path stalls the CPU once per (key,frame)
            // but reuses the per-key proxy mesh across actors that share a sprite.
            g.Skinned.GetData(g.Scratch);
            g.Proxy.SetVertices(g.Scratch);
            g.Proxy.RecalculateBounds();
            return g.Proxy;
        }

        static GpuMesh BuildGpuMesh(SkinnedVoxelMesh svm)
        {
            int vc = svm.BaseMesh.vertexCount;
            var verts = svm.BaseMesh.vertices; // Vector3[] (12 B each)
            var boneIdxBytes = svm.BoneIndices ?? Array.Empty<byte>();
            var boneIdx = new uint[vc];
            for (int i = 0; i < vc && i < boneIdxBytes.Length; i++)
            {
                boneIdx[i] = boneIdxBytes[i];
            }

            var gVerts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vc, sizeof(float) * 3);
            gVerts.SetData(verts);
            var gBoneIdx = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vc, sizeof(uint));
            gBoneIdx.SetData(boneIdx);
            var gSkinned = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vc, sizeof(float) * 3);

            // Proxy mesh: clone of the base mesh's topology + colors, positions overwritten
            // per frame from the readback. Has CPU-side data because we SetVertices each
            // frame; MarkDynamic gives Unity a hint to allocate a GPU upload buffer.
            var proxy = new Mesh { name = svm.BaseMesh.name + ":gpuskin" };
            if (svm.BaseMesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32)
            {
                proxy.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            proxy.SetVertices(verts);
            var cols = svm.BaseMesh.colors32;
            if (cols != null && cols.Length == vc) proxy.SetColors(cols);
            proxy.SetTriangles(svm.BaseMesh.triangles, 0);
            proxy.RecalculateNormals();
            proxy.RecalculateBounds();
            proxy.MarkDynamic();

            return new GpuMesh
            {
                Vertices = gVerts,
                BoneIndices = gBoneIdx,
                Skinned = gSkinned,
                Proxy = proxy,
                Scratch = new Vector3[vc],
                VertexCount = vc,
            };
        }

        static void EnsureMatricesBuffer()
        {
            if (_matricesBuf != null) return;
            // Each float4x4 = 16 floats = 64 bytes. HumanoidRig declares 12 bones; future
            // rigs may grow this, so size off Bones.Length rather than hardcoding.
            int bones = HumanoidRig.Bones.Length;
            _matricesBuf = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bones, sizeof(float) * 16);
        }

        /// <summary>
        /// Called by RigCache when an entry is evicted or the cache is cleared.
        /// Disposes the matching GPU buffer set + proxy mesh so each cache eviction
        /// has matching GPU cleanup (otherwise the (sprite,rig) entry leaks until
        /// world unload).
        /// </summary>
        public static void ReleaseGpuMesh(long key)
        {
            if (_gpu.TryGetValue(key, out var g))
            {
                g.Vertices?.Dispose();
                g.BoneIndices?.Dispose();
                g.Skinned?.Dispose();
                if (g.Proxy != null) UnityEngine.Object.Destroy(g.Proxy);
                _gpu.Remove(key);
            }
            _skinnedMeshCache.Remove(key);
        }

        public static void Clear()
        {
            _skinnedMeshCache.Clear();

            foreach (var g in _gpu.Values)
            {
                g.Vertices?.Dispose();
                g.BoneIndices?.Dispose();
                g.Skinned?.Dispose();
                if (g.Proxy != null) UnityEngine.Object.Destroy(g.Proxy);
            }
            _gpu.Clear();

            _matricesBuf?.Dispose();
            _matricesBuf = null;

            _gpuProbed = false;
            _gpuOK = false;
            _gpuStubLogged = false;
            _gpuFailureLogged = false;
            _idsResolved = false;
            _skinCS = null;
        }
    }
}
