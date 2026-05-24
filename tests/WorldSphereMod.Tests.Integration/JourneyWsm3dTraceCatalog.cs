using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Canonical manifest step slug → wsm3d.ps1 subcommand traces for phase journeys (1–10).
/// See docs/journeys/scratch/journey-integration-trace.md.
/// </summary>
internal static class JourneyWsm3dTraceCatalog
{
    internal sealed record StepTrace(
        string Slug,
        IReadOnlyList<string> Wsm3dSubcommands,
        string? Wsm3dFunctionAnchor = null,
        bool AutomatedInJourneyCapture = false);

    internal static readonly IReadOnlyList<string> CanonicalStepSlugs =
    [
        "baseline",
        "open-settings",
        "toggle-on",
        "reload-world",
    ];

    internal static readonly IReadOnlyDictionary<string, string> ManifestIdToTogglePhaseKey =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["us-wsm-phase-1-voxel-actors"] = "VoxelEntities",
            ["us-wsm-phase-2-mesh-buildings"] = "ProceduralBuildings",
            ["us-wsm-phase-3-crossed-foliage"] = "CrossedQuadFoliage",
            ["us-wsm-phase-4-mesh-water"] = "MeshWater",
            ["us-wsm-phase-5-shadows"] = "HighShadows",
            ["us-wsm-phase-6-skeletal"] = "SkeletalAnimation",
            ["us-wsm-phase-7-worldspace-ui"] = "WorldspaceUI",
            ["us-wsm-phase-8-day-night"] = "DayNightCycle",
            ["us-wsm-phase-9-postfx"] = "PostFX",
        };

    internal static readonly IReadOnlyDictionary<string, StepTrace> SlugToTrace =
        new Dictionary<string, StepTrace>(StringComparer.Ordinal)
        {
            ["baseline"] = new StepTrace(
                "baseline",
                ["screenshot"],
                "Invoke-Screenshot",
                AutomatedInJourneyCapture: true),
            ["open-settings"] = new StepTrace(
                "open-settings",
                ["screenshot"],
                "Invoke-Screenshot",
                AutomatedInJourneyCapture: true),
            ["toggle-on"] = new StepTrace(
                "toggle-on",
                ["toggle"],
                "Invoke-Toggle",
                AutomatedInJourneyCapture: false),
            ["reload-world"] = new StepTrace(
                "reload-world",
                ["relaunch"],
                "Invoke-Relaunch",
                AutomatedInJourneyCapture: false),
            ["before"] = new StepTrace(
                "before",
                ["screenshot"],
                "Invoke-ScreenshotPhase",
                AutomatedInJourneyCapture: true),
            ["after"] = new StepTrace(
                "after",
                ["screenshot"],
                "Invoke-ScreenshotPhase",
                AutomatedInJourneyCapture: true),
            ["buildings"] = new StepTrace(
                "buildings",
                ["screenshot"],
                "Invoke-ScreenshotPhase",
                AutomatedInJourneyCapture: true),
            ["foliage"] = new StepTrace(
                "foliage",
                ["screenshot"],
                "Invoke-ScreenshotPhase",
                AutomatedInJourneyCapture: true),
            ["water"] = new StepTrace(
                "water",
                ["screenshot"],
                "Invoke-ScreenshotPhase",
                AutomatedInJourneyCapture: true),
            ["shadows-sky"] = new StepTrace(
                "shadows-sky",
                ["screenshot"],
                "Invoke-ScreenshotPhase",
                AutomatedInJourneyCapture: true),
            ["skeletal"] = new StepTrace(
                "skeletal",
                ["screenshot"],
                "Invoke-ScreenshotPhase",
                AutomatedInJourneyCapture: true),
            ["ui"] = new StepTrace(
                "ui",
                ["screenshot"],
                "Invoke-ScreenshotPhase",
                AutomatedInJourneyCapture: true),
            ["day-night"] = new StepTrace(
                "day-night",
                ["screenshot"],
                "Invoke-ScreenshotPhase",
                AutomatedInJourneyCapture: true),
            ["postfx"] = new StepTrace(
                "postfx",
                ["screenshot"],
                "Invoke-ScreenshotPhase",
                AutomatedInJourneyCapture: true),
            ["lod"] = new StepTrace(
                "lod",
                ["screenshot"],
                "Invoke-ScreenshotPhase",
                AutomatedInJourneyCapture: true),
        };

    internal static bool IsPhaseJourneyManifestId(string manifestId) =>
        manifestId.StartsWith("us-wsm-phase-", StringComparison.Ordinal);

    internal static bool IsSmokeTestManifestId(string manifestId) =>
        manifestId.StartsWith("smoke-test-phase", StringComparison.Ordinal);

    internal static StepTrace VerifyStepTrace(string slug) =>
        new StepTrace(
            slug,
            ["screenshot", "journey verify"],
            "Invoke-JourneyVerify",
            AutomatedInJourneyCapture: true);

    internal static bool IsVerifySlug(string slug) =>
        slug.StartsWith("verify-", StringComparison.Ordinal);

    internal static StepTrace ResolveStepTrace(string slug) =>
        SlugToTrace.TryGetValue(slug, out var trace)
            ? trace
            : IsVerifySlug(slug)
                ? VerifyStepTrace(slug)
                : throw new ArgumentException($"Unknown journey step slug: {slug}", nameof(slug));

    internal static IReadOnlyList<StepTrace> ManifestLevelTraces(string manifestId) =>
    [
        new StepTrace(
            manifestId,
            ["journey capture", "journey verify"],
            "Invoke-JourneyCapture",
            AutomatedInJourneyCapture: false),
    ];

    internal static IEnumerable<string> DispatcherTokensForSubcommand(string subcommand)
    {
        foreach (var token in subcommand.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return $"\"{token}\"";
        }
    }

    internal static bool Wsm3dScriptExposesSubcommand(string script, string subcommand)
    {
        var tokens = DispatcherTokensForSubcommand(subcommand).ToList();
        if (tokens.Count == 0)
        {
            return false;
        }

        return tokens.All(token => script.Contains(token, StringComparison.Ordinal));
    }
}
