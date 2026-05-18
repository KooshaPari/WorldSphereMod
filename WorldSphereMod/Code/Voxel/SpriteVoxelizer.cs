using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// Converts a 2D pixel-art <see cref="Sprite"/> into a 3D voxel <see cref="Mesh"/>.
    /// Every opaque texel becomes a unit cube in local space; faces hidden by neighbouring
    /// opaque texels are culled. Per-cube color is baked into a vertex color attribute so
    /// the existing palette renders correctly under the Phase 5 directional light + shadow
    /// stack without per-instance materials.
    ///
    /// The output is pivoted so that local-space (0, 0, 0) is the sprite pivot, with the
    /// sprite plane lying on XY and +Z = "front." That matches the upstream sprite-quad
    /// orientation expected by <see cref="Tools.GetCameraAngle"/> and friends, so a voxel
    /// mesh can be dropped into the existing actor/building render passes without any
    /// rotation-frame remapping.
    /// </summary>
    public static class SpriteVoxelizer
    {
        /// <summary>Voxel depth in texels. 1 = flat extruded card, &gt;1 = chunkier. </summary>
        public const int DefaultDepth = 1;

        /// <summary>
        /// Build a voxel mesh from the given sprite. Pulls pixels via the same atlas-aware
        /// path the terrain uses (<see cref="Tools.PixelsFromSpriteAtlas"/>) when the sprite
        /// rect is &lt;= 8x8, otherwise uses the full sprite rect. Caller is responsible for
        /// caching the result; see <see cref="VoxelMeshCache"/>.
        /// </summary>
        public static Mesh Build(Sprite sprite, int depth = DefaultDepth)
        {
            if (sprite == null || sprite.texture == null)
            {
                return CreateEmpty();
            }

            // Read the sprite's rectangle out of its atlas. We don't use the
            // 8x8 fast-path here because actor sprites are typically larger;
            // PixelsFromSpriteAtlas is hardcoded to 8x8 in the upstream code.
            Rect r = sprite.textureRect;
            int w = Mathf.Max(1, (int)r.width);
            int h = Mathf.Max(1, (int)r.height);
            int x0 = (int)r.x;
            int y0 = (int)r.y;
            Color32[] tex = sprite.texture.GetPixels32();
            int texW = sprite.texture.width;

            // Build the alpha mask. We treat any pixel with alpha > 16 as solid;
            // matches the threshold used elsewhere for WorldBox pixel art.
            bool[,,] solid = new bool[w, h, depth];
            Color32[,,] color = new Color32[w, h, depth];
            for (int y = 0; y < h; y++)
            {
                int row = (y0 + y) * texW + x0;
                for (int x = 0; x < w; x++)
                {
                    Color32 c = tex[row + x];
                    if (c.a > 16)
                    {
                        for (int z = 0; z < depth; z++)
                        {
                            solid[x, y, z] = true;
                            color[x, y, z] = c;
                        }
                    }
                }
            }

            // Pivot at sprite.pivot so the mesh sits where the sprite quad would have.
            Vector2 pivot = sprite.pivot;
            float ppu = Mathf.Max(1f, sprite.pixelsPerUnit);
            Vector3 origin = new Vector3(-pivot.x / ppu, -pivot.y / ppu, -(depth * 0.5f) / ppu);
            float cell = 1f / ppu;

            var verts = new List<Vector3>(w * h * 8);
            var cols  = new List<Color32>(w * h * 8);
            var tris  = new List<int>(w * h * 36);

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int z = 0; z < depth; z++)
                    {
                        if (!solid[x, y, z]) continue;
                        Color32 c = color[x, y, z];
                        Vector3 o = origin + new Vector3(x * cell, y * cell, z * cell);

                        // +X face
                        if (x == w - 1 || !solid[x + 1, y, z])
                            AddFace(verts, tris, cols, c,
                                o + new Vector3(cell, 0, 0),
                                o + new Vector3(cell, cell, 0),
                                o + new Vector3(cell, cell, cell),
                                o + new Vector3(cell, 0, cell));
                        // -X face
                        if (x == 0 || !solid[x - 1, y, z])
                            AddFace(verts, tris, cols, c,
                                o + new Vector3(0, 0, cell),
                                o + new Vector3(0, cell, cell),
                                o + new Vector3(0, cell, 0),
                                o + new Vector3(0, 0, 0));
                        // +Y face
                        if (y == h - 1 || !solid[x, y + 1, z])
                            AddFace(verts, tris, cols, c,
                                o + new Vector3(0, cell, 0),
                                o + new Vector3(0, cell, cell),
                                o + new Vector3(cell, cell, cell),
                                o + new Vector3(cell, cell, 0));
                        // -Y face
                        if (y == 0 || !solid[x, y - 1, z])
                            AddFace(verts, tris, cols, c,
                                o + new Vector3(0, 0, cell),
                                o + new Vector3(0, 0, 0),
                                o + new Vector3(cell, 0, 0),
                                o + new Vector3(cell, 0, cell));
                        // +Z face
                        if (z == depth - 1 || !solid[x, y, z + 1])
                            AddFace(verts, tris, cols, c,
                                o + new Vector3(0, 0, cell),
                                o + new Vector3(cell, 0, cell),
                                o + new Vector3(cell, cell, cell),
                                o + new Vector3(0, cell, cell));
                        // -Z face
                        if (z == 0 || !solid[x, y, z - 1])
                            AddFace(verts, tris, cols, c,
                                o + new Vector3(cell, 0, 0),
                                o + new Vector3(0, 0, 0),
                                o + new Vector3(0, cell, 0),
                                o + new Vector3(cell, cell, 0));
                    }
                }
            }

            var mesh = new Mesh { name = $"voxel:{sprite.name}" };
            if (verts.Count > 65535)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            // Hint to Unity that we don't write the mesh again; lets it free
            // CPU-side copy after upload.
            mesh.UploadMeshData(true);
            return mesh;
        }

        static void AddFace(List<Vector3> verts, List<int> tris, List<Color32> cols, Color32 c,
            Vector3 a, Vector3 b, Vector3 d, Vector3 e)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(d); verts.Add(e);
            cols.Add(c);  cols.Add(c);  cols.Add(c);  cols.Add(c);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        static Mesh CreateEmpty()
        {
            return new Mesh { name = "voxel:empty" };
        }
    }
}
