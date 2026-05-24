using UnityEngine;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// LRU cache for Phase 10 mid-tier proxy meshes (half-res sprite → voxel at depth=1).
    /// Stub only: <see cref="Get"/> returns null until <see cref="SpriteVoxelizer.BuildProxy"/>
    /// and emit dispatch for <see cref="LOD.LodTier.Proxy"/> are wired.
    /// See <c>docs/journeys/scratch/phase10-proxy-tier-status.md</c>.
    /// </summary>
    public static class ProxyMeshCache
    {
        /// <summary>
        /// Resolve a cached proxy mesh for <paramref name="sprite"/>.
        /// Deferred — always null; <see cref="VoxelRender"/> uses <see cref="VoxelMeshCache"/> for Proxy tier.
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
