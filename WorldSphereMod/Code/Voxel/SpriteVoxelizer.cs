using System.Collections.Concurrent;
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
        /// <summary>
        /// Fallback depth when callers pass an explicit positive value and
        /// <see cref="ResolveDepth"/> cannot run. Prefer <c>depth = -1</c> so
        /// <see cref="WorldSphereMod.SavedSettings.VoxelSpriteDepth"/> applies.
        /// </summary>
        public const int DefaultDepth = 8;
        static readonly HashSet<string> _unreadableSpriteWarnings = new HashSet<string>();
        static int _buildPerTexelDiagCount;
        static readonly object _buildPerTexelDiagLock = new object();
        static int _buildPerTexelNoiseDiagCount;
        static readonly object _buildPerTexelNoiseDiagLock = new object();
        static int _buildGreedyDiagCount;
        static readonly object _buildGreedyDiagLock = new object();
        static bool _luminanceDepthStubLogged;

        // Per-texture pixel cache. Sprite atlases share one underlying Texture2D across many
        // sprites; without this, each sprite voxelization re-paid the cost of decoding the
        // entire atlas via GetPixels32(). Keyed by Texture.GetInstanceID() so atlas swap-outs
        // and texture deletions invalidate naturally via ClearPixelCache() at world unload.
        static readonly ConcurrentDictionary<int, Color32[]> _texPixelCache = new ConcurrentDictionary<int, Color32[]>();

        internal static Color32[] GetPixelsCached(Texture2D tex)
        {
            int key = tex.GetInstanceID();
            if (_texPixelCache.TryGetValue(key, out var px))
            {
                return px;
            }

            px = tex.GetPixels32();
            var cached = _texPixelCache.GetOrAdd(key, px);
            return cached;
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
        /// This method is used from background cache-build workers and avoids shared mutable
        /// state during mesh generation.
        /// </summary>
        public static Mesh Build(Sprite sprite, int depth = -1)
        {
            return Build(sprite, out _, depth);
        }

        /// <summary>
        /// Phase 10 proxy-tier mesh entry point (half-res downsample, then voxelize at depth=1).
        /// Deferred: returns null; <see cref="LOD.LodTier.Proxy"/> emit still uses
        /// <see cref="VoxelMeshCache.Get"/> until this ships. See
        /// <c>docs/journeys/scratch/phase10-proxy-tier-status.md</c>.
        /// </summary>
        public static Mesh BuildProxy(Sprite sprite)
        {
            if (sprite == null) return null;
            return null;
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
                    Color32 c = (Core.savedSettings != null && Core.savedSettings.VoxelColorTonemap)
                        ? ColorTonemap.Tonemap(tex[row + x])
                        : tex[row + x];
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
            // WHY: capture full CPU arrays NOW, before UploadMeshData(true) frees the
            // readable copy, so the disk-cache save never reads the non-readable mesh
            // (which floods Player.log with "isReadable is false" every frame).
            snapshot.diskVertices = verts.ToArray();
            snapshot.diskTriangles = tris.ToArray();
            snapshot.diskColors = cols.ToArray();
            snapshot.diskNormals = mesh.normals;
            bool logBuild = false;
            int diagIndex = 0;
            lock (_buildGreedyDiagLock)
            {
                if (_buildGreedyDiagCount < 5)
                {
                    _buildGreedyDiagCount++;
                    diagIndex = _buildGreedyDiagCount;
                    logBuild = true;
                }
            }
            if (logBuild)
            {
                Debug.Log($"[WSM3D][DIAG] BuildGreedy #{diagIndex}: sprite=\"{sprite.name}\" w={w} h={h} depth={depth} verts={mesh.vertexCount} tris={tris.Count / 3} bounds={mesh.bounds}");
            }
            // Hint to Unity that we don't write the mesh again; lets it free
            // CPU-side copy after upload.
            mesh.UploadMeshData(true);
            finalSw.Stop();
            return ReturnProfiled(mesh);
        }

        internal static int ResolveDepth(int depth)
        {
            if (depth > 0) return depth;
            int configuredDepth = Core.savedSettings != null ? Core.savedSettings.VoxelSpriteDepth : 0;
            return configuredDepth > 0 ? configuredDepth : DefaultDepth;
        }

        /// <summary>
        /// When <see cref="WorldSphereMod.SavedSettings.VoxelLuminanceDepth"/> is on, log once that
        /// hybrid DT+luminance depth is not wired yet, then callers fall through to the existing path.
        /// See <c>docs/journeys/scratch/luminance-depth-spec.md</c>.
        /// </summary>
        static void LogLuminanceDepthStubOnceIfEnabled()
        {
            if (Core.savedSettings == null || !Core.savedSettings.VoxelLuminanceDepth)
            {
                return;
            }

            if (_luminanceDepthStubLogged)
            {
                return;
            }

            _luminanceDepthStubLogged = true;
            Debug.Log("[WSM3D][Voxel] VoxelLuminanceDepth enabled; hybrid DT+luminance depth not wired yet — using existing depth.");
        }

        /// <summary>
        /// Phase 1: balloon inflation builds a silhouette-expanded solid from a 2D
        /// distance transform. Solid pixels near the edge stay thin in depth, while
        /// interior pixels become thicker, creating a rounded inflated volume instead
        /// of a flat per-texel column.
        /// </summary>
        public static Mesh BuildBalloon(Sprite sprite, int depth, out int[] vertexToTexel)
        {
            LogLuminanceDepthStubOnceIfEnabled();
            depth = ResolveDepth(depth);
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

            bool[,] solid2d = new bool[w, h];
            Color32[,] color2d = new Color32[w, h];
            for (int y = 0; y < h; y++)
            {
                int row = (y0 + y) * texW + x0;
                for (int x = 0; x < w; x++)
                {
                    Color32 c = (Core.savedSettings != null && Core.savedSettings.VoxelColorTonemap)
                        ? ColorTonemap.Tonemap(tex[row + x])
                        : tex[row + x];
                    if (c.a > 16)
                    {
                        solid2d[x, y] = true;
                        color2d[x, y] = c;
                    }
                }
            }

            int maxDist;
            int[,] distToAir = ComputeManhattanDistanceToAir(solid2d, out maxDist);

            bool[,,] solid = new bool[w, h, depth];
            Color32[,,] color = new Color32[w, h, depth];
            int safeDepth = Mathf.Max(1, depth);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!solid2d[x, y]) continue;

                    // FIXED dot-cloud failure mode: voxel at (x,y,z) opaque IFF
                    // abs(z - zCenter) <= distToAir[x,y]. Result: silhouette (d=1)
                    // is 3 voxels deep, body (d=N) is 2N+1 voxels deep — SOLID FILL,
                    // interior voxels exist so outer shell emits a closed surface.
                    // Force minimum d=2 for body pixels so silhouette isn't paper-thin.
                    int d = Mathf.Max(2, distToAir[x, y]);
                    int zCenter = safeDepth / 2;
                    int zStart = Mathf.Max(0, zCenter - d);
                    int zEnd = Mathf.Min(depth, zCenter + d + 1);

                    for (int z = zStart; z < zEnd; z++)
                    {
                        solid[x, y, z] = true;
                        color[x, y, z] = color2d[x, y];
                    }
                }
            }

            Vector2 pivot = sprite.pivot;
            float ppu = Mathf.Max(1f, sprite.pixelsPerUnit);
            Vector3 origin = new Vector3(-pivot.x / ppu, -pivot.y / ppu, -(depth * 0.5f) / ppu);
            float cell = 1f / ppu;

            var verts = new List<Vector3>();
            var cols = new List<Color32>();
            var tris = new List<int>();

            GreedyMesh(solid, color, w, h, depth, origin, cell, verts, cols, tris);

            var mesh = new Mesh { name = $"voxel-balloon:{sprite.name}" };
            if (verts.Count > 65535)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            // Balloon mode favors debug visibility over Rig cache mapping.
            vertexToTexel = System.Array.Empty<int>();
            return mesh;
        }

        /// <summary>
        /// Organic blob inflation keeps the sprite silhouette but varies the extrusion depth
        /// per column with deterministic Perlin noise so rock-like assets do not read as flat
        /// 2.5D slabs from the side.
        /// </summary>
        public static Mesh BuildOrganicBlob(Sprite sprite, int depth, out int[] vertexToTexel)
        {
            LogLuminanceDepthStubOnceIfEnabled();
            depth = ResolveDepth(depth);
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
            float ppu = Mathf.Max(1f, sprite.pixelsPerUnit);
            const float minDepthScale = 0.15f;
            const float maxDepthScale = 1.00f;

            for (int y = 0; y < h; y++)
            {
                int row = (y0 + y) * texW + x0;
                for (int x = 0; x < w; x++)
                {
                    Color32 c = (Core.savedSettings != null && Core.savedSettings.VoxelColorTonemap)
                        ? ColorTonemap.Tonemap(tex[row + x])
                        : tex[row + x];
                    if (c.a <= 16)
                    {
                        continue;
                    }

                    float worldX = (x - sprite.pivot.x) / ppu;
                    float worldZ = (y - sprite.pivot.y) / ppu;
                    float noise = Tools.PerlinNoiseCached(worldX * 0.22f + 0.13f, worldZ * 0.22f + 0.73f);
                    float depthScale = Mathf.Lerp(minDepthScale, maxDepthScale, noise);
                    int columnDepth = Mathf.Clamp(Mathf.RoundToInt(depth * depthScale), 1, depth);
                    int zStart = Mathf.Clamp(Mathf.FloorToInt((depth - columnDepth) * 0.5f), 0, depth - columnDepth);
                    int zEnd = zStart + columnDepth;

                    for (int z = zStart; z < zEnd; z++)
                    {
                        solid[x, y, z] = true;
                        color[x, y, z] = c;
                    }
                }
            }

            Vector2 pivot = sprite.pivot;
            Vector3 origin = new Vector3(-pivot.x / ppu, -pivot.y / ppu, -(depth * 0.5f) / ppu);
            float cell = 1f / ppu;

            var verts = new List<Vector3>();
            var cols = new List<Color32>();
            var tris = new List<int>();

            GreedyMesh(solid, color, w, h, depth, origin, cell, verts, cols, tris);

            var mesh = new Mesh { name = $"voxel-organicblob:{sprite.name}" };
            if (verts.Count > 65535)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            vertexToTexel = System.Array.Empty<int>();
            return mesh;
        }

        /// <summary>
        /// Phase 1: lathe inflation revolves a 2D silhouette around the Y axis into a
        /// W×H×W voxel volume. For each opaque row, we compute a silhouette half-width r(y)
        /// and fill cylinder-space voxels where (x-c)^2+(z-c)^2 ≤ r(y)^2.
        /// Color is sampled from the source sprite via a cylindrical wrap:
        /// column = (x-c)*cos(theta) + c, theta = atan2(z-c, x-c).
        /// </summary>
        public static Mesh BuildLathe(Sprite sprite, int depth, out int[] vertexToTexel)
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

            float cx = w * 0.5f;
            float cz = w * 0.5f;
            int depthFromSprite = w;

            int[] rowRadius = new int[h];
            for (int y = 0; y < h; y++)
            {
                int rowStart = (y0 + y) * texW + x0;
                int rowRadiusSq = 0;
                for (int x = 0; x < w; x++)
                {
                    Color32 c = (Core.savedSettings != null && Core.savedSettings.VoxelColorTonemap)
                        ? ColorTonemap.Tonemap(tex[rowStart + x])
                        : tex[rowStart + x];
                    if (c.a <= 16) continue;

                    float dx = x - cx;
                    int thisRadius = Mathf.CeilToInt(Mathf.Abs(dx));
                    if (thisRadius > rowRadiusSq) rowRadiusSq = thisRadius;
                }
                rowRadius[y] = rowRadiusSq;
            }

            bool[,,] solid = new bool[w, h, depthFromSprite];
            Color32[,,] color = new Color32[w, h, depthFromSprite];

            int[] sampleColumn = new int[w * depthFromSprite];
            for (int z = 0; z < depthFromSprite; z++)
            {
                float dz = z - cz;
                for (int x = 0; x < w; x++)
                {
                    float dx = x - cx;
                    int idx = z * w + x;
                    float theta = Mathf.Atan2(dz, dx);
                    float sx = (dx * Mathf.Cos(theta)) + cx;
                    int sampleX = Mathf.Clamp(Mathf.RoundToInt(sx), 0, w - 1);
                    sampleColumn[idx] = sampleX;
                }
            }

            for (int y = 0; y < h; y++)
            {
                int rY = rowRadius[y];
                if (rY <= 0) continue;

                int rowStart = (y0 + y) * texW + x0;
                float rSq = rY * (float)rY;
                for (int z = 0; z < depthFromSprite; z++)
                {
                    int rowz = z * w;
                    float dz = z - cz;
                    for (int x = 0; x < w; x++)
                    {
                        float dx = x - cx;
                        if (dx * dx + dz * dz > rSq) continue;

                        int sampleX = sampleColumn[rowz + x];
                        Color32 c = (Core.savedSettings != null && Core.savedSettings.VoxelColorTonemap)
                            ? ColorTonemap.Tonemap(tex[rowStart + sampleX])
                            : tex[rowStart + sampleX];
                        solid[x, y, z] = true;
                        color[x, y, z] = c;
                    }
                }
            }

            Vector2 pivot = sprite.pivot;
            float ppu = Mathf.Max(1f, sprite.pixelsPerUnit);
            Vector3 origin = new Vector3(-pivot.x / ppu, -pivot.y / ppu, -(depthFromSprite * 0.5f) / ppu);
            float cell = 1f / ppu;

            var verts = new List<Vector3>();
            var cols = new List<Color32>();
            var tris = new List<int>();

            GreedyMesh(solid, color, w, h, depthFromSprite, origin, cell, verts, cols, tris);

            var mesh = new Mesh { name = $"voxel-lathe:{sprite.name}" };
            if (verts.Count > 65535)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            vertexToTexel = System.Array.Empty<int>();
            return mesh;
        }

        static int[,] ComputeManhattanDistanceToAir(bool[,] solid, out int maxDist)
        {
            int w = solid.GetLength(0);
            int h = solid.GetLength(1);
            int[,] dist = new int[w, h];
            var qx = new Queue<int>();
            var qy = new Queue<int>();
            maxDist = 0;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (solid[x, y])
                    {
                        dist[x, y] = int.MaxValue / 4;
                    }
                    else
                    {
                        dist[x, y] = 0;
                        qx.Enqueue(x);
                        qy.Enqueue(y);
                    }
                }
            }

            bool hasAir = qx.Count > 0;
            if (!hasAir)
            {
                for (int y = 0; y < h; y++)
                {
                    int x = 0;
                    if (solid[x, y])
                    {
                        dist[x, y] = 0;
                        qx.Enqueue(x);
                        qy.Enqueue(y);
                    }

                    x = w - 1;
                    if (solid[x, y])
                    {
                        dist[x, y] = 0;
                        qx.Enqueue(x);
                        qy.Enqueue(y);
                    }
                }

                for (int x = 0; x < w; x++)
                {
                    int y = 0;
                    if (solid[x, y])
                    {
                        dist[x, y] = 0;
                        qx.Enqueue(x);
                        qy.Enqueue(y);
                    }

                    y = h - 1;
                    if (solid[x, y])
                    {
                        dist[x, y] = 0;
                        qx.Enqueue(x);
                        qy.Enqueue(y);
                    }
                }
            }

            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };
            while (qx.Count > 0)
            {
                int cx = qx.Dequeue();
                int cy = qy.Dequeue();
                int nextDist = dist[cx, cy] + 1;

                for (int i = 0; i < 4; i++)
                {
                    int nx = cx + dx[i];
                    int ny = cy + dy[i];
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    if (solid[nx, ny] && dist[nx, ny] > nextDist)
                    {
                        dist[nx, ny] = nextDist;
                        if (nextDist > maxDist) maxDist = nextDist;
                        qx.Enqueue(nx);
                        qy.Enqueue(ny);
                    }
                }
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (solid[x, y] && dist[x, y] > maxDist)
                    {
                        maxDist = dist[x, y];
                    }
                }
            }

            if (maxDist == 0)
            {
                maxDist = 1;
            }

            return dist;
        }

        /// <summary>
        /// Phase 6 Step 4: per-texel voxel build with no greedy merging. Emits up to six
        /// unit-cube faces per opaque texel (only the faces that border a transparent or
        /// out-of-bounds neighbour, matching <see cref="Build"/>'s face-culling). For every
        /// emitted face all four vertices share the same source texel index
        /// <c>x + y * spriteW</c>, written into <paramref name="vertexToTexel"/>. RigCache
        /// uses that mapping to project <see cref="Rig.HumanoidRig.SegmentVoxels"/> output
        /// from pixel space onto per-vertex bone indices. The depth axis mirrors X across the
        /// back half so the rear silhouette reads as a real back side instead of a straight
        /// extruded slab.
        /// </summary>
        public static Mesh BuildPerTexel(Sprite sprite, int depth, out int[] vertexToTexel)
        {
            LogLuminanceDepthStubOnceIfEnabled();
            depth = ResolveDepth(depth);
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
            int backStart = depth > 1 ? depth / 2 : 0;
            // Apply Perlin depth modulation to ALL voxelized sprites so the skinned
            // actor path (which only ever calls BuildPerTexel) stops producing flat
            // 2.5D extrusions. Same recipe as BuildOrganicBlob but applied here so
            // ShapeHint routing no longer matters for the skinned path. Disable via
            // VoxelColorTonemap-style toggle later if needed.
            const float kMinDepthScale = 0.15f;
            const float kMaxDepthScale = 1.00f;
            float ppu = Mathf.Max(1f, sprite.pixelsPerUnit);
            int[] depthScaleHistogram = null;
            bool logNoiseHistogram = false;
            lock (_buildPerTexelNoiseDiagLock)
            {
                if (_buildPerTexelNoiseDiagCount < 5)
                {
                    _buildPerTexelNoiseDiagCount++;
                    logNoiseHistogram = true;
                    depthScaleHistogram = new int[8];
                }
            }
            for (int y = 0; y < h; y++)
            {
                int row = (y0 + y) * texW + x0;
                for (int x = 0; x < w; x++)
                {
                    Color32 c = (Core.savedSettings != null && Core.savedSettings.VoxelColorTonemap)
                        ? ColorTonemap.Tonemap(tex[row + x])
                        : tex[row + x];
                    if (c.a > 16)
                    {
                        float worldX = (x - sprite.pivot.x) / ppu;
                        float worldZ = (y - sprite.pivot.y) / ppu;
                        float noise = Tools.PerlinNoiseCached(worldX * 0.22f + 0.13f, worldZ * 0.22f + 0.73f);
                        // Luminance: brighter / more saturated pixels extrude deeper
                        float lum = (c.r * 0.299f + c.g * 0.587f + c.b * 0.114f) / 255f;
                        float combined = noise * 0.6f + lum * 0.4f;
                        float depthScale = Mathf.Lerp(kMinDepthScale, kMaxDepthScale, combined);
                        int columnDepth = Mathf.Clamp(Mathf.RoundToInt(depth * depthScale), 2, depth);
                        if (logNoiseHistogram)
                        {
                            int bin = Mathf.Clamp(Mathf.FloorToInt(depthScale * depthScaleHistogram.Length), 0, depthScaleHistogram.Length - 1);
                            depthScaleHistogram[bin]++;
                        }
                        int zStart = Mathf.Clamp((depth - columnDepth) / 2, 0, depth - columnDepth);
                        int zEnd = zStart + columnDepth;

                        for (int z = zStart; z < zEnd; z++)
                        {
                            int sampleX = depth <= 1 ? x : (z < backStart ? x : (w - 1 - x));
                            solid[sampleX, y, z] = true;
                            color[sampleX, y, z] = c;
                        }
                    }
                }
            }

            Vector2 pivot = sprite.pivot;
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
            bool shouldLog = false;
            int diagIndex = 0;
            lock (_buildPerTexelDiagLock)
            {
                if (_buildPerTexelDiagCount < 5)
                {
                    _buildPerTexelDiagCount++;
                    diagIndex = _buildPerTexelDiagCount;
                    shouldLog = true;
                }
            }
            if (shouldLog)
            {
                Debug.Log($"[WSM3D][DIAG] BuildPerTexel #{diagIndex}: sprite=\"{sprite.name}\" w={w} h={h} depth={depth} verts={mesh.vertexCount} tris={tris.Count / 3} bounds={mesh.bounds}");
            }
            if (logNoiseHistogram)
            {
                Debug.Log($"[WSM3D][DIAG] BuildPerTexel depthScale histogram #{_buildPerTexelNoiseDiagCount}: sprite=\"{sprite.name}\" depth={depth} bins=[{string.Join(",", depthScaleHistogram)}]");
            }
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
            // Double-sided: emit both windings so the mesh renders regardless of
            // which way the per-direction winding happens to point. Adds 2 extra tris per quad.
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
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
