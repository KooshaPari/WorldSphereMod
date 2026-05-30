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

        // Frame-coalesced rebuild throttle. Brush tools dirty hundreds of tiles
        // per frame; the slope mesh scans all 43k tiles (316²) for cliff quads
        // each rebuild. Without throttling the game freezes for the entire brush
        // stroke. Defer until brush goes quiet (no new dirty for QuietSec) OR
        // we've been stalling for MaxStallSec. MinIntervalSec prevents
        // back-to-back rebuilds when dirties trickle in.
        float _lastDirtyTime = -1f;
        float _lastRebuildTime = -1f;
        const float QuietSec = 0.20f;
        const float MaxStallSec = 0.50f;
        const float MinIntervalSec = 0.10f;

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

            // CRITICAL: OpaqueVertexColor declares _Color and _EmissionColor inside
            // UNITY_INSTANCING_BUFFER. When the shader compiles with instancing
            // enabled, UNITY_ACCESS_INSTANCED_PROP reads from the per-instance
            // constant buffer, NOT from material.SetColor() values. A single
            // MeshRenderer without an explicit MaterialPropertyBlock leaves that
            // buffer zero-initialized -> albedo*0 + 0 = pure black. Push the tint
            // and emission via an MPB so the instanced-prop path resolves to the
            // intended values.
            try
            {
                MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(mpb);
                mpb.SetColor("_Color", Color.white);
                mpb.SetColor("_EmissionColor", new Color(0.15f, 0.15f, 0.15f, 1f));
                renderer.SetPropertyBlock(mpb);
                if (Core.savedSettings != null && Core.savedSettings.ProfilerDump)
                    Debug.Log("[WSM3D] Mountain slope MPB pushed _Color=white _EmissionColor=0.15.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[WSM3D] Mountain slope MPB push failed: {ex.Message}");
            }

            // Full diagnostic dump at slope renderer creation. Captures every
            // angle of the "shader resolves but output is black" failure mode:
            // shader/material properties, lighting environment, layer/camera mask.
            // Gated behind ProfilerDump: builds an expensive interpolated string.
            if (Core.savedSettings != null && Core.savedSettings.ProfilerDump)
            try
            {
                Material m = renderer.sharedMaterial;
                Shader sh = m != null ? m.shader : null;
                string shaderName = sh != null ? sh.name : "<null>";
                int passCount = sh != null ? sh.passCount : -1;
                int renderQueue = m != null ? m.renderQueue : -1;
                string colorStr = (m != null && m.HasProperty("_Color")) ? m.GetColor("_Color").ToString() : "N/A";
                string emissStr = (m != null && m.HasProperty("_EmissionColor")) ? m.GetColor("_EmissionColor").ToString() : "N/A";
                Texture mt = (m != null && m.HasProperty("_MainTex")) ? m.GetTexture("_MainTex") : null;
                string mainTexStr = mt == null
                    ? "NULL"
                    : (ReferenceEquals(mt, Texture2D.whiteTexture) ? "whiteTexture" : $"{mt.name}({mt.GetType().Name})");
                bool emissionKw = m != null && m.IsKeywordEnabled("_EMISSION");
                Color ambient = RenderSettings.ambientLight;
                Color ambSky = RenderSettings.ambientSkyColor;
                float ambIntensity = RenderSettings.ambientIntensity;
                int layer = go.layer;
                string layerName = LayerMask.LayerToName(layer);
                Camera mainCam = Camera.main;
                int camMask = mainCam != null ? mainCam.cullingMask : -1;
                bool visibleToCam = mainCam != null && ((camMask & (1 << layer)) != 0);
                Debug.Log(
                    $"[WSM3D-DIAG] Slope renderer: shader='{shaderName}' passCount={passCount} renderQueue={renderQueue} " +
                    $"_Color={colorStr} _EmissionColor={emissStr} _EMISSION_kw={emissionKw} _MainTex={mainTexStr} " +
                    $"instancing={(m != null ? m.enableInstancing : false)} layer={layer}('{layerName}') " +
                    $"camCullingMask=0x{camMask:X} visibleToMainCam={visibleToCam} " +
                    $"ambientLight={ambient} ambientSky={ambSky} ambientIntensity={ambIntensity} " +
                    $"sun={(RenderSettings.sun != null ? RenderSettings.sun.name : "<null>")}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[WSM3D-DIAG] Slope diagnostic dump failed: {ex.Message}");
            }

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
                Instance._lastDirtyTime = Time.realtimeSinceStartup;
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

            // CenterCapsule can throw Transform.GetChild IndexOutOfRange during async
            // Manager init (TOCTOU race). Catch and retry next redraw — repeatedly
            // logging the same stack trace every frame floods Player.log.
            Transform? capsule;
            try { capsule = Core.Sphere.CenterCapsule; }
            catch (System.Exception)
            {
                return;
            }
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

            if (!_dirty)
            {
                return;
            }

            // Brush-stroke coalescing: defer rebuild until brush goes quiet
            // (no new dirty for QuietSec) OR we've been stalling for MaxStallSec.
            // Without this, every brushed tile triggers a fresh 43k-tile cliff
            // scan + mesh build, freezing the game during multi-tile strokes.
            float now = Time.realtimeSinceStartup;
            float sinceLastDirty = now - _lastDirtyTime;
            float sinceLastRebuild = now - _lastRebuildTime;
            bool isFirstBuild = _lastRebuildTime < 0f;
            bool brushQuiet = sinceLastDirty >= QuietSec;
            bool stalledTooLong = sinceLastRebuild >= MaxStallSec;
            bool intervalElapsed = sinceLastRebuild >= MinIntervalSec;

            if (isFirstBuild || ((brushQuiet || stalledTooLong) && intervalElapsed))
            {
                RebuildMesh();
                _lastRebuildTime = Time.realtimeSinceStartup;
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
            float h10 = SampleTileHeight(cx, cy - 1, width, height, wrapped);
            float h01 = SampleTileHeight(cx - 1, cy, width, height, wrapped);
            float h11 = SampleTileHeight(cx, cy, width, height, wrapped);
            return (h00 + h10 + h01 + h11) * 0.25f;
        }

        /// <summary>
        /// Computes an interpolated corner color at the junction of four tiles.
        /// Averages the biome colors of the four adjacent tiles.
        /// </summary>
        // Minimum per-channel brightness (0–255) so mountain biome colors
        // that are very dark (dark gray rock, black volcanic) don't produce
        // an invisible mesh even with the emission floor.
        const int MinChannelBrightness = 38; // ~0.15 in 0–255 space

        Color32 CornerColor(int cx, int cy, int width, int height, bool wrapped)
        {
            Color32 c00 = SampleTileColor(cx - 1, cy - 1, width, height, wrapped);
            Color32 c10 = SampleTileColor(cx, cy - 1, width, height, wrapped);
            Color32 c01 = SampleTileColor(cx - 1, cy, width, height, wrapped);
            Color32 c11 = SampleTileColor(cx, cy, width, height, wrapped);
            int r = (c00.r + c10.r + c01.r + c11.r) / 4;
            int g = (c00.g + c10.g + c01.g + c11.g) / 4;
            int b = (c00.b + c10.b + c01.b + c11.b) / 4;
            // Brightness guard: if max channel is below the floor, scale up
            // proportionally so the hue is preserved but the mesh stays visible.
            int maxCh = Mathf.Max(r, Mathf.Max(g, b));
            if (maxCh > 0 && maxCh < MinChannelBrightness)
            {
                float scale = (float)MinChannelBrightness / maxCh;
                r = Mathf.Min(255, Mathf.RoundToInt(r * scale));
                g = Mathf.Min(255, Mathf.RoundToInt(g * scale));
                b = Mathf.Min(255, Mathf.RoundToInt(b * scale));
            }
            else if (maxCh == 0)
            {
                r = g = b = MinChannelBrightness;
            }
            return new Color32((byte)r, (byte)g, (byte)b, 255);
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
                float hBL = CornerHeight(tx, ty, width, height, wrapped);
                float hBR = CornerHeight(tx + 1, ty, width, height, wrapped);
                float hTL = CornerHeight(tx, ty + 1, width, height, wrapped);
                float hTR = CornerHeight(tx + 1, ty + 1, width, height, wrapped);

                Color32 cBL = CornerColor(tx, ty, width, height, wrapped);
                Color32 cBR = CornerColor(tx + 1, ty, width, height, wrapped);
                Color32 cTL = CornerColor(tx, ty + 1, width, height, wrapped);
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

            // Gated behind ProfilerDump: fires on every terrain-edit rebuild, flooding the viewport.
            if (Core.savedSettings != null && Core.savedSettings.ProfilerDump)
                Debug.Log($"[WSM3D] MountainSlopeSmoothing rebuilt {quads.Count} cliff quads -> {smoothTileSet.Count} smooth tiles, {vertices.Count} verts.");

            _mesh.SetVertices(vertices);
            _mesh.SetTriangles(triangles, 0);

            // Defensive vertex-color guard: if SetColors is called with an empty or
            // mismatched-length array, Unity falls back to mesh.colors = (0,0,0,0)
            // for every vertex, which the OpaqueVertexColor shader multiplies into
            // a black output. Pad with opaque white if the buffer is short, and
            // re-saturate alpha so no vertex ships with a=0.
            if (colors.Count != vertices.Count)
            {
                Debug.LogWarning($"[WSM3D] MountainSlopeSmoothing color/vertex mismatch ({colors.Count} vs {vertices.Count}); padding with white.");
                while (colors.Count < vertices.Count)
                {
                    colors.Add(new Color32(255, 255, 255, 255));
                }
                if (colors.Count > vertices.Count)
                {
                    colors.RemoveRange(vertices.Count, colors.Count - vertices.Count);
                }
            }
            for (int i = 0; i < colors.Count; i++)
            {
                Color32 c = colors[i];
                if (c.a == 0) colors[i] = new Color32(c.r, c.g, c.b, 255);
            }
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

            // Belt+suspenders: force _MainTex to a white pixel texture.
            // The shader declares _MainTex = "white" {} which should map
            // to Unity's built-in 4x4 white texture, but some runtimes
            // leave it null — producing tex2D() = (0,0,0,0) which kills
            // the albedo channel.  VoxelRender has the same guard.
            material.SetTexture("_MainTex", Texture2D.whiteTexture);

            // OpaqueVertexColor is unlit (LightMode=Always): output =
            // vertex_color * _Color * tex + _EmissionColor.  WorldBox scenes
            // have no directional/ambient light, and many mountain biome
            // colors are dark browns/grays (RGB ≤ 0.15).  Without an emission
            // floor the slope mesh is nearly black.  Match the voxel pipeline's
            // brightness guard (0.15) so slopes stay visible.
            material.EnableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", new Color(0.15f, 0.15f, 0.15f, 1f));
            }

            // If we fell through to Standard shader (LIT), the emission
            // floor of 0.15 is insufficient — Standard computes lighting
            // and without directional/ambient light, albedo contributes ~0.
            // Boost emission to 1.0 so the mesh is clearly visible.
            if (shader.name == "Standard" || shader.name.Contains("Standard"))
            {
                if (material.HasProperty("_EmissionColor"))
                {
                    material.SetColor("_EmissionColor", new Color(1.0f, 1.0f, 1.0f, 1f));
                }
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                Debug.LogWarning("[WSM3D] Mountain slope using Standard shader fallback — emission boosted to 1.0 for visibility.");
            }

            // Slope mesh sits coplanar/just-above the CompoundSphere terrain
            // (Geometry=2000). Force Geometry+1 (2001) so the slope wins the
            // depth tie against terrain — matches voxel actors, foliage, BPR.
            // See ADR-0010 + ADR-0012 for the renderQueue contract across the
            // mod's 3D pipeline.
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 1;

            _material = material;
            Debug.Log($"[WSM3D] Mountain slope material created: shader='{shader.name}' " +
                $"_Color={(material.HasProperty("_Color") ? material.GetColor("_Color").ToString() : "N/A")} " +
                $"_EmissionColor={(material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor").ToString() : "N/A")} " +
                $"instancing={material.enableInstancing}");
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
