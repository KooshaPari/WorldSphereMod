using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Xunit;

/// <summary>
/// Integration trace: manifest step slugs/IDs → Tools/wsm3d.ps1 subcommands (where mappable).
/// Grounded in docs/journeys/scratch/journey-integration-trace.md.
/// </summary>
public class JourneyIntegrationTraceTests
{
    private const string IndexRelative = "docs/journeys/manifests/index.json";
    private const string Wsm3dRelative = "Tools/wsm3d.ps1";

    private static string Wsm3dScript => TestRepo.ReadRelative(Wsm3dRelative);

    private static IReadOnlyList<(string Id, string ManifestPath)> LoadIndexedManifests()
    {
        var root = TestRepo.FindRoot();
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, IndexRelative)));

        return index.RootElement.EnumerateArray()
            .Select(entry =>
            {
                var id = entry.GetProperty("id").GetString()!;
                var file = entry.GetProperty("file").GetString()!;
                return (id, Path.Combine(root, "docs/journeys/manifests", file));
            })
            .ToList();
    }

    private static IReadOnlyList<(int Index, string Slug)> LoadManifestSteps(string manifestPath)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return manifest.RootElement.GetProperty("steps").EnumerateArray()
            .Select(step => (
                Index: step.GetProperty("index").GetInt32(),
                Slug: step.GetProperty("slug").GetString()!))
            .OrderBy(s => s.Index)
            .ToList();
    }

    [Fact]
    public void Wsm3d_resolves_indexed_manifest_ids_for_journey_capture_and_verify()
    {
        var script = Wsm3dScript;

        script.Should().Contain("docs/journeys/manifests/$Id/manifest.json");
        script.Should().Contain("function Invoke-JourneyCapture");
        script.Should().Contain("function Invoke-JourneyVerify");
        script.Should().Contain(@"""journey""");

        foreach (var (id, manifestPath) in LoadIndexedManifests())
        {
            File.Exists(manifestPath).Should().BeTrue($"index entry {id} must exist at {manifestPath}");

            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            manifest.RootElement.GetProperty("id").GetString().Should().Be(id);
        }

        script.Should().Contain("journey capture -Id");
        script.Should().Contain("journey verify -Id");
    }

    [Fact]
    public void Phase_manifests_use_canonical_step_slug_sequence_with_verify_terminal_step()
    {
        foreach (var (id, manifestPath) in LoadIndexedManifests().Where(m => JourneyWsm3dTraceCatalog.IsPhaseJourneyManifestId(m.Id)))
        {
            var steps = LoadManifestSteps(manifestPath);
            steps.Should().HaveCount(5, $"{id} ships the 5-step phase journey flow");

            var slugs = steps.Select(s => s.Slug).ToList();
            foreach (var canonical in JourneyWsm3dTraceCatalog.CanonicalStepSlugs)
            {
                slugs.Should().Contain(canonical, $"{id} must include step slug '{canonical}'");
            }

            slugs.Last().Should().StartWith("verify-", $"{id} must end with a verify-* slug");
            slugs.Where(JourneyWsm3dTraceCatalog.IsVerifySlug).Should().HaveCount(1);
        }
    }

    [Fact]
    public void Journey_capture_automates_screenshot_subcommand_for_every_manifest_step()
    {
        var script = Wsm3dScript;

        script.Should().Contain("$steps | ForEach-Object");
        script.Should().Contain("Invoke-Screenshot -Path $framePath");

        foreach (var (_, manifestPath) in LoadIndexedManifests())
        {
            foreach (var (_, slug) in LoadManifestSteps(manifestPath))
            {
                var trace = JourneyWsm3dTraceCatalog.ResolveStepTrace(slug);
                if (!trace.AutomatedInJourneyCapture)
                {
                    continue;
                }

                trace.Wsm3dSubcommands.Should().Contain("screenshot");
                JourneyWsm3dTraceCatalog.Wsm3dScriptExposesSubcommand(script, "screenshot").Should().BeTrue();
            }
        }
    }

    [Fact]
    public void Mappable_step_slugs_expose_matching_wsm3d_dispatcher_subcommands()
    {
        var script = Wsm3dScript;
        var checkedSubcommands = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, manifestPath) in LoadIndexedManifests())
        {
            foreach (var (_, slug) in LoadManifestSteps(manifestPath))
            {
                var trace = JourneyWsm3dTraceCatalog.ResolveStepTrace(slug);
                foreach (var subcommand in trace.Wsm3dSubcommands)
                {
                    if (!checkedSubcommands.Add(subcommand))
                    {
                        continue;
                    }

                    JourneyWsm3dTraceCatalog.Wsm3dScriptExposesSubcommand(script, subcommand)
                        .Should().BeTrue($"wsm3d.ps1 dispatcher must wire '{subcommand}' for slug '{slug}'");
                }

                if (trace.Wsm3dFunctionAnchor is not null)
                {
                    script.Should().Contain(
                        trace.Wsm3dFunctionAnchor,
                        $"slug '{slug}' should anchor on {trace.Wsm3dFunctionAnchor}");
                }
            }
        }
    }

    [Fact]
    public void Toggle_on_steps_map_to_toggle_phase_for_phases_with_saved_settings_keys()
    {
        var script = Wsm3dScript;

        script.Should().Contain("function Invoke-Toggle");
        script.Should().Contain("toggle -Phase");

        foreach (var (manifestId, phaseKey) in JourneyWsm3dTraceCatalog.ManifestIdToTogglePhaseKey)
        {
            script.Should().Contain($"\"{phaseKey}\"", $"PhaseMap/defaults must include {phaseKey} for {manifestId}");

            var manifestPath = LoadIndexedManifests().Single(m => m.Id == manifestId).ManifestPath;
            var toggleStep = LoadManifestSteps(manifestPath).Single(s => s.Slug == "toggle-on");
            toggleStep.Index.Should().Be(2, $"{manifestId} toggle-on remains step index 2");

            var trace = JourneyWsm3dTraceCatalog.ResolveStepTrace("toggle-on");
            trace.Wsm3dSubcommands.Should().Contain("toggle");
        }
    }

    [Fact]
    public void Phase_1_manifest_step_ids_trace_to_documented_wsm3d_subcommands()
    {
        const string phase1Id = "us-wsm-phase-1-voxel-actors";
        var script = Wsm3dScript;
        var manifestPath = LoadIndexedManifests().Single(m => m.Id == phase1Id).ManifestPath;

        var expected = new (int Index, string Slug, string[] Subcommands)[]
        {
            (0, "baseline", ["screenshot"]),
            (1, "open-settings", ["screenshot"]),
            (2, "toggle-on", ["toggle"]),
            (3, "reload-world", ["relaunch"]),
            (4, "verify-voxel", ["screenshot", "journey verify"]),
        };

        var steps = LoadManifestSteps(manifestPath);
        steps.Should().HaveCount(expected.Length);

        foreach (var row in expected)
        {
            var step = steps.Single(s => s.Index == row.Index);
            step.Slug.Should().Be(row.Slug);

            var trace = JourneyWsm3dTraceCatalog.ResolveStepTrace(row.Slug);
            trace.Wsm3dSubcommands.Should().BeEquivalentTo(row.Subcommands);

            foreach (var subcommand in row.Subcommands)
            {
                JourneyWsm3dTraceCatalog.Wsm3dScriptExposesSubcommand(script, subcommand)
                    .Should().BeTrue($"Phase 1 step {row.Index} ({row.Slug}) → {subcommand}");
            }
        }

        JourneyWsm3dTraceCatalog.ManifestIdToTogglePhaseKey[phase1Id].Should().Be("VoxelEntities");
        script.Should().Contain("journey verify -Id");
        script.Should().Contain("phenotype-journey");
    }

    [Fact]
    public void Manifest_ids_map_to_journey_capture_and_verify_Id_parameter()
    {
        var script = Wsm3dScript;

        foreach (var trace in LoadIndexedManifests().SelectMany(m => JourneyWsm3dTraceCatalog.ManifestLevelTraces(m.Id)))
        {
            foreach (var subcommand in trace.Wsm3dSubcommands)
            {
                JourneyWsm3dTraceCatalog.Wsm3dScriptExposesSubcommand(script, subcommand).Should().BeTrue();
            }
        }

        script.Should().Contain("-Id");
        script.Should().Contain("Invoke-JourneyVerify");
        script.Should().Contain("verify $manifestPath --mock");
    }

    [Fact]
    public void Smoke_test_manifests_use_before_after_closeup_screenshot_slugs()
    {
        var closeupByPhase = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["smoke-test-phase1"] = "buildings",
            ["smoke-test-phase2"] = "buildings",
            ["smoke-test-phase3"] = "foliage",
            ["smoke-test-phase4"] = "water",
            ["smoke-test-phase5"] = "shadows-sky",
            ["smoke-test-phase6"] = "skeletal",
            ["smoke-test-phase7"] = "ui",
            ["smoke-test-phase8"] = "day-night",
            ["smoke-test-phase9"] = "postfx",
            ["smoke-test-phase10"] = "lod",
        };

        foreach (var (id, closeup) in closeupByPhase)
        {
            var manifestPath = LoadIndexedManifests().Single(m => m.Id == id).ManifestPath;
            var steps = LoadManifestSteps(manifestPath);
            steps.Should().HaveCount(3, $"{id} documents three smoke-test comparison frames");
            steps.Select(s => s.Slug).Should().Equal(new[] { "before", "after", closeup });

            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var intent = manifest.RootElement.GetProperty("intent").GetString()!;
            intent.Should().Contain($"docs/{id}.md", $"{id} intent must link the human checklist doc");
        }
    }

    [Fact]
    public void Phase_10_toggle_on_is_not_mappable_to_wsm3d_toggle_phase()
    {
        const string phase10Id = "us-wsm-phase-10-lod-impostor";
        JourneyWsm3dTraceCatalog.ManifestIdToTogglePhaseKey.Should().NotContainKey(phase10Id);

        var script = Wsm3dScript;
        var steps = LoadManifestSteps(
            LoadIndexedManifests().Single(m => m.Id == phase10Id).ManifestPath);

        steps.Single(s => s.Slug == "toggle-on").Index.Should().Be(2);
        JourneyWsm3dTraceCatalog.ResolveStepTrace("toggle-on").Wsm3dSubcommands.Should().Contain("toggle");

        script.Should().NotContain("lod_impostor", "Phase 10 has no snake_case toggle entry in PhaseMap");
        script.Should().NotContain("LodImpostor", "Phase 10 has no camelCase toggle entry in PhaseMap");
    }
}
