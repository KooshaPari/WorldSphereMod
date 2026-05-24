using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
            public string SourcePath { get; set; } = string.Empty;
            public bool IsZip { get; set; }
        }

        public sealed class PackMetaInfo
        {
            public int PackFormat { get; set; }
            public string Description { get; set; } = string.Empty;
        }

        public sealed class ImportAtLoadResult
        {
            public bool ScanRootExists { get; set; }
            public string ScanRoot { get; set; } = string.Empty;
            public string CacheRoot { get; set; } = string.Empty;
            public IReadOnlyList<string> ScannedPaths { get; set; } = Array.Empty<string>();
            public IReadOnlyList<string> ValidatedPaths { get; set; } = Array.Empty<string>();
            public string? SelectedPackPath { get; set; }
            public PackMetaInfo? SelectedPackMeta { get; set; }
            public IReadOnlyList<string> BlockTextureNames { get; set; } = Array.Empty<string>();
            public string? ManifestStubPath { get; set; }
            public int KnownMappingCount { get; set; }
            public int KnownMappedInPackCount { get; set; }
            public bool AtlasImportStubbed { get; set; } = true;
            public string Message { get; set; } = string.Empty;
        }

        public const string ManifestFormatVersion = "wsm3d_mcpack_v1";
        public const string ManifestImportStatusStub = "stub_enumerated";

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
        /// Lists Minecraft block texture base names (without .png) under
        /// <see cref="BlockTexturePrefix"/> in a folder pack or zip archive.
        /// </summary>
        public static IReadOnlyList<string> EnumerateBlockTextureNames(string candidatePath, bool isZip)
        {
            var names = new List<string>();
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                return names;
            }

            try
            {
                if (isZip)
                {
                    if (!File.Exists(candidatePath))
                    {
                        return names;
                    }

                    using var archive = ZipFile.OpenRead(candidatePath);
                    foreach (var entry in archive.Entries)
                    {
                        if (TryParseBlockTextureEntryName(entry.FullName, out string blockName))
                        {
                            names.Add(blockName);
                        }
                    }
                }
                else
                {
                    string blockDir = Path.Combine(
                        candidatePath,
                        "assets",
                        "minecraft",
                        "textures",
                        "block");
                    if (!Directory.Exists(blockDir))
                    {
                        return names;
                    }

                    foreach (string filePath in Directory.EnumerateFiles(blockDir, "*.png", SearchOption.TopDirectoryOnly))
                    {
                        names.Add(Path.GetFileNameWithoutExtension(filePath));
                    }
                }
            }
            catch
            {
                return Array.Empty<string>();
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        static bool TryParseBlockTextureEntryName(string entryPath, out string blockName)
        {
            blockName = string.Empty;
            if (string.IsNullOrWhiteSpace(entryPath) ||
                !entryPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string normalized = entryPath.Replace('\\', '/');
            if (!normalized.StartsWith(BlockTexturePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string relative = normalized.Substring(BlockTexturePrefix.Length);
            if (relative.Contains('/'))
            {
                return false;
            }

            blockName = Path.GetFileNameWithoutExtension(relative);
            return blockName.Length > 0;
        }

        /// <summary>
        /// Builds a manifest JSON stub aligned with Tools/wsm3d-mcpack-import/import.py output,
        /// without atlas pixels or UV rects.
        /// </summary>
        public static string BuildManifestStubJson(
            string packPath,
            bool isZip,
            PackMetaInfo meta,
            IReadOnlyList<string> blockTextureNames)
        {
            string packStem = isZip
                ? Path.GetFileNameWithoutExtension(packPath)
                : new DirectoryInfo(packPath).Name;
            var knownInPack = new List<object>();
            foreach (string blockName in blockTextureNames)
            {
                if (TexturePackRegistry.TryGetWsm3dClass(blockName, out string wsm3dClass))
                {
                    knownInPack.Add(new
                    {
                        mc_block_name = blockName,
                        wsm3d_class = wsm3dClass,
                    });
                }
            }

            var payload = new
            {
                format = ManifestFormatVersion,
                import_status = ManifestImportStatusStub,
                pack_name = packStem,
                source_path = packPath,
                source_hash = ComputeSourceHashStub(packPath, isZip),
                created_utc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss") + "Z",
                pack_format = meta.PackFormat,
                description = meta.Description,
                atlas_rgb = string.Empty,
                atlas_normal = (string?)null,
                atlas_width = 0,
                atlas_height = 0,
                block_textures_found = blockTextureNames.Count,
                block_textures = blockTextureNames,
                mappings = knownInPack,
            };

            return JsonConvert.SerializeObject(payload, Formatting.Indented);
        }

        static string ComputeSourceHashStub(string packPath, bool isZip)
        {
            try
            {
                if (isZip && File.Exists(packPath))
                {
                    using var stream = File.OpenRead(packPath);
                    using var sha = SHA256.Create();
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
                }

                if (!isZip && Directory.Exists(packPath))
                {
                    using var sha = SHA256.Create();
                    foreach (string filePath in Directory.EnumerateFiles(packPath, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    {
                        string relative = filePath.Substring(packPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        byte[] relativeBytes = Encoding.UTF8.GetBytes(relative.Replace('\\', '/'));
                        sha.TransformBlock(relativeBytes, 0, relativeBytes.Length, null, 0);
                        byte[] content = File.ReadAllBytes(filePath);
                        sha.TransformBlock(content, 0, content.Length, null, 0);
                    }

                    sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    return BitConverter.ToString(sha.Hash!).Replace("-", string.Empty).ToLowerInvariant();
                }
            }
            catch
            {
                // fall through to empty hash for stub
            }

            return string.Empty;
        }

        /// <summary>
        /// Writes the manifest stub under cacheRoot and optionally logs a one-line summary.
        /// </summary>
        public static bool TryWriteManifestStub(
            string cacheRoot,
            string packPath,
            bool isZip,
            PackMetaInfo meta,
            IReadOnlyList<string> blockTextureNames,
            Action<string>? log,
            out string manifestPath,
            out string manifestJson)
        {
            manifestPath = string.Empty;
            manifestJson = BuildManifestStubJson(packPath, isZip, meta, blockTextureNames);

            try
            {
                string packStem = isZip
                    ? Path.GetFileNameWithoutExtension(packPath)
                    : new DirectoryInfo(packPath).Name;
                string outputDir = Path.Combine(cacheRoot, SanitizePathSegment(packStem) + "_stub");
                Directory.CreateDirectory(outputDir);
                manifestPath = Path.Combine(outputDir, "manifest.json");
                File.WriteAllText(manifestPath, manifestJson, Encoding.UTF8);

                int mapped = blockTextureNames.Count(name => TexturePackRegistry.TryGetWsm3dClass(name, out _));
                log?.Invoke(
                    $"[WSM3D] texturepack manifest stub: {manifestPath} " +
                    $"({blockTextureNames.Count} block pngs, {mapped} known mappings)");
                return true;
            }
            catch
            {
                return false;
            }
        }

        static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "pack";
            }

            var builder = new StringBuilder(value.Length);
            foreach (char ch in value)
            {
                builder.Append(Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Load-time hook: scan user packs, validate the first usable candidate, then bind any
        /// pre-baked atlas manifest via <see cref="Textures.McPackLoader"/>.
        /// </summary>
        public static ImportAtLoadResult TryImportAtLoad(
            string? scanRoot = null,
            string? cacheRoot = null,
            Action<string>? log = null)
        {
            string resolvedScanRoot = scanRoot ?? GetTexturePacksScanRoot();
            string resolvedCacheRoot = cacheRoot ?? GetImportCacheRoot();
            bool scanRootExists = Directory.Exists(resolvedScanRoot);

            var scannedPaths = new List<string>();
            var validatedPaths = new List<string>();
            string? selectedPath = null;
            PackMetaInfo? selectedMeta = null;
            bool selectedIsZip = false;
            IReadOnlyList<string> blockTextureNames = Array.Empty<string>();
            string? manifestStubPath = null;
            int knownMappedInPack = 0;

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
                            selectedIsZip = candidate.IsZip;
                        }
                    }
                }
            }

            if (selectedPath != null && selectedMeta != null)
            {
                blockTextureNames = EnumerateBlockTextureNames(selectedPath, selectedIsZip);
                knownMappedInPack = blockTextureNames.Count(name =>
                    TexturePackRegistry.TryGetWsm3dClass(name, out _));
                if (TryWriteManifestStub(
                        resolvedCacheRoot,
                        selectedPath,
                        selectedIsZip,
                        selectedMeta,
                        blockTextureNames,
                        log,
                        out manifestStubPath,
                        out _))
                {
                    log?.Invoke($"[WSM3D] texturepack manifest stub json written ({manifestStubPath})");
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
            else if (manifestStubPath == null)
            {
                message = "validated_enumerated_manifest_stub_write_failed";
            }
            else
            {
                message = "enumerated_manifest_stub_written";
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
                BlockTextureNames = blockTextureNames,
                ManifestStubPath = manifestStubPath,
                KnownMappingCount = TexturePackRegistry.DefaultMappings.Count,
                KnownMappedInPackCount = knownMappedInPack,
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
                block_textures_found = result.BlockTextureNames.Count,
                known_mappings = result.KnownMappingCount,
                known_mapped_in_pack = result.KnownMappedInPackCount,
                manifest_stub_path = result.ManifestStubPath,
                atlas_import_stubbed = result.AtlasImportStubbed,
                message = result.Message,
            };
        }
    }
}
