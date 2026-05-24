using System;
using System.IO;
using FluentAssertions;
using Xunit;

/// <summary>
/// Closes Phase 2 E2E gaps: footprint extrusion, roof/door/window heuristics,
/// rules overrides, and roof orientation (e2e-coverage-gaps.md #2).
/// </summary>
public class BuildingProcGenInvariantsTests
{
    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldSphereMod.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("repo root with WorldSphereMod.sln must be locatable from test cwd");
        return dir!.FullName;
    }

    static string ReadSourceFile(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        File.Exists(fullPath).Should().BeTrue($"source file must exist at {fullPath}");
        return File.ReadAllText(fullPath);
    }

    static string ExtractMethodBody(string source, string signature)
    {
        int headerIndex = source.IndexOf(signature, StringComparison.Ordinal);
        headerIndex.Should().BeGreaterThanOrEqualTo(0, $"method signature should exist: {signature}");

        int openBrace = source.IndexOf('{', headerIndex);
        openBrace.Should().BeGreaterThanOrEqualTo(0, "method must open with a '{'");

        int depth = 0;
        for (int i = openBrace; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '{')
            {
                depth++;
                continue;
            }

            if (c != '}')
            {
                continue;
            }

            depth--;
            if (depth == 0)
            {
                return source.Substring(openBrace + 1, i - openBrace - 1);
            }
        }

        throw new InvalidOperationException("Unbalanced braces while extracting method body");
    }

    [Fact]
    public void BuildingMeshGen_Generate_runs_footprint_story_opening_and_roof_pipeline()
    {
        var gen = ReadSourceFile("WorldSphereMod/Code/ProcGen/BuildingMeshGen.cs");
        var generateBody = ExtractMethodBody(gen, "public static Mesh? Generate(BuildingAsset asset, BuildingRules rules)");

        generateBody.Should().Contain("DetectFootprint(pixels, w, h)",
            "procgen must derive an opaque-pixel bounding box before extrusion");
        generateBody.Should().Contain("InferStories(pixels, w, h, bbox, rules)",
            "vertical banding must drive story height");
        generateBody.Should().Contain("InferOpenings(pixels, w, h, bbox, rules)",
            "door/window heuristics must run before mesh build");
        generateBody.Should().Contain("InferRoof(pixels, w, bbox, rules, wallColor, out RoofStyle roofStyle, out Color roofColor)",
            "roof style/color must be inferred or taken from rules");
        generateBody.Should().Contain("rules.PerpendicularRoof",
            "ridge orientation must honor BuildingRules.PerpendicularRoof");
    }

    [Fact]
    public void BuildingMeshGen_InferOpenings_classifies_doors_and_windows_with_rules_override()
    {
        var gen = ReadSourceFile("WorldSphereMod/Code/ProcGen/BuildingMeshGen.cs");
        var openingsBody = ExtractMethodBody(gen, "static List<DoorSpec> InferOpenings(Color32[] px, int w, int h, RectInt bbox, BuildingRules rules)");

        openingsBody.Should().Contain("rules.Doors != null && rules.Doors.Length > 0",
            "explicit door specs from BuildingRules must short-circuit heuristics");
        openingsBody.Should().Contain("rules.Windows != null && rules.Windows.Length > 0",
            "explicit window specs from BuildingRules must short-circuit heuristics");
        openingsBody.Should().Contain("bool isDoor = rh > rw && rh >= 4",
            "tall dark blobs must classify as doors");
        openingsBody.Should().Contain("bool isWindow = rw > rh && rh <= 6",
            "wide shallow blobs must classify as windows");
    }

    [Fact]
    public void BuildingMeshGen_InferRoof_honors_rules_override_and_IsRoofPixel_heuristic()
    {
        var gen = ReadSourceFile("WorldSphereMod/Code/ProcGen/BuildingMeshGen.cs");

        gen.Should().Contain("static bool IsRoofPixel(Color32 c32, out float hueDeg, Color wallRef)",
            "roof pixels must be distinguished from wall color");
        gen.Should().Contain("if (rules.Roof != RoofStyle.Inferred)",
            "non-inferred RoofStyle on BuildingRules must bypass sprite inference");

        var inferRoofBody = ExtractMethodBody(gen, "static void InferRoof(Color32[] px, int w, RectInt bbox, BuildingRules rules,");
        inferRoofBody.Should().Contain("RoofStyle.Gable");
        inferRoofBody.Should().Contain("RoofStyle.Hipped");
        inferRoofBody.Should().Contain("RoofStyle.Flat");
    }

    [Fact]
    public void BuildingMeshGen_EmitGableRoof_flips_ridge_axis_when_PerpendicularRoof_set()
    {
        var gen = ReadSourceFile("WorldSphereMod/Code/ProcGen/BuildingMeshGen.cs");
        var gableBody = ExtractMethodBody(gen, "static void EmitGableRoof(float halfX, float halfZ, float height, Color color,");

        gableBody.Should().Contain("bool ridgeAlongX = perpendicular ? !xIsLong : xIsLong",
            "PerpendicularRoof must rotate ridge onto the short footprint axis");
    }

    [Fact]
    public void BuildingRulesRegistry_invalidates_procgen_cache_on_override_register()
    {
        var registry = ReadSourceFile("WorldSphereMod/Code/ProcGen/BuildingRules.cs");
        var registerBody = ExtractMethodBody(registry, "public static void Register(string assetId, BuildingRules rules)");

        registerBody.Should().Contain("ProcGenCache.Invalidate(assetId)",
            "rules overrides must drop cached meshes so orientation/openings regenerate");
    }
}
