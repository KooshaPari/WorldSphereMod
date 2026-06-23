using UnityEngine;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// DEAD STUB. Retained only so external references compile. The mid-tier "Proxy" LOD
    /// was removed when the render ladder collapsed to VOXEL-OR-INVISIBLE (Voxel near /
    /// Cull far, no billboard or proxy tier). <see cref="Get"/> always returns null.
    /// </summary>
    public static class ProxyMeshCache
    {
        /// <summary>
        /// Always null — the proxy tier no longer exists (voxel-or-invisible).
        /// </summary>
        public static Mesh Get(Sprite sprite)
        {
            if (sprite == null) return null;
            return null;
        }

        /// <summary>No-op until proxy cache is implemented. Wire into world unload with VoxelMeshCache.</summary>
        public static void Clear()
        {
        }
    }
}
