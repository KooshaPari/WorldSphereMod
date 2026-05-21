using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace WorldSphereMod.Textures
{
    public static class McPackLoader
    {
        public sealed class AtlasRect
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public float UvX { get; set; }
            public float UvY { get; set; }
            public float UvWidth { get; set; }
            public float UvHeight { get; set; }
        }

        public sealed class TextureMapping
        {
            [JsonProperty("mc_block_name")]
            public string McBlockName { get; set; } = string.Empty;

            [JsonProperty("wsm3d_class")]
            public string Wsm3dClass { get; set; } = string.Empty;

            [JsonProperty("rect")]
            public AtlasRect Rect { get; set; } = new();

            [JsonProperty("normal_rect")]
            public AtlasRect? NormalRect { get; set; }
        }

        public sealed class McPackManifest
        {
            [JsonProperty("pack_name")]
            public string PackName { get; set; } = string.Empty;

            [JsonProperty("source_path")]
            public string SourcePath { get; set; } = string.Empty;

            [JsonProperty("source_hash")]
            public string SourceHash { get; set; } = string.Empty;

            [JsonProperty("atlas_rgb")]
            public string AtlasRgb { get; set; } = string.Empty;

            [JsonProperty("atlas_bundle")]
            public string? AtlasBundle { get; set; }

            [JsonProperty("atlas_normal")]
            public string? AtlasNormal { get; set; }

            [JsonProperty("atlas_width")]
            public int AtlasWidth { get; set; }

            [JsonProperty("atlas_height")]
            public int AtlasHeight { get; set; }

            [JsonProperty("mappings")]
            public List<TextureMapping> Mappings { get; set; } = new();
        }

        static readonly int _mainTexId = Shader.PropertyToID("_MainTex");
        static readonly int _baseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int _normalMapId = Shader.PropertyToID("_BumpMap");
        static readonly string _bundleAtlasAssetName = "atlas";

        static readonly string ConfigRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData",
            "LocalLow",
            "mkarpenko",
            "WorldBox",
            "mods_config",
            "wsm3d-texturepack");

        static bool _initialized;
        static bool _isLoaded;
        static Texture2D? _mainAtlas;
        static Texture2D? _normalAtlas;
        static McPackManifest? _manifest;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            if (!Directory.Exists(ConfigRoot))
            {
                return;
            }

            TryLoadLatestManifest();
            if (!_isLoaded)
            {
                return;
            }

            if (_manifest == null || _manifest.Mappings.Count == 0)
            {
                return;
            }
        }

        public static void ApplyToMaterial(Material material)
        {
            if (material == null || !_isLoaded) return;
            if (_mainAtlas != null)
            {
                material.mainTexture = _mainAtlas;
                material.SetTexture(_mainTexId, _mainAtlas);
                if (material.HasProperty(_baseMapId))
                {
                    material.SetTexture(_baseMapId, _mainAtlas);
                }
            }

            if (_normalAtlas != null && material.HasProperty(_normalMapId))
            {
                material.SetTexture(_normalMapId, _normalAtlas);
            }
        }

        static void TryLoadLatestManifest()
        {
            IReadOnlyList<string> manifestCandidates = Array.Empty<string>();
            try
            {
                manifestCandidates = Directory.GetFiles(ConfigRoot, "manifest.json", SearchOption.AllDirectories);
            }
            catch
            {
                Debug.LogWarning("[WSM3D] Failed to scan wsm3d-texturepack directory.");
                return;
            }

            string? latestManifest = null;
            DateTime latestTime = DateTime.MinValue;
            foreach (var candidate in manifestCandidates)
            {
                try
                {
                    DateTime candidateTime = File.GetLastWriteTimeUtc(candidate);
                    if (candidateTime > latestTime)
                    {
                        latestTime = candidateTime;
                        latestManifest = candidate;
                    }
                }
                catch
                {
                    // ignore individual manifest read errors
                }
            }

            if (latestManifest == null)
            {
                return;
            }

            if (!TryReadManifest(latestManifest, out var manifest))
            {
                return;
            }

            _manifest = manifest;
            string manifestDir = Path.GetDirectoryName(latestManifest)!;
            if (string.IsNullOrWhiteSpace(manifest.AtlasBundle))
            {
                _mainAtlas = LoadAtlasFile(Path.Combine(manifestDir, manifest.AtlasRgb));
            }
            else
            {
                _mainAtlas = LoadAtlasBundle(Path.Combine(manifestDir, manifest.AtlasBundle));
            }
            if (string.IsNullOrWhiteSpace(manifest.AtlasNormal) == false)
            {
                _normalAtlas = LoadAtlasFile(Path.Combine(Path.GetDirectoryName(latestManifest)!, manifest.AtlasNormal!));
            }
            _isLoaded = _mainAtlas != null;
            if (_isLoaded)
            {
                Debug.Log($"[WSM3D] Loaded WSM3D MCPack atlas '{manifest.PackName}' ({manifest.AtlasWidth}x{manifest.AtlasHeight}) with {_manifest.Mappings.Count} mapping entries.");
            }
        }

        static bool TryReadManifest(string manifestPath, out McPackManifest manifest)
        {
            manifest = new McPackManifest();
            try
            {
                string text = File.ReadAllText(manifestPath);
                var parsed = JsonConvert.DeserializeObject<McPackManifest>(text);
                if (parsed == null)
                {
                    Debug.LogWarning($"[WSM3D] Failed to parse manifest '{manifestPath}'.");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(parsed.AtlasRgb))
                {
                    Debug.LogWarning($"[WSM3D] Manifest '{manifestPath}' has no atlas_rgb path.");
                    return false;
                }

                manifest = parsed;
                return true;
            }
            catch
            {
                Debug.LogWarning($"[WSM3D] Error reading manifest '{manifestPath}'.");
                return false;
            }
        }

        static Texture2D? LoadAtlasFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new(2, 2, TextureFormat.RGBA32, true, true);
                // Unity's PNG parse APIs (Texture2D.LoadImage / ImageConversion.LoadImage) are
                // not exposed by WorldBox's stripped UnityEngine reference DLL. Skip the
                // runtime PNG decode; user must pre-bake to AssetBundle. Phase 5b ships
                // the offline atlas bake pipeline.
                Debug.LogWarning($"[WSM3D] Atlas runtime PNG load not supported; bake AssetBundle for '{path}'.");
                return null;
                // (unreachable but kept for diff legibility) if (!ImageConversion.LoadImage(tex, bytes))

                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Clamp;
                return tex;
            }
            catch
            {
                Debug.LogWarning($"[WSM3D] Failed to load atlas texture '{path}'.");
                return null;
            }
        }

        static Texture2D? LoadAtlasBundle(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null)
                {
                    Debug.LogWarning($"[WSM3D] Failed to load atlas AssetBundle '{path}'.");
                    return null;
                }

                Texture2D? atlas = bundle.LoadAsset<Texture2D>(_bundleAtlasAssetName);
                if (atlas == null)
                {
                    Debug.LogWarning($"[WSM3D] AssetBundle '{path}' is missing texture '{_bundleAtlasAssetName}'.");
                }
                bundle.Unload(false);
                return atlas;
            }
            catch
            {
                Debug.LogWarning($"[WSM3D] Failed to load atlas AssetBundle '{path}'.");
                return null;
            }
        }
    }
}
