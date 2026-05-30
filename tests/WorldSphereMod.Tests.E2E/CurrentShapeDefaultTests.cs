using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Guards CurrentShape default at 0 (flat). Sphere/cylindrical mode (1)
/// causes GPU hangs on large maps — this test prevents silent reversion
/// via field default, JSON corruption, or schema migration.
/// </summary>
public class CurrentShapeDefaultTests
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

    private static string ReadSourceFile(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        File.Exists(fullPath).Should().BeTrue($"source file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void SavedSettings_CurrentShape_defaults_to_zero_flat()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/SavedSettings.cs");

        // Match the field declaration and extract the default value.
        var match = Regex.Match(source, @"public\s+int\s+CurrentShape\s*=\s*(\d+)\s*;");
        match.Success.Should().BeTrue("SavedSettings must declare a public int CurrentShape field with a default");

        int defaultValue = int.Parse(match.Groups[1].Value);
        defaultValue.Should().Be(0,
            "CurrentShape must default to 0 (flat) — sphere/cylindrical (1) causes GPU hangs on large maps");
    }

    [Fact]
    public void ApplySchemaVersionMigration_does_not_reset_CurrentShape()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/Core.cs");

        // ApplySchemaVersionMigration must exist
        source.Should().Contain("ApplySchemaVersionMigration(",
            "schema migration method must be called during version mismatch");

        // It calls ApplyPhaseDefaults which only sets boolean phase flags.
        // Verify CurrentShape is NOT assigned inside the migration method body.
        var migrationBody = ExtractMethodBody(source, "ApplySchemaVersionMigration");
        migrationBody.Should().NotBeNull("ApplySchemaVersionMigration method must exist in Core.cs");

        // The method body may mention CurrentShape in comments (documenting
        // that it is intentionally preserved). What matters is that no
        // *assignment* to CurrentShape exists — i.e. no "CurrentShape =" or
        // ".CurrentShape =" outside of a comment line.
        var lines = migrationBody!.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//")) continue; // skip comment lines
            trimmed.Should().NotMatchRegex(@"\.?\s*CurrentShape\s*=",
                "schema migration must NOT assign to CurrentShape — the user's chosen shape must survive upgrades");
        }
    }

    [Fact]
    public void ApplyPhaseDefaults_does_not_reset_CurrentShape()
    {
        var source = ReadSourceFile("WorldSphereMod/Code/SavedSettings.cs");

        var methodBody = ExtractMethodBody(source, "ApplyPhaseDefaults");
        methodBody.Should().NotBeNull("ApplyPhaseDefaults method must exist in SavedSettings.cs");

        methodBody.Should().NotContain("CurrentShape",
            "ApplyPhaseDefaults must NOT touch CurrentShape — it should only reset boolean phase flags");
    }

    /// <summary>
    /// Extracts the body of a method by name (brace-counted).
    /// Returns null if the method is not found.
    /// </summary>
    private static string? ExtractMethodBody(string source, string methodName)
    {
        int idx = source.IndexOf(methodName, StringComparison.Ordinal);
        if (idx < 0) return null;

        // Find the opening brace
        int braceStart = source.IndexOf('{', idx);
        if (braceStart < 0) return null;

        int depth = 1;
        int pos = braceStart + 1;
        while (pos < source.Length && depth > 0)
        {
            if (source[pos] == '{') depth++;
            else if (source[pos] == '}') depth--;
            pos++;
        }

        return source.Substring(braceStart, pos - braceStart);
    }
}
