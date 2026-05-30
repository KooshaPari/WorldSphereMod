using System;
using System.Collections.Generic;
using System.IO;
using NeoModLoader.utils;
using UnityEngine;
using WorldSphereMod.Import;

namespace WorldSphereMod.Textures
{
    public static class McPackLoader
    {
        static readonly int _mainTexId = Shader.PropertyToID("_MainTex");
        static readonly int _baseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int _normalMapId = Shader.PropertyToID("_BumpMap");
        static readonly int _normalMapId2 = Shader.PropertyToID("_NormalMap");
        static readonly int _occlusionMapId = Shader.PropertyToID("_OcclusionMap");
        static readonly int _metallicGlossMapId = Shader.PropertyToID("_MetallicGlossMap");
        static readonly int _heightMapId = Shader.PropertyToID("_HeightMap");
        static readonly int _colorId = Shader.PropertyToID("_Color");
        static readonly string _bundleAtlasAssetName = "atlas";

        static readonly string ConfigRoot = TexturePackImporter.GetImportCacheRoot();
        static readonly string[] StratumResourceRoots =
        {
            Path.Combine("GameResources", "WorldSphereMod", "Stratum"),
            Path.Combine("GameResources", "WorldSphereMod", "StratumVoxelPBR"),
            Path.Combine("GameResources", "WorldSphereMod"),
            "GameResources"
        };

        static bool _initialized;
        static bool _isLoaded;
        static Texture2D? _mainAtlas;
        static Texture2D? _normalAtlas;
        static Texture2D? _occlusionAtlas;
        static Texture2D? _metallicAtlas;
        static Texture2D? _heightAtlas;
        static McPackManifestIO.McPackManifest? _manifest;
        static bool _hasStratumTextures;

        public static void Initialize(string? manifestStubPath = null)
        {
            if (_initialized) return;
            _initialized = true;

            if (!IsMcPackTexturesEnabled())
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(manifestStubPath)
                && File.Exists(manifestStubPath)
                && TryBindImporterStubManifest(manifestStubPath))
            {
                TryLoadStratumTextures();
                return;
            }

            if (Directory.Exists(ConfigRoot))
            {
                TryLoadLatestManifest();
            }

            TryLoadStratumTextures();
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

            if (_normalAtlas != null && material.HasProperty(_normalMapId2))
            {
                material.SetTexture(_normalMapId2, _normalAtlas);
            }

            if (_occlusionAtlas != null && material.HasProperty(_occlusionMapId))
            {
                material.SetTexture(_occlusionMapId, _occlusionAtlas);
            }

            if (_metallicAtlas != null && material.HasProperty(_metallicGlossMapId))
            {
                material.SetTexture(_metallicGlossMapId, _metallicAtlas);
            }

            if (_heightAtlas != null && material.HasProperty(_heightMapId))
            {
                material.SetTexture(_heightMapId, _heightAtlas);
            }

            if (material.HasProperty(_colorId))
            {
                material.SetColor(_colorId, Color.white);
            }
        }

        public static bool HasStratumTextures() => _hasStratumTextures && _mainAtlas != null;

        public static Material? TryCreateStratumMaterial()
        {
            if (!HasStratumTextures())
            {
                return null;
            }

            Shader? shader = ResolveStratumShader();
            if (shader == null)
            {
                return null;
            }

            Material material = new(shader)
            {
                name = "WSM3D.Voxel.StratumVoxelPBR",
                enableInstancing = true,
                renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry + 1
            };

            material.SetTexture(_mainTexId, _mainAtlas);
            material.SetTexture(_baseMapId, _mainAtlas);
            material.SetColor(_colorId, Color.white);
            ApplyToMaterial(material);
            return material;
        }

        static bool IsMcPackTexturesEnabled() =>
            Core.savedSettings != null && Core.savedSettings.EnableMcPackTextures;

