using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using WorldSphereMod.Import;

namespace WorldSphereMod.Textures
{
    /// <summary>
    /// Unity-free manifest parse/bind helpers shared by <see cref="McPackLoader"/> and unit tests.
    /// </summary>
    public static class McPackManifestIO
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
            [JsonProperty("format")]
            public string Format { get; set; } = string.Empty;

            [JsonProperty("import_status")]
            public string ImportStatus { get; set; } = string.Empty;

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

        public static bool IsImporterStubManifest(McPackManifest manifest)
        {
            if (manifest == null)
            {
                return false;
            }

            if (string.Equals(
                    manifest.ImportStatus,
                    TexturePackImporter.ManifestImportStatusStub,
                    StringComparison.Ordinal))
            {
                return true;
            }

            return string.IsNullOrWhiteSpace(manifest.AtlasRgb)
                && string.IsNullOrWhiteSpace(manifest.AtlasBundle);
        }

        public static bool TryParseManifestFile(string manifestPath, out McPackManifest manifest, out bool isImporterStub)
        {
            manifest = new McPackManifest();
            isImporterStub = false;

            try
            {
                string text = File.ReadAllText(manifestPath);
                var parsed = JsonConvert.DeserializeObject<McPackManifest>(text);
                if (parsed == null)
                {
                    return false;
                }

                manifest = parsed;
                isImporterStub = IsImporterStubManifest(parsed);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool HasAtlasPayload(McPackManifest manifest) =>
            !string.IsNullOrWhiteSpace(manifest.AtlasRgb)
            || !string.IsNullOrWhiteSpace(manifest.AtlasBundle);
    }
}
