using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Shared path rules for Phenotype journey manifests under docs/journeys/.
/// Mirrors offline checks in Tools/journey-records/src/validate.rs.
/// </summary>
internal static class JourneyManifestPathValidation
{
    private static readonly Regex EmbeddedDocsPathPattern = new(
        @"docs/[A-Za-z0-9_./-]+\.(?:md|json|png|gif|webp|mp4|webm)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> CaptureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".gif", ".webp", ".jpg", ".jpeg", ".mp4", ".webm", ".mov",
    };

    internal static IEnumerable<string> ExtractEmbeddedDocPaths(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (Match match in EmbeddedDocsPathPattern.Matches(text))
        {
            yield return NormalizeSlashes(match.Value);
        }
    }

    internal static bool IsUnsafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var normalized = NormalizeSlashes(path);
        return normalized.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(path)
            || normalized.Contains("://", StringComparison.Ordinal);
    }

    internal static bool IsOptionalCaptureAsset(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        if (!CaptureExtensions.Contains(extension))
        {
            return false;
        }

        return fileName.StartsWith("frame-", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("recording", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsRepoDocPath(string path) =>
        NormalizeSlashes(path).StartsWith("docs/", StringComparison.OrdinalIgnoreCase);

    internal static bool ShouldExistOnDisk(string path)
    {
        if (IsRepoDocPath(path))
        {
            return true;
        }

        return !IsOptionalCaptureAsset(path);
    }

    internal static string ResolvePath(string repoRoot, string manifestDir, string path)
    {
        var normalized = NormalizeSlashes(path);
        if (IsRepoDocPath(normalized))
        {
            return Path.GetFullPath(Path.Combine(repoRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        }

        return Path.GetFullPath(Path.Combine(manifestDir, path));
    }

    internal static string NormalizeSlashes(string path) => path.Replace('\\', '/');
}
