using System.Collections.Generic;
using UnityEngine;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.Rig
{
    /// <summary>
    /// Phase 6 Step 5: per-actor skinned submission shim. CPU bind-pose only — every
    /// bone matrix from <see cref="HumanoidRig.Evaluate"/> is currently identity, so the
    /// skinned mesh is byte-identical to the cached base mesh and we forward straight
    /// to <see cref="VoxelRender.Submit"/>. Step 6 introduces real per-vertex skinning
    /// (GPU compute kernel) and replaces the reuse-base-mesh shortcut here.
    /// </summary>
    public static class RigDriver
    {
        static readonly Dictionary<long, Mesh> _skinnedMeshCache = new Dictionary<long, Mesh>();

        static ComputeShader? _skinCS;
        static int _kernel;
        static bool _gpuProbed;
        static bool _gpuOK;
        static bool _gpuStubLogged;

        static void EnsureGpu()
        {
            if (_gpuProbed) return;
            _gpuProbed = true;
            _skinCS = Resources.Load<ComputeShader>("Shaders/VoxelSkin");
            if (_skinCS == null) return;
            _kernel = _skinCS.FindKernel("CSMain");
            _gpuOK = SystemInfo.supportsComputeShaders && _kernel >= 0;
        }

        public static void SubmitSkinnedActor(
            Actor a, Vector3 pos, Quaternion rot, Vector3 scl, Color tint,
            RigType rigType)
        {
            if (a == null || a.asset == null) return;
            Sprite? sp = a.calculateMainSprite();
            if (sp == null) return;

            SkinnedVoxelMesh svm = RigCache.GetOrBuild(sp, rigType);
            if (svm.BaseMesh == null) return;

            // Step 7: real per-frame compute skinning dispatch + managed-side buffer
            // plumbing. Step 6 lands only the shader source + capability probe.
            EnsureGpu();
            if (_gpuOK && !_gpuStubLogged)
            {
                _gpuStubLogged = true;
                Debug.Log("[WSM3D] Compute skinning available; ship-time using CPU bind-pose pending Step 7.");
            }

            // Step 5: CPU bind-pose skinning. HumanoidRig.Evaluate returns identity
            // matrices today, so the skinned vertex buffer equals the base mesh — cache
            // the reference and skip the per-vertex transform pass. Step 6 swaps this
            // for a real per-frame skinning dispatch.
            long key = ((long)sp.GetInstanceID() << 8) | (byte)rigType;
            if (!_skinnedMeshCache.TryGetValue(key, out Mesh? skinned))
            {
                skinned = svm.BaseMesh;
                _skinnedMeshCache[key] = skinned;
            }

            Matrix4x4 trs = Matrix4x4.TRS(pos, rot, scl);
            VoxelRender.Submit(skinned, trs, tint);
        }

        public static void Clear()
        {
            _skinnedMeshCache.Clear();
            _gpuProbed = false;
            _gpuOK = false;
            _gpuStubLogged = false;
            _skinCS = null;
        }
    }
}
