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
        /// </summary>
        public const int kMaxInstancesPerRig = 8;

        static bool _gpuCapabilityProbed;
        static bool _gpuOK;

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
        /// Release GPU buffers when skeletal rigs are cleared or the feature is toggled off.
        /// </summary>
        public static void Clear()
        {
            // Stub: no ComputeBuffer allocations yet.
            _gpuCapabilityProbed = false;
            _gpuOK = false;
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
