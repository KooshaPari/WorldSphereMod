using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

public sealed class McTexturePackImporterInvariantsTests
{
    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("repo root with WorldSphereMod.sln must be locatable from test cwd");
        return dir!.FullName;
    }

    static string ReadSource(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath);
        File.Exists(path).Should().BeTrue($"source file must exist at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Core_gates_texture_pack_import_on_EnableMcPackTextures()
    {
        var core = ReadSource(@"WorldSphereMod/Code/Core.cs");
        core.Should().Contain("savedSettings.EnableMcPackTextures");
        core.Should().Contain("TexturePackImporter.TryImportAtLoad");
        core.Should().Contain("TexturePackImporter.ImportAtLoad");
        core.Should().Contain("McPackLoader.Initialize(importResult.ManifestStubPath)");
    }

    [Fact]
    public void BridgeServer_exposes_texturepack_import_post_route()
    {
        var bridge = ReadSource(@"WorldSphereMod/Code/Bridge/BridgeServer.cs");
        bridge.Should().Contain("/texturepack/import");
        bridge.Should().Contain("BuildTexturePackImportPayload");
        bridge.Should().Contain("TexturePackImporter.BuildBridgeImportPayload");
    }

    [Fact]
    public void Importer_touchpoints_exist_per_spec()
    {
        var importer = ReadSource(@"WorldSphereMod/Code/Import/TexturePackImporter.cs");
        var registry = ReadSource(@"WorldSphereMod/Code/Import/TexturePackRegistry.cs");

        importer.Should().Contain("wsm3d\", \"texturepacks");
        importer.Should().Contain("pack.mcmeta");
        importer.Should().NotContain("McPackLoader.Initialize",
            "scan/validate importer stays Unity-free; Core binds baked atlases");
        registry.Should().Contain("grass_block_top");
        registry.Should().Contain("biome_grass");
    }

    [Fact]
    public void McPackLoader_binds_importer_manifest_stub_when_enabled()
    {
        var loader = ReadSource(@"WorldSphereMod/Code/Texture/McPackLoader.cs");
        loader.Should().Contain("Initialize(string? manifestStubPath");
        loader.Should().Contain("TryBindImporterStubManifest");
        loader.Should().Contain("EnableMcPackTextures");
        loader.Should().Contain("Texturepack manifest stub bound");
    }

    [Fact]
    public void SavedSettings_defaults_EnableMcPackTextures_to_false()
    {
        var settings = ReadSource(@"WorldSphereMod/Code/SavedSettings.cs");
        Regex.IsMatch(settings, @"public\s+bool\s+EnableMcPackTextures\s*=\s*false")
            .Should().BeTrue("McPack textures are opt-in to avoid bundle ID conflicts");
    }

    [Fact]
    public void Importer_manifest_stub_and_McPackLoader_noop_bind_contract_are_aligned()
    {
        var importer = ReadSource(@"WorldSphereMod/Code/Import/TexturePackImporter.cs");
        var loader = ReadSource(@"WorldSphereMod/Code/Texture/McPackLoader.cs");
        var manifestIo = ReadSource(@"WorldSphereMod/Code/Texture/McPackManifestIO.cs");

        importer.Should().Contain("ManifestImportStatusStub");
        importer.Should().Contain("import_status = ManifestImportStatusStub");
        importer.Should().Contain("atlas_rgb = string.Empty");
        importer.Should().Contain("atlas_width = 0");
        importer.Should().Contain("AtlasImportStubbed = true");
        importer.Should().Contain("EnumerateBlockTextureNames");

        manifestIo.Should().Contain("TexturePackImporter.ManifestImportStatusStub");
        manifestIo.Should().Contain("IsImporterStubManifest");

        loader.Should().Contain("TryBindImporterStubManifest");
        loader.Should().Contain("_isLoaded = false");
        loader.Should().Contain("_mainAtlas = null");
        loader.Should().Contain("if (material == null || !_isLoaded) return");
        loader.Should().Contain("McPackManifestIO.TryParseManifestFile");
        loader.Should().Contain("!isImporterStub");
    }
}
