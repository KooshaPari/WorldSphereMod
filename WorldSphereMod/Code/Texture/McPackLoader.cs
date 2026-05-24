using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using NeoModLoader.utils;
using WorldSphereMod.Import;

namespace WorldSphereMod.Textures
{
    public static class McPackLoader
    {
        static readonly int _mainTexId = Shader.PropertyToID("_MainTex");
        static readonly int _baseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int _normalMapId = Shader.PropertyToID("_BumpMap");
        static readonly string _bundleAtlasAssetName = "atlas";

        static readonly string ConfigRoot = TexturePackImporter.GetImportCacheRoot();

        static bool _initialized;
        static bool _isLoaded;
        static Texture2D? _mainAtlas;
        static Texture2D? _normalAtlas;
        static McPackManifestIO.McPackManifest? _manifest;

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
                return;
            }

            if (!Directory.Exists(ConfigRoot))
            {
                return;
            }

            TryLoadLatestManifest();
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
                    // ignore individual manifest read errors
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

        static Texture2D? LoadAtlasFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new(2, 2, TextureFormat.RGBA32, true, true);
                // Reflection bypass: Texture2D.LoadImage / ImageConversion.LoadImage are
                // hidden from WorldBox's stripped UnityEngine reference DLL but still
                // exist in the runtime assembly. Invoke via System.Reflection.
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

                var wrappedBundle = AssetBundleUtils.GetAssetBundle(System.IO.Path.GetFileNameWithoutExtension(path));
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
                // NML WrappedAssetBundle is cached — no unload needed.
                return atlas;
            }
            catch
            {
                Debug.LogWarning($"[WSM3D] Failed to load atlas AssetBundle '{path}'.");
                return null;
            }
        }

        // Reflection-based PNG decoder. Both 'Texture2D.LoadImage(byte[])' (legacy
        // instance method, deprecated since Unity 2018 but still in the runtime
        // assembly) and 'UnityEngine.ImageConversion.LoadImage(Texture2D, byte[])'
        // (current static helper) are tried in order. Returns true on success.
        static bool TryLoadPngViaReflection(Texture2D tex, byte[] bytes)
        {
            try
            {
                // 1) Try Texture2D's own LoadImage(byte[]) instance method.
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

                // 2) Try UnityEngine.ImageConversion.LoadImage(Texture2D, byte[]).
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
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[WSM3D] TryLoadPngViaReflection threw: {ex.GetType().Name}: {ex.Message}");
            }

            return false;
        }
    }
}
