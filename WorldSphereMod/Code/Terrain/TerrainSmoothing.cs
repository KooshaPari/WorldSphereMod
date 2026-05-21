using HarmonyLib;
using System;
using UnityEngine;
using CompoundSpheres;

namespace WorldSphereMod.Terrain
{
    /// <summary>
    /// Optional smooth overlay for the upstream CompoundSpheres terrain mesh.
    /// The blocky backend stays in place underneath; this layer rebuilds a
    /// transparent heightfield mesh from the same tile data and blends over it.
    /// </summary>
    public sealed class TerrainSmoothingSurface : MonoBehaviour
    {
        public static TerrainSmoothingSurface? Instance { get; private set; }

        static Material? _material;
        static bool _materialAttempted;

        MeshFilter? _filter;
        MeshRenderer? _renderer;
        Mesh? _mesh;
        bool _dirty = true;

        const float kOverlayLift = 0.06f;
        const float kOverlayAlpha = 0.72f;

        public static TerrainSmoothingSurface? Create(Transform parent)
        {
            if (Instance != null)
            {
                Destroy();
            }

            if (!EnsureMaterial())
            {
                return null;
            }

            GameObject go = new GameObject("WorldSphere Terrain Smoothing");
            go.transform.SetParent(parent, worldPositionStays: false);

            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var surface = go.AddComponent<TerrainSmoothingSurface>();
            surface._filter = filter;
            surface._renderer = renderer;
            surface._mesh = new Mesh { name = "WorldSphere.TerrainSmoothing" };
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
            if (!Core.IsWorld3D || !Core.savedSettings.TerrainSmoothing)
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
            if (!Core.IsWorld3D || !Core.savedSettings.TerrainSmoothing)
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

            Tools.ClearTileHeightSmoothCache();

            int width = MapBox.width;
            int height = MapBox.height;
            if (width <= 1 || height <= 1)
            {
                return;
            }

            int vertexCount = width * height;
            Vector3[] vertices = new Vector3[vertexCount];
            Color32[] colors = new Color32[vertexCount];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width) + x;
                    float sampleHeight = SampleHeight(x, y);
                    vertices[i] = Core.Sphere.SpherePos(x, y, sampleHeight + kOverlayLift);
                    colors[i] = SampleColor(x, y);
                }
            }

            int xCells = Core.Sphere.IsWrapped ? width : width - 1;
            int yCells = height - 1;
            int triangleCount = xCells * yCells * 6;
            if (triangleCount <= 0)
            {
                return;
            }

            int[] triangles = new int[triangleCount];
            int t = 0;
            for (int y = 0; y < yCells; y++)
            {
                int row = y * width;
                int nextRow = (y + 1) * width;
                for (int x = 0; x < xCells; x++)
                {
                    int xNext = x + 1;
                    if (xNext >= width)
                    {
                        if (!Core.Sphere.IsWrapped)
                        {
                            continue;
                        }
                        xNext = 0;
                    }

                    int i00 = row + x;
                    int i10 = row + xNext;
                    int i01 = nextRow + x;
                    int i11 = nextRow + xNext;

                    triangles[t++] = i00;
                    triangles[t++] = i10;
                    triangles[t++] = i11;
                    triangles[t++] = i00;
                    triangles[t++] = i11;
                    triangles[t++] = i01;
                }
            }

            if (t != triangleCount)
            {
                Array.Resize(ref triangles, t);
            }

            _mesh.vertices = vertices;
            _mesh.triangles = triangles;
            _mesh.colors32 = colors;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        float SampleHeight(int x, int y)
        {
            Vector2 sample = new Vector2(x + 0.5f, y + 0.5f);
            return Tools.GetTileHeightSmooth(sample);
        }

        Color32 SampleColor(int x, int y)
        {
            WorldTile tile = ResolveTile(x, y);
            if (tile == null)
            {
                return new Color32(0, 0, 0, 0);
            }

            return Core.Sphere.GetColor(tile.data.tile_id);
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

            string[] candidates =
            {
                "Sprites/Default",
                "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Lit",
                "Standard",
            };

            foreach (string name in candidates)
            {
                Shader shader = Shader.Find(name);
                if (shader == null)
                {
                    continue;
                }

                Material material = new Material(shader)
                {
                    name = "WSM3D.TerrainSmoothing",
                    renderQueue = 3000,
                    mainTexture = Texture2D.whiteTexture,
                };
                material.color = new Color(1f, 1f, 1f, kOverlayAlpha);

                if (!material.enableInstancing)
                {
                    material.enableInstancing = true;
                }

                if (!material.enableInstancing)
                {
                    UnityEngine.Object.Destroy(material);
                    continue;
                }

                _material = material;
                return true;
            }

            Debug.LogWarning("[WSM3D] No terrain smoothing shader found; overlay disabled.");
            return false;
        }
    }

    [Phase(nameof(SavedSettings.TerrainSmoothing))]
    [HarmonyPatch(typeof(Core.Sphere), nameof(Core.Sphere.Begin))]
    public static class TerrainSmoothingBeginPatch
    {
        [HarmonyPostfix]
        public static void OnSphereBegin()
        {
            TerrainSmoothingSurface.EnsureActive();
        }
    }

    [Phase(nameof(SavedSettings.TerrainSmoothing))]
    [HarmonyPatch(typeof(Core.Sphere), nameof(Core.Sphere.Finish))]
    public static class TerrainSmoothingFinishPatch
    {
        [HarmonyPrefix]
        public static void OnSphereFinish()
        {
            TerrainSmoothingSurface.Destroy();
        }
    }

    [Phase(nameof(SavedSettings.TerrainSmoothing))]
    [HarmonyPatch(typeof(Core.Sphere), nameof(Core.Sphere.UpdateBaseLayer))]
    public static class TerrainSmoothingBaseLayerPatch
    {
        [HarmonyPostfix]
        public static void OnUpdate()
        {
            TerrainSmoothingSurface.RequestRebuild();
        }
    }

    [Phase(nameof(SavedSettings.TerrainSmoothing))]
    [HarmonyPatch(typeof(Core.Sphere), nameof(Core.Sphere.UpdateScale))]
    public static class TerrainSmoothingScalePatch
    {
        [HarmonyPostfix]
        public static void OnUpdate()
        {
            TerrainSmoothingSurface.RequestRebuild();
        }
    }

    [Phase(nameof(SavedSettings.TerrainSmoothing))]
    [HarmonyPatch(typeof(Core.Sphere), nameof(Core.Sphere.UpdateTexture))]
    public static class TerrainSmoothingTexturePatch
    {
        [HarmonyPostfix]
        public static void OnUpdate()
        {
            TerrainSmoothingSurface.RequestRebuild();
        }
    }
}
