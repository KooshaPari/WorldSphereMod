using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

public class NmlCompileCompatTests
{
    private static readonly Regex SuspiciousLengthRegex = new(
        @"(?<!\bstring\b)(?<!\bString\b)(?<!\])\.\s*Length\b",
        RegexOptions.Compiled);

    private static readonly Regex SuspiciousMethodOperandRegex = new(
        @"\.\s*[A-Za-z_]\w*\b(?!\s*\()(?=\s*(?:[+\-*/%&|^!<>=?,):;]|\|\|?|\&\&?))",
        RegexOptions.Compiled);

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
                !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Source_does_not_contain_common_nml_roslyn_compile_traps()
    {
        var root = FindRepoRoot();
        var offenders = new List<string>();

        foreach (var file in EnumerateSourceFiles(root))
        {
            var source = File.ReadAllText(file);

            foreach (Match match in SuspiciousLengthRegex.Matches(source))
            {
                offenders.Add($"{Path.GetRelativePath(root, file)}: suspicious .Length usage at index {match.Index}");
            }

            foreach (Match match in SuspiciousMethodOperandRegex.Matches(source))
            {
                offenders.Add($"{Path.GetRelativePath(root, file)}: suspicious method operand without () at index {match.Index}");
            }
        }

        offenders.Should().BeEmpty("NML Roslyn compilation must not hit known parse/semantic traps");
    }
}