        static bool TryBindImporterStubManifest(string manifestPath)
        {
            if (!McPackManifestIO.TryParseManifestFile(manifestPath, out var manifest, out bool isImporterStub)
                || !isImporterStub)
            {
                return false;
            }

            _manifest = manifest;
            _isLoaded = false;
            _mainAtlas = null;
            _normalAtlas = null;
            _occlusionAtlas = null;
            _metallicAtlas = null;
            _heightAtlas = null;
            _hasStratumTextures = false;
            Debug.Log(
                $"[WSM3D] Texturepack manifest stub bound '{manifest.PackName}' " +
                $"({manifest.Mappings.Count} mappings, atlas import pending).");
            return true;
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
                }
            }

            if (latestManifest == null)
            {
                return;
            }

            if (!McPackManifestIO.TryParseManifestFile(latestManifest, out var manifest, out bool isImporterStub))
            {
                Debug.LogWarning($"[WSM3D] Failed to parse manifest '{latestManifest}'.");
                return;
            }

            if (isImporterStub)
            {
                TryBindImporterStubManifest(latestManifest);
                return;
            }

            if (!McPackManifestIO.HasAtlasPayload(manifest))
            {
                Debug.LogWarning($"[WSM3D] Manifest '{latestManifest}' has no atlas_rgb or atlas_bundle path.");
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
                _normalAtlas = LoadAtlasFile(Path.Combine(manifestDir, manifest.AtlasNormal!));
            }

            _isLoaded = _mainAtlas != null;
            if (_isLoaded)
            {
                Debug.Log(
                    $"[WSM3D] Loaded WSM3D MCPack atlas '{manifest.PackName}' " +
                    $"({manifest.AtlasWidth}x{manifest.AtlasHeight}) with {_manifest.Mappings.Count} mapping entries.");
            }
        }

        static void TryLoadStratumTextures()
        {
            if (_hasStratumTextures && _mainAtlas != null)
            {
                return;
            }

            foreach (string root in StratumResourceRoots)
            {
                string resolvedRoot = Path.Combine(Mod.ModDirectory, root);
                if (!Directory.Exists(resolvedRoot))
                {
                    continue;
                }

                _mainAtlas ??= LoadFirstTextureFromCandidates(resolvedRoot,
                    "Stratum_AlbedoAtlas.png",
                    "Stratum_Albedo.png",
                    "Stratum_BaseColorAtlas.png",
                    "AlbedoAtlas.png",
                    "BaseMap.png");
                _normalAtlas ??= LoadFirstTextureFromCandidates(resolvedRoot,
                    "Stratum_NormalAtlas.png",
                    "Stratum_Normal.png",
                    "NormalAtlas.png",
                    "NormalMap.png");
                _occlusionAtlas ??= LoadFirstTextureFromCandidates(resolvedRoot,
                    "Stratum_AOAtlas.png",
                    "Stratum_AmbientOcclusionAtlas.png",
                    "AOAtlas.png",
                    "OcclusionAtlas.png");
                _metallicAtlas ??= LoadFirstTextureFromCandidates(resolvedRoot,
                    "Stratum_MetallicAtlas.png",
                    "Stratum_RoughnessAtlas.png",
                    "MetallicAtlas.png",
                    "RoughnessAtlas.png");
                _heightAtlas ??= LoadFirstTextureFromCandidates(resolvedRoot,
                    "Stratum_HeightAtlas.png",
                    "HeightAtlas.png");

                _hasStratumTextures = _mainAtlas != null;
                if (_hasStratumTextures)
                {
                    _isLoaded = true;
                    Debug.Log(
                        $"[WSM3D] Loaded Stratum textures from '{resolvedRoot}' " +
                        $"(albedo={(_mainAtlas != null)}, normal={(_normalAtlas != null)}, ao={(_occlusionAtlas != null)}, metallic={(_metallicAtlas != null)}, height={(_heightAtlas != null)}).");
                    return;
                }
            }
        }

        static Texture2D? LoadFirstTextureFromCandidates(string root, params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                string path = Path.Combine(root, candidate);
                Texture2D? texture = LoadAtlasFile(path);
                if (texture != null)
                {
                    return texture;
                }
            }

            return null;
        }

        static Shader? ResolveStratumShader()
        {
            if (Core.Sphere.LoadedShaders.TryGetValue("StratumVoxelPBR", out var cached) && cached != null)
            {
                return cached;
            }

            return Shader.Find("WSM3D/StratumVoxelPBR");
        }

        static Texture2D? LoadAtlasFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new(2, 2, TextureFormat.RGBA32, true, true);
                if (!TryLoadPngViaReflection(tex, bytes))
                {
                    Debug.LogWarning($"[WSM3D] Reflection-based PNG load failed for '{path}'.");
                    return null;
                }

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

                var wrappedBundle = AssetBundleUtils.GetAssetBundle(Path.GetFileNameWithoutExtension(path));
                if (wrappedBundle == null)
                {
                    Debug.LogWarning($"[WSM3D] Failed to load atlas AssetBundle '{path}'.");
                    return null;
                }

                Texture2D? atlas = wrappedBundle.GetObject<Texture2D>(_bundleAtlasAssetName);
                if (atlas == null)
                {
                    Debug.LogWarning($"[WSM3D] AssetBundle '{path}' is missing texture '{_bundleAtlasAssetName}'.");
                }

                return atlas;
            }
            catch
            {
                Debug.LogWarning($"[WSM3D] Failed to load atlas AssetBundle '{path}'.");
                return null;
            }
        }

        static bool TryLoadPngViaReflection(Texture2D tex, byte[] bytes)
        {
            try
            {
                var miInstance = typeof(Texture2D).GetMethod(
                    "LoadImage",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                    null,
                    new System.Type[] { typeof(byte[]) },
                    null);
                if (miInstance != null)
                {
                    object result = miInstance.Invoke(tex, new object[] { bytes });
                    if (result is bool b1) return b1;
                    return true;
                }

                var icType = typeof(Texture2D).Assembly.GetType("UnityEngine.ImageConversion");
                if (icType != null)
                {
                    var miStatic = icType.GetMethod(
                        "LoadImage",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public,
                        null,
                        new System.Type[] { typeof(Texture2D), typeof(byte[]) },
                        null);
                    if (miStatic != null)
                    {
                        object result = miStatic.Invoke(null, new object[] { tex, bytes });
                        if (result is bool b2) return b2;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WSM3D] TryLoadPngViaReflection threw: {ex.GetType().Name}: {ex.Message}");
            }

            return false;
        }
    }
}
