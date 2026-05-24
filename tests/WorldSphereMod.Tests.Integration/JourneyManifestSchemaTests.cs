using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Xunit;

public class JourneyManifestSchemaTests
{
    private const string SchemaRelative = "Tools/journey-records/schema.json";
    private const string IndexRelative = "docs/journeys/manifests/index.json";

    [Fact]
    public void Journey_manifest_schema_file_exists_and_is_valid_json_schema()
    {
        var schemaText = TestRepo.ReadRelative(SchemaRelative);
        var parse = () => JsonDocument.Parse(schemaText);
        parse.Should().NotThrow();

        using var schema = JsonDocument.Parse(schemaText);
        var root = schema.RootElement;

        root.GetProperty("$schema").GetString().Should().Contain("json-schema.org");
        root.GetProperty("type").GetString().Should().Be("object");
        root.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString())
            .Should().Contain(new[] { "id", "intent" });
        root.TryGetProperty("$defs", out var defs).Should().BeTrue();
        defs.TryGetProperty("Step", out _).Should().BeTrue();
        defs.TryGetProperty("StepAssertions", out _).Should().BeTrue();
    }

    [Fact]
    public void Manifest_index_lists_every_phase_manifest()
    {
        var root = TestRepo.FindRoot();
        var indexPath = Path.Combine(root, IndexRelative);
        File.Exists(indexPath).Should().BeTrue();

        using var index = JsonDocument.Parse(File.ReadAllText(indexPath));
        index.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        var entries = index.RootElement.EnumerateArray().ToList();
        entries.Should().HaveCount(10, "phases 1-10 each ship a journey manifest");

        foreach (var entry in entries)
        {
            entry.GetProperty("id").GetString().Should().NotBeNullOrWhiteSpace();
            entry.GetProperty("intent").GetString().Should().NotBeNullOrWhiteSpace();
            var file = entry.GetProperty("file").GetString()!;
            var manifestPath = Path.Combine(root, "docs/journeys/manifests", file);
            File.Exists(manifestPath).Should().BeTrue($"index entry must point at {manifestPath}");
        }
    }

    [Fact]
    public void Indexed_manifests_conform_to_required_schema_fields()
    {
        var root = TestRepo.FindRoot();
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, IndexRelative)));

        foreach (var entry in index.RootElement.EnumerateArray())
        {
            var id = entry.GetProperty("id").GetString()!;
            var manifestPath = Path.Combine(root, "docs/journeys/manifests", entry.GetProperty("file").GetString()!);
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var rootEl = manifest.RootElement;

            rootEl.GetProperty("id").GetString().Should().Be(id);
            rootEl.GetProperty("intent").GetString().Should().NotBeNullOrWhiteSpace();
            rootEl.TryGetProperty("steps", out var steps).Should().BeTrue();
            steps.GetArrayLength().Should().BeGreaterThan(0);

            var seenIndexes = new HashSet<int>();
            foreach (var step in steps.EnumerateArray())
            {
                step.GetProperty("slug").GetString().Should().NotBeNullOrWhiteSpace();
                step.GetProperty("intent").GetString().Should().NotBeNullOrWhiteSpace();
                step.GetProperty("screenshot_path").GetString().Should().NotBeNullOrWhiteSpace();

                var indexValue = step.GetProperty("index").GetInt32();
                seenIndexes.Add(indexValue).Should().BeTrue($"duplicate step index {indexValue} in {id}");
            }

            seenIndexes.Should().Contain(0, $"{id} must include a step at index 0");
        }
    }

    [Fact]
    public void Every_manifest_directory_has_on_disk_json()
    {
        var manifestsDir = Path.Combine(TestRepo.FindRoot(), "docs/journeys/manifests");
        var manifestFiles = Directory.GetDirectories(manifestsDir)
            .Select(dir => Path.Combine(dir, "manifest.json"))
            .Where(File.Exists)
            .Select(dir => Path.GetFileName(Path.GetDirectoryName(dir))!)
            .OrderBy(name => name)
            .ToArray();

        manifestFiles.Should().HaveCount(10);
        manifestFiles.Should().OnlyContain(name => name.StartsWith("us-wsm-phase-", StringComparison.Ordinal));
    }

    [Fact]
    public void Manifest_steps_reference_existing_doc_paths_where_checkable()
    {
        var root = TestRepo.FindRoot();
        var missing = new List<string>();
        var unsafePaths = new List<string>();

        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, IndexRelative)));
        foreach (var entry in index.RootElement.EnumerateArray())
        {
            var id = entry.GetProperty("id").GetString()!;
            var manifestPath = Path.Combine(root, "docs/journeys/manifests", entry.GetProperty("file").GetString()!);
            var manifestDir = Path.GetDirectoryName(manifestPath)!;

            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var rootEl = manifest.RootElement;

            AssertCheckablePaths(root, manifestDir, id, "recording", rootEl, missing, unsafePaths);
            AssertCheckablePaths(root, manifestDir, id, "recording_gif", rootEl, missing, unsafePaths);

            if (!rootEl.TryGetProperty("steps", out var steps))
            {
                continue;
            }

            foreach (var step in steps.EnumerateArray())
            {
                var stepIndex = step.TryGetProperty("index", out var indexEl) ? indexEl.GetInt32() : -1;
                var context = $"{id} step {stepIndex}";

                AssertCheckablePaths(root, manifestDir, context, "screenshot_path", step, missing, unsafePaths);
                AssertCheckablePaths(root, manifestDir, context, "doc_path", step, missing, unsafePaths);
                AssertCheckablePaths(root, manifestDir, context, "description", step, missing, unsafePaths);

                if (step.TryGetProperty("intent", out var intent))
                {
                    foreach (var embedded in JourneyManifestPathValidation.ExtractEmbeddedDocPaths(intent.GetString()))
                    {
                        AssertResolvedPath(root, manifestDir, context, embedded, missing, unsafePaths);
                    }
                }
            }
        }

        unsafePaths.Should().BeEmpty("manifest paths must stay relative and inside the repo: " + string.Join("; ", unsafePaths));
        missing.Should().BeEmpty("checkable doc paths must exist on disk: " + string.Join("; ", missing));
    }

    [Fact]
    public void Journey_records_schema_matches_repo_copy()
    {
        var root = TestRepo.FindRoot();
        var schemaPath = Path.Combine(root, SchemaRelative);
        var readmePath = Path.Combine(root, "Tools/journey-records/README.md");

        File.Exists(schemaPath).Should().BeTrue();
        File.Exists(readmePath).Should().BeTrue();

        var readme = File.ReadAllText(readmePath);
        readme.Should().Contain("schema.json", "journey-records README should document the schema file");
        readme.Should().Contain("--strict-assets", "journey-records README should document strict asset validation");
    }

    private static void AssertCheckablePaths(
        string repoRoot,
        string manifestDir,
        string context,
        string propertyName,
        JsonElement container,
        List<string> missing,
        List<string> unsafePaths)
    {
        if (!container.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var path = value.GetString();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        AssertResolvedPath(repoRoot, manifestDir, $"{context}.{propertyName}", path, missing, unsafePaths);
    }

    private static void AssertResolvedPath(
        string repoRoot,
        string manifestDir,
        string context,
        string path,
        List<string> missing,
        List<string> unsafePaths)
    {
        if (JourneyManifestPathValidation.IsUnsafePath(path))
        {
            unsafePaths.Add($"{context}: {path}");
            return;
        }

        if (!JourneyManifestPathValidation.ShouldExistOnDisk(path))
        {
            return;
        }

        var resolved = JourneyManifestPathValidation.ResolvePath(repoRoot, manifestDir, path);
        if (!File.Exists(resolved) && !Directory.Exists(resolved))
        {
            missing.Add($"{context}: {path} -> {resolved}");
        }
    }
}
