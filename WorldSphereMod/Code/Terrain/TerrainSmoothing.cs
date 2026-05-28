using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WorldSphereMod.Terrain
{
    /// <summary>
    /// Smooth interpolated terrain mesh overlay for height transitions.
    /// Instead of flat billboard quads between height levels, this generates
    /// a continuous height-interpolated mesh where each tile vertex height is
    /// bilinearly blended from surrounding tile centers — like OptiFine smooth
    /// terrain. The underlying tile data stays blocky; only the visual overlay
    /// is smooth.
    /// </summary>
    public sealed class MountainSlopeSurface : MonoBehaviour
    {
        public static MountainSlopeSurface? Instance { get; private set; }

        static Material? _material;
        static bool _materialAttempted;

        MeshFilter? _filter;
        MeshRenderer? _renderer;
        Mesh? _mesh;
        bool _dirty = true;

        // Subdivision level per tile edge for the smooth mesh (4 = 16 sub-quads per tile).
        const int SubDiv = 4;

        struct CliffQuad
        {
            public int X;
            public int Y;
            public bool IsVertical;
            public float HeightA;
            public float HeightB;
            public Color32 ColorA;
            public Color32 ColorB;
        }

        public static MountainSlopeSurface? Create(Transform parent)
        {
            if (Instance != null)
            {
                Destroy();
            }

            if (!EnsureMaterial())
            {
                return null;
            }

            GameObject go = new GameObject("WorldSphere Mountain Slope Smoothing");
            go.transform.SetParent(parent, worldPositionStays: false);

            MeshFilter filter = go.AddComponent<MeshFilter>();
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            MountainSlopeSurface surface = go.AddComponent<MountainSlopeSurface>();
            surface._filter = filter;
            surface._renderer = renderer;
            surface._mesh = new Mesh { name = "WorldSphere.MountainSlopeSmoothing" };
            surface._mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            filter.sharedMesh = surface._mesh;
            surface._dirty = true;
            Instance = surface;
            surface.RebuildMesh();
            return surface;
        }

        public static void Destroy()
        {
            if (Instance == null)
            {
                return;
            }

            GameObject go = Instance.gameObject;
            if (Instance._mesh != null)
            {
                UnityEngine.Object.Destroy(Instance._mesh);
            }

            if (Instance._renderer != null)
            {
                UnityEngine.Object.Destroy(Instance._renderer.sharedMaterial);
            }

            Instance._mesh = null;
            Instance._filter = null;
            Instance._renderer = null;
            Instance._dirty = false;
            Instance = null;

            if (go != null)
            {
                UnityEngine.Object.Destroy(go);
            }

            if (_material != null)
            {
                UnityEngine.Object.Destroy(_material);
            }

            _material = null;
            _materialAttempted = false;
        }

        public static void RequestRebuild()
        {
            if (Instance != null)
            {
                Instance._dirty = true;
            }
        }

        public static void EnsureActive()
        {
            if (!Core.IsWorld3D || !Core.savedSettings.MountainSlopeSmoothing)
            {
                Destroy();
                return;
            }

            if (Instance != null)
            {
                return;
            }

            Transform? capsule = Core.Sphere.CenterCapsule;
            if (capsule == null || capsule.parent == null)
            {
                return;
            }

            Create(capsule.parent);
        }

        void LateUpdate()
        {
            if (!Core.IsWorld3D || !Core.savedSettings.MountainSlopeSmoothing)
            {
                return;
            }

            if (_dirty)
            {
                RebuildMesh();
            }
        }

        /// <summary>
        /// Returns the tile height at integer tile coords, clamped/wrapped to map bounds.
        /// </summary>
        float SampleTileHeight(int x, int y, int width, int height, bool wrapped)
        {
            if (wrapped)
            {
                x = ((x % width) + width) % width;
            }
            else
            {
                x = Mathf.Clamp(x, 0, width - 1);
            }
            y = Mathf.Clamp(y, 0, height - 1);

            WorldTile tile = World.world.GetTileSimple(x, y);
            if (tile == null) return 0f;
            return tile.TileHeight();
        }

        /// <summary>
        /// Returns the biome color at integer tile coords, clamped/wrapped to map bounds.
        /// </summary>
        Color32 SampleTileColor(int x, int y, int width, int height, bool wrapped)
        {
            if (wrapped)
            {
                x = ((x % width) + width) % width;
            }
            else
            {
                x = Mathf.Clamp(x, 0, width - 1);
            }
            y = Mathf.Clamp(y, 0, height - 1);

            WorldTile tile = World.world.GetTileSimple(x, y);
            if (tile == null) return new Color32(128, 128, 128, 255);
            return Core.Sphere.GetColor(tile.data.tile_id);
        }

        /// <summary>
        /// Computes an interpolated corner height at the junction of four tiles.
        /// The corner at (cx, cy) in tile-corner space sits at the meeting point of
        /// tiles (cx-1,cy-1), (cx,cy-1), (cx-1,cy), (cx,cy). We average their heights.
        /// </summary>
        float CornerHeight(int cx, int cy, int width, int height, bool wrapped)
        {
            float h00 = SampleTileHeight(cx - 1, cy - 1, width, height, wrapped);
            float h10 = SampleTileHeight(cx,     cy - 1, width, height, wrapped);
            float h01 = SampleTileHeight(cx - 1, cy,     width, height, wrapped);
            float h11 = SampleTileHeight(cx,     cy,     width, height, wrapped);
            return (h00 + h10 + h01 + h11) * 0.25f;
        }

        /// <summary>
        /// Computes an interpolated corner color at the junction of four tiles.
        /// Averages the biome colors of the four adjacent tiles.
        /// </summary>
        Color32 CornerColor(int cx, int cy, int width, int height, bool wrapped)
        {
            Color32 c00 = SampleTileColor(cx - 1, cy - 1, width, height, wrapped);
            Color32 c10 = SampleTileColor(cx,     cy - 1, width, height, wrapped);
            Color32 c01 = SampleTileColor(cx - 1, cy,     width, height, wrapped);
            Color32 c11 = SampleTileColor(cx,     cy,     width, height, wrapped);
            return new Color32(
                (byte)((c00.r + c10.r + c01.r + c11.r) / 4),
                (byte)((c00.g + c10.g + c01.g + c11.g) / 4),
                (byte)((c00.b + c10.b + c01.b + c11.b) / 4),
                255);
        }

        void RebuildMesh()
        {
            if (_mesh == null)
            {
                return;
            }

            _dirty = false;
            _mesh.Clear();

            if (World.world == null || World.world.tiles_list == null)
            {
                return;
            }

            int width = MapBox.width;
            int height = MapBox.height;
            if (width <= 1 || height <= 1)
            {
                return;
            }

            // Use DetectCliffQuads to identify tiles involved in height transitions.
            // We then generate smooth interpolated geometry for those tiles and their
            // immediate neighbors instead of flat billboard quads.
            List<CliffQuad> quads = DetectCliffQuads(width, height);
            if (quads.Count == 0)
            {
                return;
            }

            bool wrapped = Core.Sphere.IsWrapped;

            // Collect the set of tiles that need smooth geometry: every tile touching
            // a cliff edge plus neighbors within SmoothRadius for a wide transition
            // zone that hides the blocky terrain underneath.
            const int SmoothRadius = 3;
            HashSet<long> smoothTileSet = new HashSet<long>();
            for (int i = 0; i < quads.Count; i++)
            {
                CliffQuad q = quads[i];
                for (int dy = -SmoothRadius; dy <= SmoothRadius; dy++)
                {
                    for (int dx = -SmoothRadius; dx <= SmoothRadius; dx++)
                    {
                        int tx = q.X + dx;
                        int ty = q.Y + dy;
                        if (ty < 0 || ty >= height) continue;
                        if (wrapped)
                        {
                            tx = ((tx % width) + width) % width;
                        }
                        else
                        {
                            if (tx < 0 || tx >= width) continue;
                        }
                        smoothTileSet.Add((long)ty * width + tx);
                    }
                }
            }

            int tileCount = smoothTileSet.Count;
            int vertsPerTile = (SubDiv + 1) * (SubDiv + 1);
            int trisPerTile = SubDiv * SubDiv * 6;

            List<Vector3> vertices = new List<Vector3>(tileCount * vertsPerTile);
            List<Color32> colors = new List<Color32>(tileCount * vertsPerTile);
            List<int> triangles = new List<int>(tileCount * trisPerTile);

            // Height offset so the smooth overlay sits clearly above the flat blocky
            // terrain, fully hiding cube edges instead of clipping through them.
            const float HeightBias = 0.15f;

            foreach (long key in smoothTileSet)
            {
                int ty = (int)(key / width);
                int tx = (int)(key % width);

                // Corner heights for this tile's 4 corners (in tile-corner space):
                //   (tx, ty) ---- (tx+1, ty)
                //      |              |
                //   (tx, ty+1) -- (tx+1, ty+1)
                float hBL = CornerHeight(tx,     ty,     width, height, wrapped);
                float hBR = CornerHeight(tx + 1, ty,     width, height, wrapped);
                float hTL = CornerHeight(tx,     ty + 1, width, height, wrapped);
                float hTR = CornerHeight(tx + 1, ty + 1, width, height, wrapped);

                Color32 cBL = CornerColor(tx,     ty,     width, height, wrapped);
                Color32 cBR = CornerColor(tx + 1, ty,     width, height, wrapped);
                Color32 cTL = CornerColor(tx,     ty + 1, width, height, wrapped);
                Color32 cTR = CornerColor(tx + 1, ty + 1, width, height, wrapped);

                int baseVertex = vertices.Count;

                // Generate a (SubDiv+1) x (SubDiv+1) grid of vertices across this tile,
                // with bilinearly interpolated heights from the 4 corner values.
                for (int sy = 0; sy <= SubDiv; sy++)
                {
                    float fy = (float)sy / SubDiv;
                    for (int sx = 0; sx <= SubDiv; sx++)
                    {
                        float fx = (float)sx / SubDiv;

                        // Bilinear interpolation of height
                        float h = Mathf.Lerp(
                            Mathf.Lerp(hBL, hBR, fx),
                            Mathf.Lerp(hTL, hTR, fx),
                            fy) + HeightBias;

                        // Bilinear interpolation of color
                        byte r = (byte)Mathf.RoundToInt(Mathf.Lerp(
                            Mathf.Lerp(cBL.r, cBR.r, fx),
                            Mathf.Lerp(cTL.r, cTR.r, fx), fy));
                        byte g = (byte)Mathf.RoundToInt(Mathf.Lerp(
                            Mathf.Lerp(cBL.g, cBR.g, fx),
                            Mathf.Lerp(cTL.g, cTR.g, fx), fy));
                        byte b = (byte)Mathf.RoundToInt(Mathf.Lerp(
                            Mathf.Lerp(cBL.b, cBR.b, fx),
                            Mathf.Lerp(cTL.b, cTR.b, fx), fy));

                        // Project onto sphere via Core.Sphere.SpherePos(
                        float worldX = tx + fx;
                        float worldY = ty + fy;
                        vertices.Add(Core.Sphere.SpherePos(worldX, worldY, h));
                        colors.Add(new Color32(r, g, b, 255));
                    }
                }

                // Emit triangles for the SubDiv x SubDiv sub-grid
                int stride = SubDiv + 1;
                for (int sy = 0; sy < SubDiv; sy++)
                {
                    for (int sx = 0; sx < SubDiv; sx++)
                    {
                        int i00 = baseVertex + sy * stride + sx;
                        int i10 = i00 + 1;
                        int i01 = i00 + stride;
                        int i11 = i01 + 1;

                        triangles.Add(i00);
                        triangles.Add(i01);
                        triangles.Add(i10);

                        triangles.Add(i10);
                        triangles.Add(i01);
                        triangles.Add(i11);
                    }
                }
            }

            Debug.Log($"[WSM3D] MountainSlopeSmoothing rebuilt {quads.Count} cliff quads -> {smoothTileSet.Count} smooth tiles, {vertices.Count} verts.");

            _mesh.SetVertices(vertices);
            _mesh.SetTriangles(triangles, 0);
            _mesh.SetColors(colors);

            // Analytic normals: compute per-vertex normals from the grid structure
            // using finite differences on neighboring vertices within each tile's
            // sub-grid, then average shared-position normals for seamless shading.
            Vector3[] normals = ComputeAnalyticNormals(vertices, tileCount, vertsPerTile);
            _mesh.SetNormals(new List<Vector3>(normals));
            _mesh.RecalculateBounds();
        }

        /// <summary>
        /// Computes per-vertex normals using finite differences on the sub-grid
        /// structure. For each vertex in a tile's (SubDiv+1)x(SubDiv+1) grid, the
        /// tangent and bitangent are derived from horizontal and vertical neighbors,
        /// and the normal is their cross product. Edge vertices use one-sided
        /// differences. The result is smooth across each tile; cross-tile seam
        /// smoothing happens via RecalculateNormals on shared-position vertices
        /// as a fallback when the grid doesn't provide neighbors.
        /// </summary>
        static Vector3[] ComputeAnalyticNormals(List<Vector3> vertices, int tileCount, int vertsPerTile)
        {
            Vector3[] normals = new Vector3[vertices.Count];
            int stride = SubDiv + 1;

            for (int t = 0; t < tileCount; t++)
            {
                int baseIdx = t * vertsPerTile;

                for (int sy = 0; sy <= SubDiv; sy++)
                {
                    for (int sx = 0; sx <= SubDiv; sx++)
                    {
                        int idx = baseIdx + sy * stride + sx;

                        // Horizontal tangent via finite difference
                        Vector3 tangentX;
                        if (sx > 0 && sx < SubDiv)
                        {
                            tangentX = vertices[idx + 1] - vertices[idx - 1];
                        }
                        else if (sx < SubDiv)
                        {
                            tangentX = vertices[idx + 1] - vertices[idx];
                        }
                        else
                        {
                            tangentX = vertices[idx] - vertices[idx - 1];
                        }

                        // Vertical tangent via finite difference
                        Vector3 tangentY;
                        if (sy > 0 && sy < SubDiv)
                        {
                            tangentY = vertices[idx + stride] - vertices[idx - stride];
                        }
                        else if (sy < SubDiv)
                        {
                            tangentY = vertices[idx + stride] - vertices[idx];
                        }
                        else
                        {
                            tangentY = vertices[idx] - vertices[idx - stride];
                        }

                        Vector3 n = Vector3.Cross(tangentX, tangentY).normalized;
                        if (n.sqrMagnitude < 0.001f)
                        {
                            n = vertices[idx].normalized;
                        }
                        normals[idx] = n;
                    }
                }
            }

            return normals;
        }

        List<CliffQuad> DetectCliffQuads(int width, int height)
        {
            List<CliffQuad> quads = new List<CliffQuad>(Mathf.Max(width, 0) * Mathf.Max(height, 0));
            bool wrapped = Core.Sphere.IsWrapped;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    WorldTile tile = ResolveTile(x, y);
                    if (tile == null)
                    {
                        continue;
                    }

                    float tileHeight = tile.TileHeight();
                    Color32 tileColor = Core.Sphere.GetColor(tile.data.tile_id);

                    int rightX = x + 1;
                    if (wrapped || rightX < width)
                    {
                        int sampleX = wrapped ? rightX % width : rightX;
                        WorldTile rightTile = ResolveTile(sampleX, y);
                        if (rightTile != null)
                        {
                            float rightHeight = rightTile.TileHeight();
                            if (Mathf.Abs(tileHeight - rightHeight) > 0.1f)
                            {
                                Color32 rightColor = Core.Sphere.GetColor(rightTile.data.tile_id);
                                quads.Add(new CliffQuad
                                {
                                    X = x,
                                    Y = y,
                                    IsVertical = false,
                                    HeightA = tileHeight,
                                    HeightB = rightHeight,
                                    ColorA = tileColor,
                                    ColorB = rightColor,
                                });
                            }
                        }
                    }

                    int upY = y + 1;
                    if (upY < height)
                    {
                        WorldTile upTile = ResolveTile(x, upY);
                        if (upTile != null)
                        {
                            float upHeight = upTile.TileHeight();
                            if (Mathf.Abs(tileHeight - upHeight) > 0.1f)
                            {
                                Color32 upColor = Core.Sphere.GetColor(upTile.data.tile_id);
                                quads.Add(new CliffQuad
                                {
                                    X = x,
                                    Y = y,
                                    IsVertical = true,
                                    HeightA = tileHeight,
                                    HeightB = upHeight,
                                    ColorA = tileColor,
                                    ColorB = upColor,
                                });
                            }
                        }
                    }
                }
            }

            return quads;
        }

        WorldTile ResolveTile(int x, int y)
        {
            if (World.world == null)
            {
                return null;
            }

            int width = MapBox.width;
            int height = MapBox.height;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            if (Core.Sphere.IsWrapped)
            {
                x = (int)Tools.MathStuff.Wrap(x, 0, width);
            }
            else
            {
                x = Mathf.Clamp(x, 0, width - 1);
            }

            y = Mathf.Clamp(y, 0, height - 1);
            return World.world.GetTileSimple(x, y);
        }

        static Material? GetUnderlyingTerrainMaterial()
        {
            Material terrainMaterial = Core.Sphere.CompoundSphereMaterial;
            if (terrainMaterial != null)
            {
                return terrainMaterial;
            }

            Transform? capsule = Core.Sphere.CenterCapsule;
            if (capsule != null)
            {
                MeshRenderer? parentRenderer = capsule.parent?.GetComponentInChildren<MeshRenderer>();
                if (parentRenderer != null && parentRenderer.sharedMaterial != null)
                {
                    return parentRenderer.sharedMaterial;
                }
            }

            return null;
        }

        static bool EnsureMaterial()
        {
            if (_material != null)
            {
                return true;
            }

            if (_materialAttempted)
            {
                return false;
            }

            _materialAttempted = true;

            // SKIP GetUnderlyingTerrainMaterial path — copying a vanilla terrain Material
            // produces magenta-fallback meshes at runtime (user-confirmed screenshot).
            // Resolve the bundled opaque vertex-color shader directly from the cache so
            // slope quads use the same shader path as the working voxel meshes.
            Shader? shader = null;
            if (WorldSphereMod.Core.Sphere.LoadedShaders.TryGetValue("OpaqueVertexColor", out var bundledShader) && bundledShader != null)
            {
                shader = bundledShader;
                Debug.Log("[WSM3D] Mountain slope material resolved via Core.Sphere.LoadedShaders cache.");
            }

            if (shader == null)
            {
                shader = Shader.Find("WSM3D/OpaqueVertexColor");
                if (shader != null)
                {
                    Debug.Log("[WSM3D] Mountain slope material resolved via Shader.Find('WSM3D/OpaqueVertexColor').");
                }
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
                if (shader != null)
                {
                    Debug.Log("[WSM3D] Mountain slope material resolved via Standard fallback.");
                }
            }

            if (shader == null)
            {
                Debug.LogWarning("[WSM3D] No mountain slope smoothing shader found; overlay disabled.");
                return false;
            }

            // Guard: a shader that loaded from the bundle with a valid name
            // can still be unsupported on this GPU (no subshaders/fallbacks
            // match). Using it produces "ERROR: Shader shader is not
            // supported on this GPU" and a game hang.
            if (!shader.isSupported)
            {
                Debug.LogError($"[WSM3D] Mountain slope shader '{shader.name}' is not supported on this GPU; overlay disabled.");
                return false;
            }

            Material material = new Material(shader)
            {
                name = "WSM3D.MountainSlopeSmoothing",
                enableInstancing = true,
            };
            if (!material.enableInstancing)
            {
                UnityEngine.Object.Destroy(material);
                Debug.LogWarning("[WSM3D] Mountain slope smoothing material rejected enableInstancing; overlay disabled.");
                return false;
            }

            // Tint must be white so vertex colors are the sole albedo source.
            // OpaqueVertexColor multiplies vertex color * _Color; a non-white
            // tint darkens the already earth-toned vertex colors to near-black.
            material.color = Color.white;
            try
            {
                material.SetColor("_BaseColor", Color.white);
                material.SetColor("_Color", Color.white);
            }
            catch { }

            // OpaqueVertexColor is unlit (LightMode=Always), so emission is
            // unnecessary — the shader outputs vertex_color * _Color + _EmissionColor
            // directly. A non-zero emission tints the biome colors gray. Keep it at
            // zero so vertex colors are the sole albedo source.
            material.EnableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", Color.black);
            }

            _material = material;
            Debug.Log($"[WSM3D] Mountain slope material created: shader='{shader.name}' instancing={material.enableInstancing}");
            return true;

        }
    }

    [Phase(nameof(SavedSettings.MountainSlopeSmoothing))]
    [HarmonyPatch(typeof(WorldTilemap), nameof(WorldTilemap.redrawTiles))]
    public static class MountainSlopeRedrawPatch
    {
        [HarmonyPostfix]
        public static void OnRedraw()
        {
            MountainSlopeSurface.EnsureActive();
            MountainSlopeSurface.RequestRebuild();
        }
    }
}
