using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Minimal preflight for the visual regression harness (see
/// docs/journeys/scratch/visual-regression-harness-design.md): screenshot path
/// conventions and the live journey capture checklist.
/// </summary>
public class VisualRegressionHarnessTests
{
    private const string ScreenshotsRelative = "docs/screenshots";
    private const string SmokeTestPhase1Relative = "docs/smoke-test-phase1.md";
    private const string RecordingRunbookRelative = "docs/journeys/recording-runbook.md";

    private static readonly string[] LiveCaptureChecklistBullets =
    {
        "WorldBox is open in the intended window size.",
        "The correct mod is enabled and the conflicting upstream mod is disabled.",
        "The game state matches the journey step you are about to capture.",
        "The file path you are saving to matches the manifest.",
        "The frame you just captured is legible before you continue.",
    };

    private static readonly Regex SmokeTestScreenshotNamePattern = new(
        @"`((?:phase-\d+-)?[a-z0-9][a-z0-9._-]*\.png)`",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void Docs_screenshots_directory_exists()
    {
        var root = TestRepo.FindRoot();
        Directory.Exists(Path.Combine(root, ScreenshotsRelative)).Should().BeTrue(ScreenshotsRelative);
    }

    [Fact]
    public void Smoke_test_phase1_documents_screenshot_paths_under_docs_screenshots()
    {
        var root = TestRepo.FindRoot();
        var screenshotsDir = Path.GetFullPath(Path.Combine(root, ScreenshotsRelative));
        var smokeTestPath = Path.Combine(root, SmokeTestPhase1Relative);
        File.Exists(smokeTestPath).Should().BeTrue(SmokeTestPhase1Relative);

        var text = File.ReadAllText(smokeTestPath);
        var captureSection = ExtractSection(text, "## Capture screenshots");
        captureSection.Should().NotBeNullOrWhiteSpace("smoke test must document capture targets");

        var names = SmokeTestScreenshotNamePattern.Matches(captureSection)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        names.Should().NotBeEmpty("smoke test must name at least one screenshot file");

        var invalid = new List<string>();
        foreach (var name in names)
        {
            var resolved = Path.GetFullPath(Path.Combine(screenshotsDir, name));
            if (!resolved.StartsWith(screenshotsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(resolved, screenshotsDir, StringComparison.OrdinalIgnoreCase))
            {
                invalid.Add($"{name} resolves outside {ScreenshotsRelative}: {resolved}");
                continue;
            }

            if (name.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(name))
            {
                invalid.Add($"{name} must be a simple file name under {ScreenshotsRelative}");
            }
        }

        invalid.Should().BeEmpty(string.Join("; ", invalid));
    }

    [Fact]
    public void Committed_pngs_under_docs_screenshots_are_non_empty()
    {
        var root = TestRepo.FindRoot();
        var screenshotsDir = Path.Combine(root, ScreenshotsRelative);
        if (!Directory.Exists(screenshotsDir))
        {
            return;
        }

        var empty = Directory.EnumerateFiles(screenshotsDir, "*.png", SearchOption.TopDirectoryOnly)
            .Where(path => new FileInfo(path).Length == 0)
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        empty.Should().BeEmpty("committed screenshot PNGs must not be empty: " + string.Join(", ", empty));
    }

    [Fact]
    public void Recording_runbook_includes_live_capture_checklist()
    {
        var root = TestRepo.FindRoot();
        var runbookPath = Path.Combine(root, RecordingRunbookRelative);
        File.Exists(runbookPath).Should().BeTrue(RecordingRunbookRelative);

        var text = File.ReadAllText(runbookPath);
        text.Should().Contain("## Live capture checklist");

        var checklistSection = ExtractSection(text, "## Live capture checklist");
        checklistSection.Should().NotBeNullOrWhiteSpace();

        var missing = LiveCaptureChecklistBullets
            .Where(bullet => !checklistSection.Contains(bullet, StringComparison.Ordinal))
            .ToList();

        missing.Should().BeEmpty(
            "recording runbook live capture checklist is incomplete: " + string.Join("; ", missing));
    }

    private static string ExtractSection(string markdown, string heading)
    {
        var start = markdown.IndexOf(heading, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        var bodyStart = markdown.IndexOf('\n', start);
        if (bodyStart < 0)
        {
            return string.Empty;
        }

        bodyStart++;
        var nextHeading = markdown.IndexOf("\n## ", bodyStart, StringComparison.Ordinal);
        return nextHeading < 0
            ? markdown[bodyStart..]
            : markdown[bodyStart..nextHeading];
    }
}
