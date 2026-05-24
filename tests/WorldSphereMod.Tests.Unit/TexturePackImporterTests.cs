using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
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
    public void TryImportAtLoad_selects_first_valid_candidate_and_reports_stub_message()
    {
        string modConfig = CreateTempDir();
        string scanRoot = Path.Combine(modConfig, "wsm3d", "texturepacks");
        Directory.CreateDirectory(scanRoot);

        string invalidDir = Path.Combine(scanRoot, "invalid");
        Directory.CreateDirectory(invalidDir);

        string validDir = Path.Combine(scanRoot, "valid");
        Directory.CreateDirectory(validDir);
        File.WriteAllText(
            Path.Combine(validDir, TexturePackImporter.PackMetaFileName),
            @"{ ""pack"": { ""pack_format"": 34, ""description"": ""First valid"" } }");

        try
        {
            var result = TexturePackImporter.TryImportAtLoad(scanRoot, Path.Combine(modConfig, "wsm3d-texturepack"));

            result.ScanRootExists.Should().BeTrue();
            result.ScannedPaths.Should().HaveCount(2);
            result.ValidatedPaths.Should().ContainSingle().Which.Should().Be(validDir);
            result.SelectedPackPath.Should().Be(validDir);
            result.SelectedPackMeta!.PackFormat.Should().Be(34);
            result.AtlasImportStubbed.Should().BeTrue();
            result.Message.Should().Be("validated_stub_atlas_import_pending");
            result.KnownMappingCount.Should().Be(11);
        }
        finally
        {
            TryDeleteDir(modConfig);
        }
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
