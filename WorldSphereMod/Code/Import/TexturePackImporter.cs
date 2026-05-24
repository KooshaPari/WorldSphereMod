using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WorldSphereMod.Import
{
    /// <summary>
    /// Scan and validate user-local Minecraft resource packs under
    /// mods_config/wsm3d/texturepacks/. Atlas build and bind are staged;
    /// see docs/journeys/scratch/mc-texture-pack-importer-spec.md.
    /// </summary>
    public static class TexturePackImporter
    {
        public const int MinPackFormat = 1;
        public const int MaxPackFormat = 120;
        public const string PackMetaFileName = "pack.mcmeta";
        public const string BlockTexturePrefix = "assets/minecraft/textures/block/";

        public sealed class TexturePackCandidate
        {
            public string SourcePath { get; init; } = string.Empty;
            public bool IsZip { get; init; }
        }

        public sealed class PackMetaInfo
        {
            public int PackFormat { get; init; }
            public string Description { get; init; } = string.Empty;
        }

        public sealed class ImportAtLoadResult
        {
            public bool ScanRootExists { get; init; }
            public string ScanRoot { get; init; } = string.Empty;
            public string CacheRoot { get; init; } = string.Empty;
            public IReadOnlyList<string> ScannedPaths { get; init; } = Array.Empty<string>();
            public IReadOnlyList<string> ValidatedPaths { get; init; } = Array.Empty<string>();
            public string? SelectedPackPath { get; init; }
            public PackMetaInfo? SelectedPackMeta { get; init; }
            public int KnownMappingCount { get; init; }
            public bool AtlasImportStubbed { get; init; } = true;
            public string Message { get; init; } = string.Empty;
        }

        static string DefaultModConfigRoot =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData",
                "LocalLow",
                "mkarpenko",
                "WorldBox",
                "mods_config");

        public static string GetTexturePacksScanRoot(string? modConfigRoot = null) =>
            Path.Combine(modConfigRoot ?? DefaultModConfigRoot, "wsm3d", "texturepacks");

        public static string GetImportCacheRoot(string? modConfigRoot = null) =>
            Path.Combine(modConfigRoot ?? DefaultModConfigRoot, "wsm3d-texturepack");

        public static IReadOnlyList<TexturePackCandidate> ScanCandidates(string scanRoot)
        {
            var results = new List<TexturePackCandidate>();
            if (!Directory.Exists(scanRoot))
            {
                return results;
            }

            foreach (string zipPath in Directory.EnumerateFiles(scanRoot, "*.zip", SearchOption.TopDirectoryOnly))
            {
                results.Add(new TexturePackCandidate { SourcePath = zipPath, IsZip = true });
            }

            foreach (string dirPath in Directory.EnumerateDirectories(scanRoot, "*", SearchOption.TopDirectoryOnly))
            {
                results.Add(new TexturePackCandidate { SourcePath = dirPath, IsZip = false });
            }

            return results;
        }

        public static bool TryReadPackMeta(string candidatePath, bool isZip, out PackMetaInfo meta, out string error)
        {
            meta = new PackMetaInfo();
            error = string.Empty;

            try
            {
                string jsonText;
                if (isZip)
                {
                    if (!File.Exists(candidatePath))
                    {
                        error = "zip_missing";
                        return false;
                    }

                    using var archive = ZipFile.OpenRead(candidatePath);
                    var entry = archive.GetEntry(PackMetaFileName)
                        ?? archive.GetEntry(PackMetaFileName.Replace('/', '\\'));
                    if (entry == null)
                    {
                        error = "missing_pack_mcmeta";
                        return false;
                    }

                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream);
                    jsonText = reader.ReadToEnd();
                }
                else
                {
                    string metaPath = Path.Combine(candidatePath, PackMetaFileName);
                    if (!File.Exists(metaPath))
                    {
                        error = "missing_pack_mcmeta";
                        return false;
                    }

                    jsonText = File.ReadAllText(metaPath);
                }

                if (!TryParsePackMetaJson(jsonText, out meta, out error))
                {
                    return false;
                }

                if (meta.PackFormat < MinPackFormat || meta.PackFormat > MaxPackFormat)
                {
                    error = $"unsupported_pack_format={meta.PackFormat}";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "parse_error:" + ex.GetType().Name;
                return false;
            }
        }

        public static bool TryParsePackMetaJson(string jsonText, out PackMetaInfo meta, out string error)
        {
            meta = new PackMetaInfo();
            error = string.Empty;

            try
            {
                var root = JObject.Parse(jsonText);
                var pack = root["pack"] as JObject;
                if (pack == null)
                {
                    error = "missing_pack_section";
                    return false;
                }

                int packFormat = pack["pack_format"]?.Value<int>() ?? 0;
                string description = pack["description"]?.ToString() ?? string.Empty;
                meta = new PackMetaInfo
                {
                    PackFormat = packFormat,
                    Description = description,
                };

                if (meta.PackFormat < MinPackFormat || meta.PackFormat > MaxPackFormat)
                {
                    error = $"unsupported_pack_format={meta.PackFormat}";
                    return false;
                }

                return true;
            }
            catch (JsonException)
            {
                error = "invalid_json";
                return false;
            }
        }

        /// <summary>
        /// Load-time hook: scan user packs, validate the first usable candidate, then bind any
        /// pre-baked atlas manifest via <see cref="Textures.McPackLoader"/>.
        /// </summary>
        public static ImportAtLoadResult TryImportAtLoad(string? scanRoot = null, string? cacheRoot = null)
        {
            string resolvedScanRoot = scanRoot ?? GetTexturePacksScanRoot();
            string resolvedCacheRoot = cacheRoot ?? GetImportCacheRoot();
            bool scanRootExists = Directory.Exists(resolvedScanRoot);

            var scannedPaths = new List<string>();
            var validatedPaths = new List<string>();
            string? selectedPath = null;
            PackMetaInfo? selectedMeta = null;

            if (scanRootExists)
            {
                foreach (var candidate in ScanCandidates(resolvedScanRoot))
                {
                    scannedPaths.Add(candidate.SourcePath);
                    if (TryReadPackMeta(candidate.SourcePath, candidate.IsZip, out var meta, out _))
                    {
                        validatedPaths.Add(candidate.SourcePath);
                        if (selectedPath == null)
                        {
                            selectedPath = candidate.SourcePath;
                            selectedMeta = meta;
                        }
                    }
                }
            }

            string message;
            if (!scanRootExists)
            {
                message = "texturepack_scan_root_missing";
            }
            else if (selectedPath == null)
            {
                message = scannedPaths.Count == 0
                    ? "no_texturepack_candidates"
                    : "no_valid_texturepack";
            }
            else
            {
                message = "validated_stub_atlas_import_pending";
            }

            return new ImportAtLoadResult
            {
                ScanRootExists = scanRootExists,
                ScanRoot = resolvedScanRoot,
                CacheRoot = resolvedCacheRoot,
                ScannedPaths = scannedPaths,
                ValidatedPaths = validatedPaths,
                SelectedPackPath = selectedPath,
                SelectedPackMeta = selectedMeta,
                KnownMappingCount = TexturePackRegistry.DefaultMappings.Count,
                AtlasImportStubbed = true,
                Message = message,
            };
        }

        public static object BuildBridgeImportPayload(string? packPath = null, string? scanRoot = null, string? cacheRoot = null)
        {
            if (!string.IsNullOrWhiteSpace(packPath))
            {
                string path = packPath!;
                bool isZip = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                bool valid = TryReadPackMeta(path, isZip, out var meta, out string error);
                return new
                {
                    ok = valid,
                    mode = "single",
                    path,
                    error = valid ? null : error,
                    pack_format = valid ? meta.PackFormat : (int?)null,
                    description = valid ? meta.Description : null,
                    known_mappings = TexturePackRegistry.DefaultMappings.Count,
                    atlas_import_stubbed = true,
                };
            }

            var result = TryImportAtLoad(scanRoot, cacheRoot);
            return new
            {
                ok = true,
                mode = "scan",
                scan_root = result.ScanRoot,
                cache_root = result.CacheRoot,
                scan_root_exists = result.ScanRootExists,
                scanned = result.ScannedPaths,
                validated = result.ValidatedPaths,
                selected = result.SelectedPackPath,
                pack_format = result.SelectedPackMeta?.PackFormat,
                description = result.SelectedPackMeta?.Description,
                known_mappings = result.KnownMappingCount,
                atlas_import_stubbed = result.AtlasImportStubbed,
                message = result.Message,
            };
        }
    }
}
