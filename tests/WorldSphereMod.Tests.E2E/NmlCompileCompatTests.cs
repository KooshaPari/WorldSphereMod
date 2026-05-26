using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

public class NmlCompileCompatTests
{
    // NML's embedded Roslyn rejects a few patterns that net48/msbuild accepts.
    // Keep this list curated from Player.log CS errors — avoid broad regex scans.
    private static readonly (string Snippet, string Reason)[] KnownTraps =
    {
    };

    private static readonly string[] ExcludedDirectoryMarkers =
    {
        $"{Path.DirectorySeparatorChar}WorldSphereAPI{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}WorldSphereTester{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}External{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}Tools{Path.DirectorySeparatorChar}",
    };

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

    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                ExcludedDirectoryMarkers.All(marker => !path.Contains(marker, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Source_does_not_contain_common_nml_roslyn_compile_traps()
    {
        var root = FindRepoRoot();
        var offenders = new List<string>();

        foreach (var file in EnumerateSourceFiles(root))
        {
            var relativePath = Path.GetRelativePath(root, file);

            foreach (var line in File.ReadLines(file).Select((text, index) => (text, index)))
            {
                foreach (var (snippet, reason) in KnownTraps)
                {
                    if (line.text.Contains(snippet, StringComparison.Ordinal))
                    {
                        offenders.Add($"{relativePath}:{line.index + 1}: {reason} ({snippet})");
                    }
                }
            }
        }

        offenders.Should().BeEmpty("NML Roslyn compilation must not hit known parse/semantic traps");
    }
}
