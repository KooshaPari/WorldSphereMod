using System;
using System.IO;
using FluentAssertions;
using WorldSphereMod.Import;
using WorldSphereMod.Textures;
using Xunit;

public sealed class McPackLoaderManifestTests
{
    [Fact]
    public void IsImporterStubManifest_recognizes_texturepack_importer_stub_json()
    {
        var manifest = new McPackManifestIO.McPackManifest
        {
            ImportStatus = TexturePackImporter.ManifestImportStatusStub,
            AtlasRgb = string.Empty,
            PackName = "valid",
        };

        McPackManifestIO.IsImporterStubManifest(manifest).Should().BeTrue();
    }

    [Fact]
    public void IsImporterStubManifest_treats_empty_atlas_without_status_as_stub()
    {
        var manifest = new McPackManifestIO.McPackManifest
        {
            AtlasRgb = string.Empty,
            AtlasBundle = null,
        };

        McPackManifestIO.IsImporterStubManifest(manifest).Should().BeTrue();
    }

    [Fact]
    public void IsImporterStubManifest_rejects_baked_atlas_manifest()
    {
        var manifest = new McPackManifestIO.McPackManifest
        {
            ImportStatus = "imported",
            AtlasRgb = "atlas.png",
            AtlasWidth = 512,
            AtlasHeight = 512,
        };

        McPackManifestIO.IsImporterStubManifest(manifest).Should().BeFalse();
        McPackManifestIO.HasAtlasPayload(manifest).Should().BeTrue();
    }

    [Fact]
    public void TryParseManifestFile_reads_importer_stub_written_by_TryImportAtLoad()
    {
        string modConfig = CreateTempDir();
        string scanRoot = Path.Combine(modConfig, "wsm3d", "texturepacks");
        string cacheRoot = Path.Combine(modConfig, "wsm3d-texturepack");
        Directory.CreateDirectory(scanRoot);

        string validDir = Path.Combine(scanRoot, "faithful");
        Directory.CreateDirectory(validDir);
        File.WriteAllText(
            Path.Combine(validDir, TexturePackImporter.PackMetaFileName),
            @"{ ""pack"": { ""pack_format"": 34, ""description"": ""Faithful"" } }");

        string blockDir = Path.Combine(validDir, "assets", "minecraft", "textures", "block");
        Directory.CreateDirectory(blockDir);
        File.WriteAllBytes(Path.Combine(blockDir, "dirt.png"), Array.Empty<byte>());

        try
        {
            var importResult = TexturePackImporter.TryImportAtLoad(scanRoot, cacheRoot);
            importResult.ManifestStubPath.Should().NotBeNullOrWhiteSpace();

            McPackManifestIO.TryParseManifestFile(importResult.ManifestStubPath!, out var manifest, out bool isStub)
                .Should().BeTrue();
            isStub.Should().BeTrue();
            manifest.PackName.Should().Be("faithful");
            manifest.Format.Should().Be(TexturePackImporter.ManifestFormatVersion);
            manifest.ImportStatus.Should().Be(TexturePackImporter.ManifestImportStatusStub);
            manifest.Mappings.Should().Contain(m => m.McBlockName == "dirt" && m.Wsm3dClass == "biome_dirt");
        }
        finally
        {
            TryDeleteDir(modConfig);
        }
    }

    static string CreateTempDir() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "wsm3d-mcpack-" + Guid.NewGuid().ToString("N"))).FullName;

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
