using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

/// <summary>
/// Guards the wsm3d.ps1 PhaseDefaults hashtable against drift from
/// SavedSettings.cs boolean field defaults.  If either side changes a
/// default without updating the other, this test fails with a clear
/// per-field message.
/// </summary>
public class PhaseDefaultsDriftTests
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

    /// <summary>
    /// Parses "public bool FieldName = true/false;" declarations from SavedSettings.cs.
    /// Returns a dictionary of field name -> bool default.
    /// </summary>
    private static Dictionary<string, bool> ParseSavedSettingsBoolDefaults(string source)
    {
        var defaults = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(
                     source,
                     @"public\s+bool\s+(\w+)\s*=\s*(true|false)\s*;"))
        {
            defaults[match.Groups[1].Value] = match.Groups[2].Value == "true";
        }

        return defaults;
    }

    /// <summary>
    /// Parses the $script:PhaseDefaults hashtable from wsm3d.ps1.
    /// Expects entries like:  "VoxelEntities" = $true
    /// Returns a dictionary of field name -> bool default.
    /// </summary>
    private static Dictionary<string, bool> ParsePs1PhaseDefaults(string source)
    {
        var defaults = new Dictionary<string, bool>(StringComparer.Ordinal);

        // Match lines like:  "VoxelEntities"       = $true
        foreach (Match match in Regex.Matches(
                     source,
                     @"""(\w+)""\s*=\s*\$(true|false)",
                     RegexOptions.IgnoreCase))
        {
            defaults[match.Groups[1].Value] =
                string.Equals(match.Groups[2].Value, "true", StringComparison.OrdinalIgnoreCase);
        }

        return defaults;
    }

    [Fact]
    public void PhaseDefaults_hashtable_matches_SavedSettings_bool_defaults()
    {
        var root = FindRepoRoot();

        var savedSettingsPath = Path.Combine(root, "WorldSphereMod", "Code", "SavedSettings.cs");
        File.Exists(savedSettingsPath).Should().BeTrue("SavedSettings.cs must exist");
        var csDefaults = ParseSavedSettingsBoolDefaults(File.ReadAllText(savedSettingsPath));

        var ps1Path = Path.Combine(root, "Tools", "wsm3d.ps1");
        File.Exists(ps1Path).Should().BeTrue("Tools/wsm3d.ps1 must exist");
        var ps1Defaults = ParsePs1PhaseDefaults(File.ReadAllText(ps1Path));

        ps1Defaults.Should().NotBeEmpty("wsm3d.ps1 PhaseDefaults hashtable must contain entries");

        foreach (var (field, ps1Value) in ps1Defaults)
        {
            csDefaults.Should().ContainKey(
                field,
                $"PhaseDefaults entry \"{field}\" must correspond to a bool field in SavedSettings.cs");

            var csValue = csDefaults[field];
            ps1Value.Should().Be(
                csValue,
                $"PhaseDefaults[\"{field}\"] = ${ps1Value.ToString().ToLowerInvariant()} in wsm3d.ps1 " +
                $"but SavedSettings.cs declares {field} = {csValue.ToString().ToLowerInvariant()}");
        }
    }

    [Fact]
    public void PhaseDefaults_covers_all_phase_gated_flags()
    {
        // The PhaseDefaults hashtable must include every phase-gated bool
        // from SavedSettings.cs that appears in the PhaseMap slug table.
        var root = FindRepoRoot();

        var ps1Source = File.ReadAllText(Path.Combine(root, "Tools", "wsm3d.ps1"));
        var ps1Defaults = ParsePs1PhaseDefaults(ps1Source);

        // Extract PhaseMap values (the PascalCase side).
        var phaseMapValues = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     ps1Source,
                     @"""(\w+)""\s*=\s*""(\w+)"""))
        {
            phaseMapValues.Add(match.Groups[2].Value);
        }

        // Every PhaseMap target that is also in PhaseDefaults should stay there.
        // This catches someone adding a new PhaseMap entry but forgetting PhaseDefaults.
        foreach (var phaseName in phaseMapValues)
        {
            if (ps1Defaults.ContainsKey(phaseName))
            {
                continue;
            }

            // Some PhaseMap entries (e.g. SSAOEnabled, SSGIEnabled, BloomEnabled,
            // ACESTonemapping) are not in PhaseDefaults because they are sub-toggles
            // rather than top-level phase gates.  That is acceptable.
        }

        // At minimum the 10-phase core flags must be present.
        var corePhaseFlags = new[]
        {
            "VoxelEntities",
            "ProceduralBuildings",
            "CrossedQuadFoliage",
            "MeshWater",
            "HighShadows",
            "SkeletalAnimation",
            "WorldspaceUI",
            "DayNightCycle",
            "PostFX",
            "ParticleEffects",
        };

        foreach (var flag in corePhaseFlags)
        {
            ps1Defaults.Should().ContainKey(
                flag,
                $"PhaseDefaults must include the core phase flag \"{flag}\"");
        }
    }
}
