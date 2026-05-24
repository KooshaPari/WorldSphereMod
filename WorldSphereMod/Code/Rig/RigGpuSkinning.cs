using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.Rig
{
    /// <summary>
    /// ADR-0006 scaffold: GPU compute skinning with per-(sprite, rig) StructuredBuffers
    /// and <see cref="Graphics.DrawProceduralIndirect"/>. Stubs only — the live humanoid
    /// path remains <see cref="RigDriver"/> + <see cref="SkinnedMeshRenderer"/> until
    /// Phase 5b (<c>VoxelSkinned.shader</c>) and the full buffer lifecycle land.
    /// </summary>
    /// <seealso cref="docs/adr/ADR-0006-phase-6-step-9-drawprocedural-skinning.md"/>
    public static class RigGpuSkinning
    {
        /// <summary>
        /// Per-(sprite, rig) instance cap for position-buffer slicing (ADR-0006 § Open Questions).
        /// Sized for Phase 10: 1000 actors ÷ ~50 unique rigs ≈ 20 actors/rig on average;
        /// cap at 8 to leave headroom for popular rigs (e.g. humanoids). Each position buffer
        /// holds <c>vertexCount * kMaxInstancesPerRig</c> float3 elements (see
        /// <see cref="PositionBufferElementCount"/>).
        /// </summary>
        public const int kMaxInstancesPerRig = 8;

        /// <summary>Stride for per-vertex float3 positions in the per-rig StructuredBuffer.</summary>
        public const int PositionBufferStrideBytes = sizeof(float) * 3;

        /// <summary>
        /// <see cref="Graphics.DrawProceduralIndirect"/> args buffer length (indexCount,
        /// instanceCount, startIndex, baseVertex, startInstance).
        /// </summary>
        public const int IndirectArgsCount = 5;

        /// <summary>
        /// Per-(sprite, rig) GPU buffer handles. Key matches <see cref="RigCache"/>:
        /// <c>((long)(uint)sprite.GetInstanceID() &lt;&lt; 8) | (byte)rigType</c>.
        /// </summary>
        static readonly Dictionary<long, RigGpuBufferSet> _perRigBuffers =
            new Dictionary<long, RigGpuBufferSet>(64);

        static bool _gpuCapabilityProbed;
        static bool _gpuOK;

        /// <summary>
        /// ComputeBuffer field stubs for one (sprite, rig) batch (ADR-0006 steps 1–2).
        /// Not allocated until the full GPU path replaces the scaffold.
        /// </summary>
        sealed class RigGpuBufferSet
        {
            /// <summary>
            /// Skinned float3 positions: count <c>vertexCount * kMaxInstancesPerRig</c>,
            /// stride <see cref="PositionBufferStrideBytes"/>.
            /// </summary>
            public ComputeBuffer? PositionBuffer;

            /// <summary>
            /// Indirect draw args ({indexCount, instanceCount, 0, 0, 0}); length
            /// <see cref="IndirectArgsCount"/>.
            /// </summary>
            public ComputeBuffer? IndirectArgsBuffer;

            /// <summary>
            /// Optional per-actor RGBA tints (ADR-0006 § Neutral); deferred to Phase 6 Step 10.
            /// </summary>
            public ComputeBuffer? InstanceTintBuffer;
        }

        /// <summary>
        /// True when both <see cref="SavedSettings.SkeletalAnimation"/> and
        /// <see cref="SavedSettings.GpuProceduralSkinning"/> are enabled.
        /// </summary>
        public static bool IsEnabled(SavedSettings? settings)
        {
            return settings != null
                && settings.SkeletalAnimation
                && settings.GpuProceduralSkinning;
        }

        /// <summary>
        /// One-time probe for compute + indirect procedural draw support.
        /// Stub: always returns false until the full ADR-0006 path is implemented.
        /// </summary>
        public static bool CanDispatchGPU()
        {
            if (!_gpuCapabilityProbed)
            {
                _gpuCapabilityProbed = true;
                _gpuOK = ProbeGpuSkinningSupport();
            }

            return _gpuOK;
        }

        /// <summary>
        /// Per-frame hook from <see cref="RigDriver.Update"/> when <see cref="IsEnabled"/> is true.
        /// Future: batch actors by (sprite, rig), dispatch <c>VoxelSkin.compute</c>, draw indirect.
        /// </summary>
        public static void TickFrame()
        {
            if (!CanDispatchGPU())
            {
                return;
            }

            // Stub: no compute dispatch or DrawProceduralIndirect yet.
        }

        /// <summary>
        /// Element count for a per-rig position StructuredBuffer
        /// (<c>vertexCount * kMaxInstancesPerRig</c>, ADR-0006 implementation step 1).
        /// </summary>
        public static int PositionBufferElementCount(int vertexCount)
        {
            return vertexCount * kMaxInstancesPerRig;
        }

        /// <summary>
        /// Release GPU buffers when skeletal rigs are cleared or the feature is toggled off.
        /// </summary>
        public static void Clear()
        {
            foreach (var set in _perRigBuffers.Values)
            {
                ReleaseBufferSet(set);
            }

            _perRigBuffers.Clear();
            _gpuCapabilityProbed = false;
            _gpuOK = false;
        }

        static void ReleaseBufferSet(RigGpuBufferSet set)
        {
            set.PositionBuffer?.Release();
            set.PositionBuffer = null;
            set.IndirectArgsBuffer?.Release();
            set.IndirectArgsBuffer = null;
            set.InstanceTintBuffer?.Release();
            set.InstanceTintBuffer = null;
        }

        /// <summary>
        /// Future: skin one actor instance into <c>positionBuffer[vertexCount * instanceId + vid]</c>.
        /// </summary>
        public static void DispatchSkin(
            Sprite sprite,
            RigType rigType,
            int instanceId,
            Matrix4x4[] boneMatrices)
        {
            // Stub — not called until RigDriver batches GPU actors.
        }

        /// <summary>
        /// Future: issue one DrawProceduralIndirect per (sprite, rig) batch after all dispatches.
        /// </summary>
        public static void FlushDraws()
        {
            // Stub — pairs with DispatchSkin batching in TickFrame.
        }

        static bool ProbeGpuSkinningSupport()
        {
            // Full path requires SystemInfo.supportsComputeShaders, indirect args buffers,
            // and VoxelSkinned.shader (Phase 5b). Intentionally false for the scaffold.
            return false;
        }
    }
}
