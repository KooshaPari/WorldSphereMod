using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Validates markdown journey docs under docs/journeys/ reference on-disk targets where checkable.
/// </summary>
public class JourneyMarkdownDocPathTests
{
    private static readonly Regex MarkdownLinkPattern = new(
        @"\[[^\]]*\]\(([^)]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] TopLevelJourneyDocs =
    {
        "docs/journeys/README.md",
        "docs/journeys/CONTRIBUTING.md",
        "docs/journeys/recording-runbook.md",
        "docs/journeys/install-and-play.md",
        "docs/journeys/diagnose-perf.md",
        "docs/journeys/upgrade-from-upstream.md",
        "docs/journeys/extend-via-api.md",
        "docs/journeys/contribute-a-phase.md",
    };

    [Fact]
    public void Journey_markdown_docs_link_to_existing_markdown_targets_where_checkable()
    {
        var root = TestRepo.FindRoot();
        var missing = new List<string>();

        foreach (var relativeDoc in TopLevelJourneyDocs)
        {
            var docPath = Path.Combine(root, relativeDoc);
            File.Exists(docPath).Should().BeTrue(relativeDoc);

            var docDir = Path.GetDirectoryName(docPath)!;
            var text = File.ReadAllText(docPath);

            foreach (Match match in MarkdownLinkPattern.Matches(text))
            {
                var target = match.Groups[1].Value.Trim();
                if (!IsCheckableMarkdownTarget(target))
                {
                    continue;
                }

                var resolved = ResolveMarkdownTarget(root, docDir, target);
                if (!File.Exists(resolved))
                {
                    missing.Add($"{relativeDoc} -> {target} (resolved {resolved})");
                }
            }
        }

        missing.Should().BeEmpty(
            "journey markdown should only link to committed docs that exist: " + string.Join("; ", missing));
    }

    private static bool IsCheckableMarkdownTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target)
            || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var pathOnly = target.Split('#')[0];
        if (string.IsNullOrWhiteSpace(pathOnly))
        {
            return false;
        }

        return pathOnly.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveMarkdownTarget(string repoRoot, string docDir, string target)
    {
        var pathOnly = target.Split('#')[0];
        var normalized = pathOnly.Replace('\\', '/');

        if (normalized.StartsWith("docs/", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(Path.Combine(repoRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        }

        if (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            var vitepressRelative = normalized.TrimStart('/');
            if (!vitepressRelative.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                vitepressRelative += ".md";
            }

            return Path.GetFullPath(Path.Combine(repoRoot, "docs", vitepressRelative.Replace('/', Path.DirectorySeparatorChar)));
        }

        return Path.GetFullPath(Path.Combine(docDir, pathOnly.Replace('/', Path.DirectorySeparatorChar)));
    }
}
