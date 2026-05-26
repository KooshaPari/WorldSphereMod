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
        @"(?<![\w.])(?:[A-Za-z_]\w*)\b(?!\?)(?!\s*(?:<[^>]*>\s*)?\()(?=\s*(?:[+\-*/%&|^!<>=?,):;]|\|\|?|\&\&?))",
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

    private static readonly Regex FieldOrLocalDeclarationRegex = new(
        @"^[\w.?<>\[\],\s]+\s+\w+\s*;$",
        RegexOptions.Compiled);

    private static bool LooksLikeExpressionLine(string line)
    {
        var trimmed = line.TrimStart();

        if (trimmed.StartsWith("using ", StringComparison.Ordinal) ||
            trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        var statement = trimmed.TrimEnd();
        if (statement.EndsWith(";", StringComparison.Ordinal) &&
            !statement.Contains('(', StringComparison.Ordinal) &&
            !statement.Contains('=', StringComparison.Ordinal) &&
            FieldOrLocalDeclarationRegex.IsMatch(statement))
        {
            return false;
        }

        return line.IndexOfAny(new[] { '+', '-', '*', '/', '%', '&', '|', '^', '=', '?', ':', '<', '>', '!' }) >= 0;
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
                if (!LooksLikeExpressionLine(line.text))
                {
                    continue;
                }

                foreach (Match match in SuspiciousLengthRegex.Matches(line.text))
                {
                    offenders.Add($"{relativePath}:{line.index + 1}: suspicious .Length usage");
                }

                foreach (Match match in SuspiciousMethodOperandRegex.Matches(line.text))
                {
                    offenders.Add($"{relativePath}:{line.index + 1}: suspicious method operand without ()");
                }
            }
        }

        offenders.Should().BeEmpty("NML Roslyn compilation must not hit known parse/semantic traps");
    }
}
