using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace WorldSphereMod.Voxel
{
    /// <summary>
    /// Converts a 2D pixel-art <see cref="Sprite"/> into a 3D voxel <see cref="Mesh"/>.
    /// Every opaque texel becomes a unit cube in local space; faces hidden by neighbouring
    /// opaque texels are culled. Coplanar same-color faces are then merged via greedy
    /// meshing so the final vertex count is a small fraction of the naive per-texel output.
    /// Per-cube color is baked into a vertex color attribute so the existing palette renders
    /// correctly under the Phase 5 directional light + shadow stack without per-instance
    /// materials.
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
        static readonly HashSet<string> _unreadableSpriteWarnings = new HashSet<string>();

        // Per-texture pixel cache. Sprite atlases share one underlying Texture2D across many
        // sprites; without this, each sprite voxelization re-paid the cost of decoding the
        // entire atlas via GetPixels32(). Keyed by Texture.GetInstanceID() so atlas swap-outs
        // and texture deletions invalidate naturally via ClearPixelCache() at world unload.
        static readonly Dictionary<int, Color32[]> _texPixelCache = new Dictionary<int, Color32[]>(64);

        internal static Color32[] GetPixelsCached(Texture2D tex)
        {
            int key = tex.GetInstanceID();
            if (_texPixelCache.TryGetValue(key, out var px)) return px;
            px = tex.GetPixels32();
            _texPixelCache[key] = px;
            return px;
        }

        /// <summary>Drop the per-texture pixel cache. Wire into world unload so stale atlas
        /// pixel arrays don't pin GC memory across sessions.</summary>
        public static void ClearPixelCache()
        {
            _texPixelCache.Clear();
        }

        /// <summary>
        /// Build a voxel mesh from the given sprite. Pulls pixels via the same atlas-aware
        /// path the terrain uses (<see cref="Tools.PixelsFromSpriteAtlas"/>) when the sprite
        /// rect is &lt;= 8x8, otherwise uses the full sprite rect. Caller is responsible for
        /// caching the result; see <see cref="VoxelMeshCache"/>.
        /// </summary>
        public static Mesh Build(Sprite sprite, int depth = -1)
        {
            return Build(sprite, out _, depth);
        }

        public static Mesh Build(Sprite sprite, out VoxelMeshCache.MeshSnapshot snapshot, int depth = -1)
        {
            snapshot = null;
            depth = ResolveDepth(depth);
            bool profile = Core.savedSettings.ProfilerDump;
            Stopwatch totalSw = new Stopwatch();
            Stopwatch solidSw = new Stopwatch();
            Stopwatch greedySw = new Stopwatch();
            Stopwatch finalSw = new Stopwatch();

            if (profile) totalSw.Start();

            Mesh ReturnProfiled(Mesh mesh)
            {
                if (!profile) return mesh;
                if (totalSw.IsRunning) totalSw.Stop();
                Debug.Log($"[WSM3D][PERF] SpriteVoxelizer.Build total={totalSw.Elapsed.TotalMilliseconds:F3}ms");
                Debug.Log($"[WSM3D][PERF] SpriteVoxelizer.Build solidMask={solidSw.Elapsed.TotalMilliseconds:F3}ms");
                Debug.Log($"[WSM3D][PERF] SpriteVoxelizer.Build greedyMesh={greedySw.Elapsed.TotalMilliseconds:F3}ms");
                Debug.Log($"[WSM3D][PERF] SpriteVoxelizer.Build finalize={finalSw.Elapsed.TotalMilliseconds:F3}ms");
                return mesh;
            }

            if (sprite == null || sprite.texture == null)
            {
                return ReturnProfiled(CreateEmpty());
            }

            Texture2D sourceTexture = sprite.texture;
            Texture2D? fallbackTexture = null;
            RenderTexture? fallbackRt = null;

            // Atlased textures imported without Read/Write enabled throw on GetPixels32.
            // Return an empty mesh so the caller (cache) doesn't crash the render pass.
            try
            {
                if (!sprite.texture.isReadable)
                {
                    fallbackRt = RenderTexture.GetTemporary(sourceTexture.width, sourceTexture.height, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(sourceTexture, fallbackRt);

                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = fallbackRt;
                    try
                    {
                        fallbackTexture = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false);
                        fallbackTexture.ReadPixels(new Rect(0f, 0f, sourceTexture.width, sourceTexture.height), 0, 0, false);
                        fallbackTexture.Apply(false, false);
                    }
                    finally
                    {
                        RenderTexture.active = prev;
                    }

                    if (fallbackTexture == null || !fallbackTexture.isReadable)
                    {
                        throw new System.Exception("Fallback texture conversion did not produce a readable texture.");
                    }

                    sourceTexture = fallbackTexture;
                }
            }
            catch
            {
                if (fallbackTexture != null)
                {
                    Object.Destroy(fallbackTexture);
                }
                if (fallbackRt != null)
                {
                    RenderTexture.ReleaseTemporary(fallbackRt);
                }
                if (_unreadableSpriteWarnings.Add(sprite.name))
                {
                    Debug.LogWarning($"[SpriteVoxelizer] Sprite '{sprite.name}' texture is not readable and fallback failed; returning null.");
                }
                return ReturnProfiled(null);
            }

            // Read the sprite's rectangle out of its atlas. We don't use the
            // 8x8 fast-path here because actor sprites are typically larger;
            // PixelsFromSpriteAtlas is hardcoded to 8x8 in the upstream code.
            Rect r = sprite.textureRect;
            int w = Mathf.Max(1, (int)r.width);
            int h = Mathf.Max(1, (int)r.height);
            int x0 = (int)r.x;
            int y0 = (int)r.y;
            solidSw.Start();
            Color32[] tex = GetPixelsCached(sourceTexture);
            int texW = sourceTexture.width;

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
            solidSw.Stop();

            var verts = new List<Vector3>();
            var cols  = new List<Color32>();
            var tris  = new List<int>();

            greedySw.Start();
            GreedyMesh(solid, color, w, h, depth, origin, cell, verts, cols, tris);
            greedySw.Stop();

            finalSw.Start();
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
            if (fallbackTexture != null)
            {
                Object.Destroy(fallbackTexture);
            }
            if (fallbackRt != null)
            {
                RenderTexture.ReleaseTemporary(fallbackRt);
            }
            snapshot = VoxelMeshCache.CreateSnapshot(sprite, mesh, verts, cols, tris);
            // Hint to Unity that we don't write the mesh again; lets it free
            // CPU-side copy after upload.
            mesh.UploadMeshData(true);
            finalSw.Stop();
            return ReturnProfiled(mesh);
        }

        static int ResolveDepth(int depth)
        {
            if (depth > 0) return depth;
            int configuredDepth = Core.savedSettings != null ? Core.savedSettings.VoxelSpriteDepth : 0;
            return configuredDepth > 0 ? configuredDepth : 3;
        }

        /// <summary>
        /// Phase 6 Step 4: per-texel voxel build with no greedy merging. Emits up to six
        /// unit-cube faces per opaque texel (only the faces that border a transparent or
        /// out-of-bounds neighbour, matching <see cref="Build"/>'s face-culling). For every
        /// emitted face all four vertices share the same source texel index
        /// <c>x + y * spriteW</c>, written into <paramref name="vertexToTexel"/>. RigCache
        /// uses that mapping to project <see cref="Rig.HumanoidRig.SegmentVoxels"/> output
        /// from pixel space onto per-vertex bone indices. The depth axis collapses to the
        /// front-most texel (z=0) because the source is a 2D sprite — every (x,y,z) column of
        /// solid voxels descends from the same texel.
        /// </summary>
        public static Mesh BuildPerTexel(Sprite sprite, int depth, out int[] vertexToTexel)
        {
            if (sprite == null || sprite.texture == null || !sprite.texture.isReadable)
            {
                vertexToTexel = System.Array.Empty<int>();
                return CreateEmpty();
            }

            Rect r = sprite.textureRect;
            int w = Mathf.Max(1, (int)r.width);
            int h = Mathf.Max(1, (int)r.height);
            int x0 = (int)r.x;
            int y0 = (int)r.y;
            Color32[] tex = GetPixelsCached(sprite.texture);
            int texW = sprite.texture.width;

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

            Vector2 pivot = sprite.pivot;
            float ppu = Mathf.Max(1f, sprite.pixelsPerUnit);
            Vector3 origin = new Vector3(-pivot.x / ppu, -pivot.y / ppu, -(depth * 0.5f) / ppu);
            float cell = 1f / ppu;

            var verts = new List<Vector3>();
            var cols  = new List<Color32>();
            var tris  = new List<int>();
            var vToT  = new List<int>();

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int z = 0; z < depth; z++)
                    {
                        if (!solid[x, y, z]) continue;
                        Color32 c = color[x, y, z];
                        int texel = x + y * w;

                        // dir 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z
                        TryEmitTexelFace(0, x, y, z, w, h, depth, solid, c, texel, origin, cell, verts, cols, tris, vToT);
                        TryEmitTexelFace(1, x, y, z, w, h, depth, solid, c, texel, origin, cell, verts, cols, tris, vToT);
                        TryEmitTexelFace(2, x, y, z, w, h, depth, solid, c, texel, origin, cell, verts, cols, tris, vToT);
                        TryEmitTexelFace(3, x, y, z, w, h, depth, solid, c, texel, origin, cell, verts, cols, tris, vToT);
                        TryEmitTexelFace(4, x, y, z, w, h, depth, solid, c, texel, origin, cell, verts, cols, tris, vToT);
                        TryEmitTexelFace(5, x, y, z, w, h, depth, solid, c, texel, origin, cell, verts, cols, tris, vToT);
                    }
                }
            }

            var mesh = new Mesh { name = $"voxel-pt:{sprite.name}" };
            if (verts.Count > 65535)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            // No UploadMeshData(true) here: callers (RigCache) need to keep CPU-side vertex
            // data readable so they can stamp per-vertex bone indices alongside vertexToTexel.

            vertexToTexel = vToT.ToArray();
            return mesh;
        }

        static void TryEmitTexelFace(int dir, int cx, int cy, int cz,
            int w, int h, int d, bool[,,] solid, Color32 c, int texel,
            Vector3 origin, float cell,
            List<Vector3> verts, List<Color32> cols, List<int> tris, List<int> vertexToTexel)
        {
            int nx = cx, ny = cy, nz = cz;
            switch (dir)
            {
                case 0: nx++; break;
                case 1: nx--; break;
                case 2: ny++; break;
                case 3: ny--; break;
                case 4: nz++; break;
                case 5: nz--; break;
            }
            bool neighborSolid =
                nx >= 0 && nx < w &&
                ny >= 0 && ny < h &&
                nz >= 0 && nz < d &&
                solid[nx, ny, nz];
            if (neighborSolid) return;

            int s, u, v;
            switch (dir >> 1)
            {
                case 0: s = cx; u = cy; v = cz; break;
                case 1: s = cy; u = cx; v = cz; break;
                default: s = cz; u = cx; v = cy; break;
            }

            int baseIdx = verts.Count;
            EmitQuad(dir, s, u, v, 1, 1, c, origin, cell, verts, cols, tris);
            int added = verts.Count - baseIdx;
            for (int i = 0; i < added; i++) vertexToTexel.Add(texel);
        }

        // Mikola Lysenko-style binary greedy meshing: 6 face directions, per-slice 2D mask, merge equal-color cells into rectangles.
        static void GreedyMesh(bool[,,] solid, Color32[,,] color, int w, int h, int d,
            Vector3 origin, float cell,
            List<Vector3> verts, List<Color32> cols, List<int> tris)
        {
            // dir encoding: 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z
            for (int dir = 0; dir < 6; dir++)
            {
                int axis = dir >> 1;          // 0=X, 1=Y, 2=Z
                int positive = (dir & 1) ^ 1;  // 1 if +face, 0 if -face

                int sliceCount, uCount, vCount;
                if (axis == 0) { sliceCount = w; uCount = h; vCount = d; }
                else if (axis == 1) { sliceCount = h; uCount = w; vCount = d; }
                else { sliceCount = d; uCount = w; vCount = h; }

                var mask = new Color32[uCount, vCount];
                var present = new bool[uCount, vCount];

                for (int s = 0; s < sliceCount; s++)
                {
                    for (int u = 0; u < uCount; u++)
                    {
                        for (int v = 0; v < vCount; v++)
                        {
                            present[u, v] = false;

                            int cx, cy, cz;
                            if (axis == 0) { cx = s; cy = u; cz = v; }
                            else if (axis == 1) { cx = u; cy = s; cz = v; }
                            else { cx = u; cy = v; cz = s; }

                            if (!solid[cx, cy, cz]) continue;

                            int nx = cx, ny = cy, nz = cz;
                            if (axis == 0) nx += (positive == 1 ? 1 : -1);
                            else if (axis == 1) ny += (positive == 1 ? 1 : -1);
                            else nz += (positive == 1 ? 1 : -1);

                            bool neighborSolid =
                                nx >= 0 && nx < w &&
                                ny >= 0 && ny < h &&
                                nz >= 0 && nz < d &&
                                solid[nx, ny, nz];
                            if (neighborSolid) continue;

                            present[u, v] = true;
                            mask[u, v] = color[cx, cy, cz];
                        }
                    }

                    for (int v = 0; v < vCount; v++)
                    {
                        for (int u = 0; u < uCount; )
                        {
                            if (!present[u, v]) { u++; continue; }

                            Color32 c = mask[u, v];

                            int u1 = u + 1;
                            while (u1 < uCount && present[u1, v] && ColorEq(mask[u1, v], c)) u1++;
                            int width = u1 - u;

                            int v1 = v + 1;
                            while (v1 < vCount)
                            {
                                bool rowOk = true;
                                for (int k = u; k < u1; k++)
                                {
                                    if (!present[k, v1] || !ColorEq(mask[k, v1], c)) { rowOk = false; break; }
                                }
                                if (!rowOk) break;
                                v1++;
                            }
                            int height = v1 - v;

                            for (int vv = v; vv < v1; vv++)
                                for (int uu = u; uu < u1; uu++)
                                    present[uu, vv] = false;

                            EmitQuad(dir, s, u, v, width, height, c, origin, cell, verts, cols, tris);

                            u = u1;
                        }
                    }
                }
            }
        }

        static void EmitQuad(int dir, int s, int u, int v, int uw, int vh, Color32 c,
            Vector3 origin, float cell,
            List<Vector3> verts, List<Color32> cols, List<int> tris)
        {
            // Resolve the slice plane offset (along the face's normal axis) and the
            // four corner positions in world space.
            float fs = (dir == 0 || dir == 2 || dir == 4) ? (s + 1) * cell : s * cell;
            float u0 = u * cell;
            float u1 = (u + uw) * cell;
            float v0 = v * cell;
            float v1 = (v + vh) * cell;

            Vector3 a, b, dd, e;
            switch (dir)
            {
                case 0: // +X: U=Y, V=Z, winding matches original (cell,0,0)->(cell,h,0)->(cell,h,d)->(cell,0,d)
                    a = origin + new Vector3(fs, u0, v0);
                    b = origin + new Vector3(fs, u1, v0);
                    dd= origin + new Vector3(fs, u1, v1);
                    e = origin + new Vector3(fs, u0, v1);
                    break;
                case 1: // -X: U=Y, V=Z, reversed winding: (0,0,d)->(0,h,d)->(0,h,0)->(0,0,0)
                    a = origin + new Vector3(fs, u0, v1);
                    b = origin + new Vector3(fs, u1, v1);
                    dd= origin + new Vector3(fs, u1, v0);
                    e = origin + new Vector3(fs, u0, v0);
                    break;
                case 2: // +Y: U=X, V=Z, original (0,cell,0)->(0,cell,d)->(w,cell,d)->(w,cell,0)
                    a = origin + new Vector3(u0, fs, v0);
                    b = origin + new Vector3(u0, fs, v1);
                    dd= origin + new Vector3(u1, fs, v1);
                    e = origin + new Vector3(u1, fs, v0);
                    break;
                case 3: // -Y: U=X, V=Z, original (0,0,d)->(0,0,0)->(w,0,0)->(w,0,d)
                    a = origin + new Vector3(u0, fs, v1);
                    b = origin + new Vector3(u0, fs, v0);
                    dd= origin + new Vector3(u1, fs, v0);
                    e = origin + new Vector3(u1, fs, v1);
                    break;
                case 4: // +Z: U=X, V=Y, original (0,0,cell)->(w,0,cell)->(w,h,cell)->(0,h,cell)
                    a = origin + new Vector3(u0, v0, fs);
                    b = origin + new Vector3(u1, v0, fs);
                    dd= origin + new Vector3(u1, v1, fs);
                    e = origin + new Vector3(u0, v1, fs);
                    break;
                default: // 5 -Z: U=X, V=Y, original (w,0,0)->(0,0,0)->(0,h,0)->(w,h,0)
                    a = origin + new Vector3(u1, v0, fs);
                    b = origin + new Vector3(u0, v0, fs);
                    dd= origin + new Vector3(u0, v1, fs);
                    e = origin + new Vector3(u1, v1, fs);
                    break;
            }

            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(dd); verts.Add(e);
            cols.Add(c);  cols.Add(c);  cols.Add(c);   cols.Add(c);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        static bool ColorEq(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        static Mesh CreateEmpty()
        {
            return new Mesh { name = "voxel:empty" };
        }
    }
}
