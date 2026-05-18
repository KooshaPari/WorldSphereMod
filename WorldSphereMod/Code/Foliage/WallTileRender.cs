using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WorldSphereMod.Voxel;

namespace WorldSphereMod.Foliage
{
    /// <summary>
    /// Phase 3b Step 3. Replaces the vanilla wall flush in
    /// <c>QuantumSpriteLibrary.drawWallType</c> with a 3D extruded prism
    /// per tile when <see cref="SavedSettings.CrossedQuadFoliage"/> is on.
    ///
    /// Vanilla emits a sprite quad through the QuantumSprite group system —
    /// in 3D that quad reads as a paper-thin billboard. We swap it for a
    /// short rectangular prism (4 top + 4 bottom verts, 6 box faces) drawn
    /// through <see cref="MeshInstanceBatcher"/>. The wall sprite texture
    /// is re-used as the foliage material's albedo so colors stay roughly
    /// aligned with vanilla; for now the prism samples the shared foliage
    /// material directly (sprite-driven coloring is left for a polish pass).
    ///
    /// Animated walls (<c>animated_wall</c>) are deferred — they need a
    /// per-frame frame-pick that the cached mesh path can't trivially
    /// represent, so we fall through to vanilla for those.
    /// </summary>
    [HarmonyPatch(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawWallType))]
    public static class WallTileRender
    {
        const float kPrismHeight = 0.5f;
        const float kPrismHalfWidth = 0.5f;

        static Mesh? _sharedPrism;

        [HarmonyPrefix]
        public static bool Prefix(TopTileType pTileTypeAsset, QuantumSpriteAsset pAsset, bool pTransparentBuildings, Material pMaterial)
        {
            if (!Core.IsWorld3D || !Core.savedSettings.CrossedQuadFoliage) return true;
            if (pTileTypeAsset == null) return true;
            if (!pTileTypeAsset.wall) return true;
            if (pTileTypeAsset.animated_wall) return true;

            if (!FoliageMaterial.EnsureMaterial()) return true;
            Material? mat = FoliageMaterial.Get();
            if (mat == null) return true;

            List<WorldTile> tiles = pTileTypeAsset.getCurrentTiles();
            if (tiles == null || tiles.Count == 0) return false;

            Mesh mesh = GetOrBuildPrism();

            for (int i = 0; i < tiles.Count; i++)
            {
                WorldTile t = tiles[i];
                if (t == null) continue;
                if (t.zone == null || !t.zone.visible) continue;

                Vector2 pos2 = new Vector2(t.pos.x, t.pos.y);
                Vector3 pos3 = Tools.To3DTileHeight(pos2);
                Quaternion rot = Tools.GetRotation(t.pos);
                Matrix4x4 trs = Matrix4x4.TRS(pos3, rot, Vector3.one);

                MeshInstanceBatcher.Submit(mesh, mat, trs, Color.white);
            }

            return false;
        }

        static Mesh GetOrBuildPrism()
        {
            if (_sharedPrism != null) return _sharedPrism;

            float hw = kPrismHalfWidth;
            float h = kPrismHeight;

            // 8 corners: 0..3 bottom (y=0) CCW from -X-Z, 4..7 top (y=h) same order.
            Vector3[] verts = new Vector3[24];
            Vector2[] uvs = new Vector2[24];
            int[] tris = new int[36];

            // Per-face: 4 verts, 2 tris. Order: bottom, top, +X, -X, +Z, -Z.
            // We duplicate vertices per face so flat normals + per-face UVs work.
            Vector3 bnn = new Vector3(-hw, 0f, -hw);
            Vector3 bpn = new Vector3( hw, 0f, -hw);
            Vector3 bpp = new Vector3( hw, 0f,  hw);
            Vector3 bnp = new Vector3(-hw, 0f,  hw);
            Vector3 tnn = new Vector3(-hw,  h, -hw);
            Vector3 tpn = new Vector3( hw,  h, -hw);
            Vector3 tpp = new Vector3( hw,  h,  hw);
            Vector3 tnp = new Vector3(-hw,  h,  hw);

            // Helper for face emission (local mutable index).
            int v = 0;
            int triIdx = 0;
            void EmitFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                verts[v + 0] = a; uvs[v + 0] = new Vector2(0f, 0f);
                verts[v + 1] = b; uvs[v + 1] = new Vector2(1f, 0f);
                verts[v + 2] = c; uvs[v + 2] = new Vector2(1f, 1f);
                verts[v + 3] = d; uvs[v + 3] = new Vector2(0f, 1f);
                tris[triIdx + 0] = v + 0;
                tris[triIdx + 1] = v + 2;
                tris[triIdx + 2] = v + 1;
                tris[triIdx + 3] = v + 0;
                tris[triIdx + 4] = v + 3;
                tris[triIdx + 5] = v + 2;
                v += 4;
                triIdx += 6;
            }

            EmitFace(tnn, tpn, tpp, tnp); // top (y = h, normal +Y)
            EmitFace(bnp, bpp, bpn, bnn); // bottom (y = 0, normal -Y)
            EmitFace(bpn, bpp, tpp, tpn); // +X face
            EmitFace(bnp, bnn, tnn, tnp); // -X face
            EmitFace(bpp, bnp, tnp, tpp); // +Z face
            EmitFace(bnn, bpn, tpn, tnn); // -Z face

            Mesh m = new Mesh { name = "wsm3d.wall.prism" };
            m.vertices = verts;
            m.uv = uvs;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            _sharedPrism = m;
            return m;
        }

        public static void Reset()
        {
            if (_sharedPrism != null)
            {
                Object.Destroy(_sharedPrism);
                _sharedPrism = null;
            }
        }
    }
}
