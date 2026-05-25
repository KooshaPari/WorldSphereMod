using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WorldSphereMod.Terrain
{
    /// <summary>
    /// Cliff-facing slope mesh for upstream terrain. The underlying voxel-like terrain
    /// remains unchanged; this only emits geometry for steep tile transitions.
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

            List<CliffQuad> quads = DetectCliffQuads(width, height);
            if (quads.Count == 0)
            {
                return;
            }

            Debug.Log($"[WSM3D] MountainSlopeSmoothing rebuilt {quads.Count} cliff quads.");

            Vector3[] vertices = new Vector3[quads.Count * 4];
            Color32[] colors = new Color32[quads.Count * 4];
            int[] triangles = new int[quads.Count * 6];

            int vi = 0;
            int ti = 0;
            for (int i = 0; i < quads.Count; i++)
            {
                CliffQuad quad = quads[i];
                if (quad.IsVertical)
                {
                    Vector3 p00 = Core.Sphere.SpherePos(quad.X, quad.Y, quad.HeightA);
                    Vector3 p01 = Core.Sphere.SpherePos(quad.X, quad.Y + 1, quad.HeightA);
                    Vector3 p10 = Core.Sphere.SpherePos(quad.X + 1, quad.Y, quad.HeightB);
                    Vector3 p11 = Core.Sphere.SpherePos(quad.X + 1, quad.Y + 1, quad.HeightB);

                    vertices[vi++] = p00;
                    vertices[vi++] = p01;
                    vertices[vi++] = p10;
                    vertices[vi++] = p11;
                }
                else
                {
                    Vector3 p00 = Core.Sphere.SpherePos(quad.X, quad.Y, quad.HeightA);
                    Vector3 p10 = Core.Sphere.SpherePos(quad.X + 1, quad.Y, quad.HeightA);
                    Vector3 p01 = Core.Sphere.SpherePos(quad.X, quad.Y + 1, quad.HeightB);
                    Vector3 p11 = Core.Sphere.SpherePos(quad.X + 1, quad.Y + 1, quad.HeightB);

                    vertices[vi++] = p00;
                    vertices[vi++] = p10;
                    vertices[vi++] = p01;
                    vertices[vi++] = p11;
                }

                int baseColor = i * 4;
                colors[baseColor] = quad.ColorA;
                colors[baseColor + 1] = quad.ColorA;
                colors[baseColor + 2] = quad.ColorB;
                colors[baseColor + 3] = quad.ColorB;

                int baseVertex = i * 4;
                triangles[ti++] = baseVertex;
                triangles[ti++] = baseVertex + 1;
                triangles[ti++] = baseVertex + 2;
                triangles[ti++] = baseVertex + 2;
                triangles[ti++] = baseVertex + 1;
                triangles[ti++] = baseVertex + 3;
            }

            _mesh.vertices = vertices;
            _mesh.triangles = triangles;
            _mesh.colors32 = colors;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
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
                            if (Mathf.Abs(tileHeight - rightHeight) > 1f)
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
                            if (Mathf.Abs(tileHeight - upHeight) > 1f)
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
            FieldInfo? terrainField = typeof(Core.Sphere).GetField(
                "CompoundSphereMaterial",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (terrainField != null && terrainField.GetValue(null) is Material terrainMaterial && terrainMaterial != null)
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
                Debug.LogWarning("[WSM3D] No mountain slope smoothing shader found; overlay disabled.");
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

            material.color = new Color(0.55f, 0.45f, 0.35f, 1f);
            try
            {
                material.SetColor("_BaseColor", new Color(0.55f, 0.45f, 0.35f, 1f));
                material.SetColor("_Color", new Color(0.55f, 0.45f, 0.35f, 1f));
            }
            catch { }

            _material = material;
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
