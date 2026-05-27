using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

public class NmlCompileCompatTests
{
    // NML's embedded Roslyn rejects some source patterns that net48/msbuild accepts.
    // Keep this narrow and evidence-backed from Player.log CS errors.
    private static readonly Regex SuspiciousLengthRegex =
        new(@"\btiles_list\s*\.\s*Length\b", RegexOptions.Compiled);

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
    public void Source_does_not_contain_suspicious_length_traps()
    {
        var root = FindRepoRoot();
        var offenders = new List<string>();

        foreach (var file in EnumerateSourceFiles(root))
        {
            var relativePath = Path.GetRelativePath(root, file);

            foreach (var line in File.ReadLines(file).Select((text, index) => (text, index)))
            {
                if (SuspiciousLengthRegex.IsMatch(line.text))
                {
                    offenders.Add(
                        $"{relativePath}:{line.index + 1}: NML Roslyn treats tiles_list.Length as a method group; assign tiles_list to a WorldTile[] local first");
                }
            }
        }

        offenders.Should().BeEmpty("NML Roslyn compilation must not hit known parse/semantic traps");
    }

    /// <summary>
    /// SmoothLoader.add() expects MapLoaderAction, not System.Action.
    /// NML Roslyn rejects the implicit conversion. Catch any call site that
    /// declares or casts a System.Action and passes it to SmoothLoader.add.
    /// </summary>
    [Fact]
    public void SmoothLoader_add_must_not_receive_System_Action()
    {
        var root = FindRepoRoot();
        var offenders = new List<string>();

        // Patterns that indicate a System.Action being fed to SmoothLoader.add:
        //  1. SmoothLoader.add( <identifier> ,  — where <identifier> was declared as Action / System.Action
        //  2. Explicit cast: (Action) or (System.Action) inside SmoothLoader.add(...)
        //  3. Variable typed as Action passed on a preceding line
        var addCallRegex = new Regex(@"SmoothLoader\s*\.\s*add\s*\(", RegexOptions.Compiled);
        var systemActionCast = new Regex(@"SmoothLoader\s*\.\s*add\s*\(\s*\(?\s*(System\s*\.\s*)?Action\s*[<>)\s]", RegexOptions.Compiled);
        var actionVarDecl = new Regex(@"\b(System\s*\.\s*)?Action\b\s+(\w+)\s*[=;]", RegexOptions.Compiled);

        foreach (var file in EnumerateSourceFiles(root))
        {
            var relativePath = Path.GetRelativePath(root, file);
            var lines = File.ReadAllLines(file);
            var declaredActionVars = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Track any local declared as Action / System.Action
                var declMatch = actionVarDecl.Match(line);
                if (declMatch.Success)
                {
                    declaredActionVars.Add(declMatch.Groups[2].Value);
                }

                if (!addCallRegex.IsMatch(line))
                    continue;

                // Flag explicit (System.)Action cast inside the call
                if (systemActionCast.IsMatch(line))
                {
                    offenders.Add(
                        $"{relativePath}:{i + 1}: SmoothLoader.add receives a System.Action cast — use MapLoaderAction or delegate {{ }}");
                    continue;
                }

                // Flag passing a variable previously declared as Action
                foreach (var varName in declaredActionVars)
                {
                    if (Regex.IsMatch(line, $@"SmoothLoader\s*\.\s*add\s*\(\s*{Regex.Escape(varName)}\b"))
                    {
                        offenders.Add(
                            $"{relativePath}:{i + 1}: SmoothLoader.add receives '{varName}' which was declared as System.Action — use MapLoaderAction");
                    }
                }
            }
        }

        offenders.Should().BeEmpty(
            "SmoothLoader.add expects MapLoaderAction, not System.Action — " +
            "NML Roslyn cannot implicitly convert between delegate types");
    }
}
