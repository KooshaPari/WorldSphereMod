using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Guards that README.md documents every phase (0–10) defined in root PLAN.md.
/// </summary>
public class PlanReadmePhaseCoverageTests
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

    private static IReadOnlyList<int> ParsePlanPhaseNumbers(string planMarkdown)
    {
        var phases = new SortedSet<int>();
        foreach (Match match in Regex.Matches(planMarkdown, @"^### Phase (\d+)\b", RegexOptions.Multiline))
        {
            phases.Add(int.Parse(match.Groups[1].Value));
        }

        phases.Should().NotBeEmpty("PLAN.md must declare at least one ### Phase N heading");
        return phases.ToList();
    }

    [Fact]
    public void Readme_mentions_every_phase_from_root_PLAN_md()
    {
        var root = FindRepoRoot();
        var plan = ReadRepoFile(root, "PLAN.md");
        var readme = ReadRepoFile(root, "README.md");

        var phases = ParsePlanPhaseNumbers(plan);
        phases.Should().BeEquivalentTo(
            Enumerable.Range(0, 11),
            "root PLAN.md is expected to define phases 0 through 10 for this fork");

        readme.Should().Contain(
            "docs/PLAN.md",
            "README must link to the canonical plan (docs/PLAN.md pointer or root PLAN.md)");

        foreach (var phase in phases)
        {
            var phaseTableRow = readme
                .Split('\n')
                .FirstOrDefault(line => Regex.IsMatch(line, $@"^\|\s*{phase}\s+\|"));

            phaseTableRow.Should().NotBeNull(
                $"README.md phase table must include a row for Phase {phase} (from PLAN.md)");
        }
    }
}
