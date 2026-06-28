using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WorldSphereMod.Tests.E2E
{
    /// <summary>
    /// Source-text invariants for the actor-rig registry defined in
    /// WorldSphereMod/Code/Constants.cs (populated via AddRigGroup).
    ///
    /// These tests intentionally do NOT call Constants.* at runtime because the
    /// test project doesn't reference the Unity source project (Constants.cs
    /// pulls UnityEngine + WorldSphereMod.Rig + WorldSphereMod.Effects).
    /// Instead they assert on the source of Constants.cs directly.
    /// </summary>
    public class SkeletalRigVariantInvariantsTests
    {
        private static readonly string RepoRoot = ResolveRepoRoot();

        private static string ResolveRepoRoot()
        {
            // Walk up from the test binary location to find the WorldSphereMod repo root
            // (marked by the presence of WorldSphereMod/Code/Constants.cs).
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var probe = Path.Combine(dir.FullName, "WorldSphereMod", "Code", "Constants.cs");
                if (File.Exists(probe))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate WorldSphereMod repo root from " + AppContext.BaseDirectory);
        }

        private string ConstantsSource =>
            File.ReadAllText(Path.Combine(RepoRoot, "WorldSphereMod", "Code", "Constants.cs"));

        [Fact]
        public void Constants_source_uses_AddRigGroup_helper()
        {
            ConstantsSource.Should().Contain(
                "AddRigGroup",
                "Constants.cs populates ActorRigTypes via the AddRigGroup helper (no inline [\"id\"] syntax)");
        }

        [Fact]
        public void Constants_source_declares_ActorRigTypes_dictionary()
        {
            ConstantsSource.Should().MatchRegex(
                @"public\s+static\s+(?:readonly\s+)?Dictionary<string,\s*RigType>\s+ActorRigTypes",
                "ActorRigTypes is the canonical rig registry dictionary");
        }

        [Theory]
        [InlineData("human")]
        [InlineData("villager")]
        [InlineData("swordsman")]
        [InlineData("archer")]
        [InlineData("mage")]
        [InlineData("orc")]
        [InlineData("elf")]
        [InlineData("dwarf")]
        [InlineData("goblin")]
        [InlineData("skeleton")]
        [InlineData("zombie")]
        [InlineData("bandit")]
        [InlineData("necromancer")]
        [InlineData("druid")]
        [InlineData("king")]
        public void ActorRigTypes_registry_contains_expected_id(string actorId)
        {
            // The ID is registered with AddRigGroup inside a `new[] { ... }` literal.
            // We assert the literal text appears in the source.
            var pattern = "\"" + actorId + "\"";
            ConstantsSource.Should().Contain(
                pattern,
                $"ActorRigTypes is expected to register actor id '{actorId}' via AddRigGroup");
        }

        [Fact]
        public void ActorRigTypes_registers_at_least_3_groups()
        {
            // Count AddRigGroup invocations - each registers one rig group with >=1 actor
            var addCalls = Regex.Matches(ConstantsSource, @"AddRigGroup\s*\(");
            addCalls.Count.Should().BeGreaterThanOrEqualTo(
                3,
                "Constants.cs must register at minimum the humanoids, undead, and other rig groups");
        }

        [Fact]
        public void Constants_source_declares_ResolveActorRig_method()
        {
            ConstantsSource.Should().MatchRegex(
                @"public\s+static\s+RigType\s+ResolveActorRig\s*\(\s*string\s+\w+",
                "ResolveActorRig is the canonical lookup API");
        }

        [Fact]
        public void Constants_does_not_use_inline_dictionary_initialization()
        {
            // The registry must NOT be initialized with inline ["id"] = RigType.X syntax
            // because that prevents the test project from referencing the type.
            ConstantsSource.Should().NotMatchRegex(
                @"ActorRigTypes\s*=\s*new\s+Dictionary[^=]*=\s*\{",
                "ActorRigTypes uses AddRigGroup helper, not inline dictionary initialization");
        }
    }
}
