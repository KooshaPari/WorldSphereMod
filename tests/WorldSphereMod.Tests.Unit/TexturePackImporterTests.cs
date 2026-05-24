using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using WorldSphereMod.Import;
using Xunit;

public sealed class TexturePackImporterTests
{
    [Fact]
    public void TexturePackRegistry_maps_spec_example_blocks()
    {
        TexturePackRegistry.TryGetWsm3dClass("grass_block_top", out var grassTop).Should().BeTrue();
        grassTop.Should().Be("biome_grass");

        TexturePackRegistry.TryGetWsm3dClass("water_flow", out var water).Should().BeTrue();
        water.Should().Be("water_surface");

        TexturePackRegistry.DefaultMappings.Should().HaveCount(11);
    }

    [Fact]
    public void ScanCandidates_finds_zip_and_directory_entries()
    {
        string root = CreateTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "pack-a.zip"), Array.Empty<byte>());
            Directory.CreateDirectory(Path.Combine(root, "pack-b"));

            var candidates = TexturePackImporter.ScanCandidates(root);
            candidates.Select(c => Path.GetFileName(c.SourcePath)).Should().BeEquivalentTo("pack-a.zip", "pack-b");
            candidates.Single(c => c.SourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).IsZip.Should().BeTrue();
            candidates.Single(c => !c.SourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).IsZip.Should().BeFalse();
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void TryReadPackMeta_accepts_folder_pack_and_rejects_missing_meta()
    {
        string root = CreateTempDir();
        try
        {
            string goodDir = Path.Combine(root, "good");
            Directory.CreateDirectory(goodDir);
            File.WriteAllText(
                Path.Combine(goodDir, TexturePackImporter.PackMetaFileName),
                @"{ ""pack"": { ""pack_format"": 34, ""description"": ""Test pack"" } }");

            TexturePackImporter.TryReadPackMeta(goodDir, isZip: false, out var meta, out var error)
                .Should().BeTrue(error);
            meta.PackFormat.Should().Be(34);
            meta.Description.Should().Contain("Test pack");

            string badDir = Path.Combine(root, "bad");
            Directory.CreateDirectory(badDir);
            TexturePackImporter.TryReadPackMeta(badDir, isZip: false, out _, out error)
                .Should().BeFalse();
            error.Should().Be("missing_pack_mcmeta");
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void TryReadPackMeta_reads_pack_mcmeta_from_zip()
    {
        string root = CreateTempDir();
        try
        {
            string zipPath = Path.Combine(root, "faithful.zip");
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry(TexturePackImporter.PackMetaFileName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(@"{ ""pack"": { ""pack_format"": 15, ""description"": ""Zip pack"" } }");
            }

            TexturePackImporter.TryReadPackMeta(zipPath, isZip: true, out var meta, out var error)
                .Should().BeTrue(error);
            meta.PackFormat.Should().Be(15);
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void EnumerateBlockTextureNames_finds_pngs_in_folder_pack()
    {
        string root = CreateTempDir();
        try
        {
            string blockDir = Path.Combine(root, "assets", "minecraft", "textures", "block");
            Directory.CreateDirectory(blockDir);
            File.WriteAllBytes(Path.Combine(blockDir, "dirt.png"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(blockDir, "stone.png"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(blockDir, "custom_leaf.png"), Array.Empty<byte>());

            TexturePackImporter.EnumerateBlockTextureNames(root, isZip: false)
                .Should().BeEquivalentTo("custom_leaf", "dirt", "stone");
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void EnumerateBlockTextureNames_finds_pngs_in_zip_pack()
    {
        string root = CreateTempDir();
        try
        {
            string zipPath = Path.Combine(root, "pack.zip");
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                WriteZipBytes(archive, "assets/minecraft/textures/block/grass_block_top.png", Array.Empty<byte>());
                WriteZipBytes(archive, "assets/minecraft/textures/block/models/.gitkeep", Array.Empty<byte>());
                WriteZipBytes(archive, "assets/minecraft/textures/item/dirt.png", Array.Empty<byte>());
            }

            TexturePackImporter.EnumerateBlockTextureNames(zipPath, isZip: true)
                .Should().ContainSingle()
                .Which.Should().Be("grass_block_top");
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void BuildManifestStubJson_lists_known_mappings_and_block_inventory()
    {
        var blockNames = new[] { "dirt", "stone", "unknown_block" };
        var meta = new TexturePackImporter.PackMetaInfo { PackFormat = 34, Description = "Stub pack" };

        string json = TexturePackImporter.BuildManifestStubJson(
            @"C:\packs\faithful",
            isZip: false,
            meta,
            blockNames);

        var root = JObject.Parse(json);
        root["format"]!.Value<string>().Should().Be(TexturePackImporter.ManifestFormatVersion);
        root["import_status"]!.Value<string>().Should().Be(TexturePackImporter.ManifestImportStatusStub);
        root["block_textures_found"]!.Value<int>().Should().Be(3);
        root["atlas_width"]!.Value<int>().Should().Be(0);
        root["mappings"]!.Should().HaveCount(2);
        root["mappings"]![0]!["mc_block_name"]!.Value<string>().Should().Be("dirt");
        root["mappings"]![0]!["wsm3d_class"]!.Value<string>().Should().Be("biome_dirt");
    }

    [Fact]
    public void TryImportAtLoad_enumerates_blocks_writes_manifest_stub_and_logs()
    {
        string modConfig = CreateTempDir();
        string scanRoot = Path.Combine(modConfig, "wsm3d", "texturepacks");
        string cacheRoot = Path.Combine(modConfig, "wsm3d-texturepack");
        Directory.CreateDirectory(scanRoot);

        string invalidDir = Path.Combine(scanRoot, "invalid");
        Directory.CreateDirectory(invalidDir);

        string validDir = Path.Combine(scanRoot, "valid");
        Directory.CreateDirectory(validDir);
        File.WriteAllText(
            Path.Combine(validDir, TexturePackImporter.PackMetaFileName),
            @"{ ""pack"": { ""pack_format"": 34, ""description"": ""First valid"" } }");

        string blockDir = Path.Combine(validDir, "assets", "minecraft", "textures", "block");
        Directory.CreateDirectory(blockDir);
        File.WriteAllBytes(Path.Combine(blockDir, "dirt.png"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(blockDir, "stone.png"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(blockDir, "grass_block_top.png"), Array.Empty<byte>());

        var logLines = new List<string>();
        try
        {
            var result = TexturePackImporter.TryImportAtLoad(scanRoot, cacheRoot, logLines.Add);

            result.ScanRootExists.Should().BeTrue();
            result.ScannedPaths.Should().HaveCount(2);
            result.ValidatedPaths.Should().ContainSingle().Which.Should().Be(validDir);
            result.SelectedPackPath.Should().Be(validDir);
            result.SelectedPackMeta!.PackFormat.Should().Be(34);
            result.BlockTextureNames.Should().BeEquivalentTo("dirt", "grass_block_top", "stone");
            result.KnownMappedInPackCount.Should().Be(3);
            result.AtlasImportStubbed.Should().BeTrue();
            result.Message.Should().Be("enumerated_manifest_stub_written");
            result.KnownMappingCount.Should().Be(11);
            result.ManifestStubPath.Should().NotBeNullOrWhiteSpace();
            File.Exists(result.ManifestStubPath!).Should().BeTrue();

            var manifest = JObject.Parse(File.ReadAllText(result.ManifestStubPath!));
            manifest["import_status"]!.Value<string>().Should().Be(TexturePackImporter.ManifestImportStatusStub);
            manifest["mappings"]!.Should().HaveCount(3);

            logLines.Should().Contain(line => line.Contains("manifest stub"));
        }
        finally
        {
            TryDeleteDir(modConfig);
        }
    }

    static void WriteZipBytes(ZipArchive archive, string entryName, byte[] bytes)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    static string CreateTempDir() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "wsm3d-texpack-" + Guid.NewGuid().ToString("N"))).FullName;

    static void TryDeleteDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup for temp dirs
        }
    }
}
