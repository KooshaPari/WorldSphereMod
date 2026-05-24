using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Guards docs/HANDOFF.md and README phase-table drift against SavedSettings.cs.
/// </summary>
public class HandoffDefaultsAlignmentTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("repo root with WorldSphereMod.sln must be locatable from test cwd");
        return dir!.FullName;
    }

    private static string ReadRepoFile(string root, params string[] segments)
    {
        var path = Path.Combine(new[] { root }.Concat(segments).ToArray());
        File.Exists(path).Should().BeTrue($"{string.Join('/', segments)} must exist at {path}");
        return File.ReadAllText(path);
    }

    private static Dictionary<string, string> ParseSavedSettingsDefaults(string source)
    {
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(
                     source,
                     @"public\s+(bool|float)\s+(\w+)\s*=\s*([^;]+);"))
        {
            defaults[match.Groups[2].Value] = match.Groups[3].Value.Trim();
        }

        foreach (Match match in Regex.Matches(
                     source,
                     @"public\s+\w+\s+(\w+)\s*=\s*(?:\w+\.)?(\w+)\s*;"))
        {
            if (!defaults.ContainsKey(match.Groups[1].Value))
            {
                defaults[match.Groups[1].Value] = match.Groups[2].Value;
            }
        }

        return defaults;
    }

    private static Dictionary<string, string> ParseHandoffDefaultsMatrix(string handoff)
    {
        var start = handoff.IndexOf("## Current defaults matrix", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "HANDOFF.md must contain a Current defaults matrix section");

        var end = handoff.IndexOf("## Current defaults by category", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "HANDOFF.md must contain a Current defaults by category section");

        var section = handoff.Substring(start, end - start);
        var matrix = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(
                     section,
                     @"\|\s*`(\w+)`\s*\|\s*`([^`]+)`\s*\|"))
        {
            matrix[match.Groups[1].Value] = match.Groups[2].Value;
        }

        matrix.Should().NotBeEmpty("HANDOFF defaults matrix must list at least one setting");
        return matrix;
    }

    private static IEnumerable<string> ParseHandoffCategoryFieldNames(string handoff, string categoryHeading)
    {
        var heading = $"### {categoryHeading}";
        var start = handoff.IndexOf(heading, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"HANDOFF.md must contain {heading}");

        var nextHeading = handoff.IndexOf("\n### ", start + heading.Length, StringComparison.Ordinal);
        var section = nextHeading < 0
            ? handoff.Substring(start)
            : handoff.Substring(start, nextHeading - start);

        foreach (Match match in Regex.Matches(section, @"- `(\w+)`"))
        {
            yield return match.Groups[1].Value;
        }
    }

    [Fact]
    public void Handoff_defaults_matrix_matches_SavedSettings_cs()
    {
        var root = FindRepoRoot();
        var savedSettings = ReadRepoFile(root, "WorldSphereMod", "Code", "SavedSettings.cs");
        var handoff = ReadRepoFile(root, "docs", "HANDOFF.md");

        var codeDefaults = ParseSavedSettingsDefaults(savedSettings);
        var handoffMatrix = ParseHandoffDefaultsMatrix(handoff);

        foreach (var (field, documented) in handoffMatrix)
        {
            codeDefaults.Should().ContainKey(
                field,
                $"SavedSettings.cs must declare {field} because HANDOFF documents it");

            var codeValue = codeDefaults[field];
            if (documented is "true" or "false")
            {
                codeValue.Should().Be(
                    documented,
                    $"HANDOFF matrix default for {field} must match SavedSettings.cs");
                continue;
            }

            if (documented.EndsWith("f", StringComparison.Ordinal))
            {
                codeValue.Should().Be(
                    documented,
                    $"HANDOFF matrix default for {field} must match SavedSettings.cs");
                continue;
            }

            codeValue.Should().Be(
                documented,
                $"HANDOFF matrix default for {field} must match SavedSettings.cs");
        }
    }

    [Fact]
    public void Handoff_default_off_category_excludes_default_on_phase_flags()
    {
        var root = FindRepoRoot();
        var savedSettings = ReadRepoFile(root, "WorldSphereMod", "Code", "SavedSettings.cs");
        var handoff = ReadRepoFile(root, "docs", "HANDOFF.md");

        var codeDefaults = ParseSavedSettingsDefaults(savedSettings);
        var defaultOff = ParseHandoffCategoryFieldNames(handoff, "Default-off / opt-in").ToList();

        foreach (var field in defaultOff)
        {
            if (!codeDefaults.TryGetValue(field, out var value))
            {
                continue;
            }

            if (value is "true" or "false")
            {
                value.Should().Be(
                    "false",
                    $"{field} is default-on in SavedSettings.cs and must not appear under Default-off in HANDOFF.md");
            }
        }

        defaultOff.Should().Contain(
            "MeshWater",
            "MeshWater defaults to false in SavedSettings.cs and must be listed under Default-off in HANDOFF.md");
    }

    [Fact]
    public void Handoff_default_off_category_includes_MeshWater()
    {
        var root = FindRepoRoot();
        var handoff = ReadRepoFile(root, "docs", "HANDOFF.md");

        var defaultOff = ParseHandoffCategoryFieldNames(handoff, "Default-off / opt-in").ToList();
        defaultOff.Should().Contain(
            "MeshWater",
            "MeshWater is default-off in SavedSettings.cs and must be listed under Default-off in HANDOFF.md");
    }

    [Fact]
    public void Handoff_and_live_verification_docs_distinguish_playcua_capture_from_visual_proof()
    {
        var root = FindRepoRoot();
        var handoff = ReadRepoFile(root, "docs", "HANDOFF.md");
        var liveVerification = ReadRepoFile(root, "docs", "live-verification.md");

        handoff.Should().Contain("PlayCUA capture + telemetry passed for `ProceduralBuildings`");
        handoff.Should().Contain("Visual/vision approval and strict journey capture remain separate proof");
        handoff.Should().Contain("PlayCUA capture + telemetry passed for `CloudCrossedQuadRender`");
        handoff.Should().NotContain("GET /health returns 200 body null");
        handoff.Should().NotContain("returns `200` with body `null`");
        handoff.Should().NotContain("body `null`");
        handoff.Should().NotContain("Bridge health check failed");
        handoff.Should().Contain("bridge-health-vision and bridge-save-load-smoke");
        handoff.Should().Contain("Full `-Live -Vision`, strict journey capture, and a true `load_save` transition remain open/manual/partial");

        liveVerification.Should().Contain("pre/post `health` + telemetry passed");
        liveVerification.Should().Contain("load_save` transition remains skipped/partial");
        liveVerification.Should().Contain("non-dict response: null");
    }

    [Theory]
    [InlineData(1, "VoxelEntities", "true")]
    [InlineData(2, "ProceduralBuildings", "false")]
    [InlineData(4, "MeshWater", "false")]
    [InlineData(8, "DayNightCycle", "false")]
    public void Readme_phase_table_documents_saved_settings_default(
        int phase,
        string flag,
        string expectedLiteral)
    {
        var root = FindRepoRoot();
        var savedSettings = ReadRepoFile(root, "WorldSphereMod", "Code", "SavedSettings.cs");
        var readme = ReadRepoFile(root, "README.md");

        var codeDefaults = ParseSavedSettingsDefaults(savedSettings);
        codeDefaults.Should().ContainKey(flag);
        codeDefaults[flag].Should().Be(expectedLiteral);

        var phaseRow = readme
            .Split('\n')
            .FirstOrDefault(line => Regex.IsMatch(line, $@"^\|\s*{phase}\s+\|"));

        phaseRow.Should().NotBeNull($"README phase table must include a Phase {phase} row");
        var expectedStatus = expectedLiteral == "true" ? "default ON" : "default OFF";
        phaseRow!.Should().Contain(expectedStatus, $"README Phase {phase} row must document default status");
        phaseRow.Should().Contain(
            $"{flag} = {expectedLiteral}",
            $"README Phase {phase} row must cite the live SavedSettings default");
    }
}
