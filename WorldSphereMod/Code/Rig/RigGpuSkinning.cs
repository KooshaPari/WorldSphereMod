using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WorldSphereMod.Rig
{
    /// <summary>
    /// Opt-in ADR-0006 backend; runtime parity and 1000 actors profiling remain gated.
    /// See docs/adr/ADR-0006-phase-6-step-9-drawprocedural-skinning.md.
    /// CPU renderers remain available until successful draw submission.
    /// </summary>
    public static class RigGpuSkinning
    {
        public const int kMaxInstancesPerRig = 8;
        public const int PositionBufferStrideBytes = 12;
        // Non-indexed procedural arguments: vertices, instances, first vertex, first instance.
        public const int IndirectArgsCount = 4;
        const int MaxBatches = 128;
        const int MaxPositionElements = 2 * 1024 * 1024;
        static readonly Dictionary<long, Batch> Batches = new Dictionary<long, Batch>();
        static readonly List<SkinnedMeshRenderer> Hidden = new List<SkinnedMeshRenderer>();
        static readonly List<long> Expired = new List<long>();
        static ComputeShader? _compute;
        static Material? _material;
        static Shader? _shader;
        static int _kernel;
        static bool _failed;

        sealed class Batch
        {
            public ComputeBuffer? Vertices, BoneIndices, Weights, Colors, Triangles, Matrices;
            public ComputeBuffer? PositionBuffer, IndirectArgsBuffer, InstanceTintBuffer;
            public int VertexCount, TriangleCount, BoneCount, Count, LastFrame;
            public Bounds Bounds;
            public Matrix4x4[] MatrixData = Array.Empty<Matrix4x4>();
            public readonly Vector4[] Tints = new Vector4[kMaxInstancesPerRig];
            public readonly List<SkinnedMeshRenderer> Renderers = new List<SkinnedMeshRenderer>();
            public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        }

        // Assets borrowed from bundle owner; only our material is owned.
        public static void ConfigureResources(ComputeShader compute, Shader shader)
        {
            Clear();
            _compute = compute;
            _shader = shader;
            if (compute == null || shader == null || !shader.isSupported) return;
            try { _kernel = compute.FindKernel("CSMain"); _material = new Material(shader); }
            catch (Exception e) { Fail(e); }
        }

        public static bool IsEnabled(SavedSettings? settings) => settings != null
            && settings.SkeletalAnimation && settings.GpuProceduralSkinning;
        public static bool CanDispatchGPU()
        {
            if (_failed || _compute == null || _shader == null || !_shader.isSupported
                || !SystemInfo.supportsComputeShaders || !SystemInfo.supportsInstancing || SystemInfo.graphicsShaderLevel < 45) return false;
            if (_material == null)
            {
                try { _material = new Material(_shader); } catch (Exception e) { Fail(e); return false; }
            }
            return true;
        }
        public static int PositionBufferElementCount(int vertexCount)
        {
            if (vertexCount <= 0) throw new ArgumentOutOfRangeException(nameof(vertexCount));
            return checked(vertexCount * kMaxInstancesPerRig);
        }

        // Unconditional hook also restores CPU rendering on flag-off and empty frames.
        public static void TickFrame()
        {
            RestoreCpuRenderers();
            Expired.Clear();
            foreach (var pair in Batches)
            {
                pair.Value.Count = 0;
                pair.Value.Renderers.Clear();
                if (Time.frameCount - pair.Value.LastFrame > 120) Expired.Add(pair.Key);
            }
            foreach (long key in Expired) { Release(Batches[key]); Batches.Remove(key); }
        }

        public static void DispatchSkin(
            long sourceKey, Mesh mesh, Transform[] bones, SkinnedMeshRenderer renderer, Color tint)
        {
            if (!CanDispatchGPU() || mesh == null || renderer == null || bones == null) return;
            try
            {
                if (!Batches.TryGetValue(sourceKey, out var set))
                {
                    if (Batches.Count >= MaxBatches) return;
                    int elements = PositionBufferElementCount(mesh.vertexCount);
                    foreach (var batch in Batches.Values) elements = checked(elements + PositionBufferElementCount(batch.VertexCount));
                    if (elements > MaxPositionElements) return;
                    set = Create(mesh, bones.Length);
                    Batches.Add(sourceKey, set);
                }
                if (set.Count >= kMaxInstancesPerRig || mesh.vertexCount != set.VertexCount || bones.Length != set.BoneCount) return;
                Matrix4x4[] bind = mesh.bindposes;
                if (bind.Length != bones.Length) return;
                for (int b = 0; b < bones.Length; b++)
                {
                    if (bones[b] == null) return;
                    set.MatrixData[set.Count * set.BoneCount + b] = bones[b].localToWorldMatrix * bind[b];
                }
                set.Tints[set.Count] = tint;
                if (set.Count == 0) set.Bounds = renderer.bounds; else set.Bounds.Encapsulate(renderer.bounds);
                set.Renderers.Add(renderer);
                set.Count++;
                set.LastFrame = Time.frameCount;
            }
            catch (Exception e) { Fail(e); }
        }

        static Batch Create(Mesh mesh, int boneCount)
        {
            if (boneCount <= 0) throw new ArgumentOutOfRangeException(nameof(boneCount));
            var set = new Batch { BoneCount = boneCount, MatrixData = new Matrix4x4[boneCount * kMaxInstancesPerRig] };
            try
            {
                set.VertexCount = mesh.vertexCount;
                var weights = mesh.boneWeights;
                if (weights.Length != set.VertexCount) throw new InvalidOperationException("Missing skin weights");
                var indices = new Vector4[weights.Length];
                var amounts = new Vector4[weights.Length];
                for (int i = 0; i < weights.Length; i++)
                {
                    var w = weights[i];
                    if (w.boneIndex0 < 0 || w.boneIndex0 >= boneCount || w.boneIndex1 < 0 || w.boneIndex1 >= boneCount
                        || w.boneIndex2 < 0 || w.boneIndex2 >= boneCount || w.boneIndex3 < 0 || w.boneIndex3 >= boneCount)
                        throw new InvalidOperationException("Skin index outside palette");
                    indices[i] = new Vector4(w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3);
                    amounts[i] = new Vector4(w.weight0, w.weight1, w.weight2, w.weight3);
                }
                var colors = mesh.colors;
                if (colors.Length != set.VertexCount) { colors = new Color[set.VertexCount]; for (int i = 0; i < colors.Length; i++) colors[i] = Color.white; }
                int[] triangles = mesh.triangles;
                set.TriangleCount = triangles.Length;
                set.Vertices = Upload(mesh.vertices, 12);
                set.BoneIndices = Upload(indices, 16);
                set.Weights = Upload(amounts, 16);
                set.Colors = Upload(colors, 16);
                set.Triangles = Upload(triangles, 4);
                set.Matrices = new ComputeBuffer(set.MatrixData.Length, 64);
                set.PositionBuffer = new ComputeBuffer(PositionBufferElementCount(set.VertexCount), PositionBufferStrideBytes);
                set.InstanceTintBuffer = new ComputeBuffer(kMaxInstancesPerRig, 16);
                set.IndirectArgsBuffer = new ComputeBuffer(IndirectArgsCount, 4, ComputeBufferType.IndirectArguments);
                set.Block.SetBuffer("_SkinnedVertices", set.PositionBuffer);
                set.Block.SetBuffer("_Colors", set.Colors);
                set.Block.SetBuffer("_Triangles", set.Triangles);
                set.Block.SetBuffer("_InstanceTints", set.InstanceTintBuffer);
                set.Block.SetInt("_VertexCount", set.VertexCount);
                return set;
            }
            catch { Release(set); throw; }
        }

        static ComputeBuffer Upload(Array data, int stride)
        {
            var buffer = new ComputeBuffer(data.Length, stride);
            try { buffer.SetData(data); return buffer; } catch { buffer.Release(); throw; }
        }

        public static void FlushDraws()
        {
            if (!CanDispatchGPU()) return;
            foreach (var set in Batches.Values)
            {
                if (set.Count == 0) continue;
                try
                {
                    set.Matrices!.SetData(set.MatrixData);
                    set.InstanceTintBuffer!.SetData(set.Tints);
                    set.IndirectArgsBuffer!.SetData(new uint[] { (uint)set.TriangleCount, (uint)set.Count, 0, 0 });
                    _compute!.SetInt("_VertexCount", set.VertexCount);
                    _compute.SetInt("_BoneCount", set.BoneCount);
                    _compute.SetInt("_InstanceCount", set.Count);
                    _compute.SetBuffer(_kernel, "_Vertices", set.Vertices);
                    _compute.SetBuffer(_kernel, "_BoneIndices", set.BoneIndices);
                    _compute.SetBuffer(_kernel, "_Weights", set.Weights);
                    _compute.SetBuffer(_kernel, "_BoneMatrices", set.Matrices);
                    _compute.SetBuffer(_kernel, "_SkinnedVertices", set.PositionBuffer);
                    _compute.Dispatch(_kernel, (set.VertexCount + 63) / 64, set.Count, 1);
                    Graphics.DrawProceduralIndirect(_material, set.Bounds, MeshTopology.Triangles,
                        set.IndirectArgsBuffer, 0, null, set.Block, ShadowCastingMode.Off, false);
                    foreach (var renderer in set.Renderers) { renderer.enabled = false; Hidden.Add(renderer); }
                }
                catch (Exception e) { Fail(e); return; }
            }
        }

        static void RestoreCpuRenderers()
        {
            foreach (var renderer in Hidden) if (renderer != null) renderer.enabled = true;
            Hidden.Clear();
        }
        static void Fail(Exception e)
        {
            _failed = true;
            // The failed batch was never hidden. Earlier successful draws are already
            // queued and must stay hidden until TickFrame to avoid duplicate geometry.
            Debug.LogWarning("[WSM3D] GPU skinning disabled; CPU fallback: " + e.Message);
        }
        public static void Clear()
        {
            RestoreCpuRenderers();
            foreach (var set in Batches.Values) Release(set);
            Batches.Clear();
            Expired.Clear();
            if (_material != null) UnityEngine.Object.Destroy(_material);
            _material = null;
            // Borrowed bundle assets survive world/feature clears. Unity null checks
            // reject them if their owner subsequently unloads the bundle.
            _failed = false;
        }
        static void Release(Batch set)
        {
            set.Vertices?.Release(); set.BoneIndices?.Release(); set.Weights?.Release();
            set.Colors?.Release(); set.Triangles?.Release(); set.Matrices?.Release();
            set.PositionBuffer?.Release(); set.IndirectArgsBuffer?.Release(); set.InstanceTintBuffer?.Release();
            set.Renderers.Clear();
        }
    }
}
