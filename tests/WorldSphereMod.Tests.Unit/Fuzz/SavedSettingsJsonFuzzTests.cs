using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorldSphereMod;
using WorldSphereMod.Tests.Unit.Fuzz;
using Xunit;

namespace WorldSphereMod.Tests.Unit.Fuzz;

/// <summary>
/// Fuzz / property-style resilience tests for SavedSettings JSON load path
/// (<see cref="SavedSettingsJson"/> mirrors <see cref="Core.LoadSettings"/> parse behavior).
/// </summary>
public class SavedSettingsJsonFuzzTests
{
    static readonly string[] KnownBoolFields =
    {
        "VoxelEntities", "ProceduralBuildings", "MeshWater", "HighShadows",
        "SkeletalAnimation", "PostFX", "SSAOEnabled", "WeatherRain",
    };

    [Fact]
    public void TryDeserialize_random_malformed_input_never_throws()
    {
        var rng = FuzzRandom.Create();
        for (int i = 0; i < FuzzRandom.DefaultIterations; i++)
        {
            string raw = (i % 3) switch
            {
                0 => FuzzRandom.RandomAscii(rng, 256),
                1 => FuzzRandom.RandomUnicode(rng, 64),
                _ => FuzzRandom.MutateJson(rng, """{"Version":"2.2","Is3D":true}"""),
            };

            bool ok = false;
            SavedSettings? settings = null;
            Action act = () => ok = SavedSettingsJson.TryDeserialize(raw, out settings);
            act.Should().NotThrow($"iteration {i} with payload length {raw.Length}");
            if (!ok)
            {
                settings.Should().BeNull();
            }
        }
    }

    [Fact]
    public void TryDeserialize_partial_payloads_never_throw_and_recognized_fields_apply()
    {
        var rng = FuzzRandom.Create();
        var allFields = typeof(SavedSettings)
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Select(f => f.Name)
            .ToList();

        for (int i = 0; i < FuzzRandom.DefaultIterations; i++)
        {
            string raw = FuzzRandom.PartialSettingsPayload(rng, allFields);
            bool ok = false;
            SavedSettings? settings = null;
            Action act = () => ok = SavedSettingsJson.TryDeserialize(raw, out settings);
            act.Should().NotThrow();

            if (!ok || settings == null)
            {
                continue;
            }

            try
            {
                var obj = JObject.Parse(raw);
                if (obj.TryGetValue("VoxelEntities", out JToken? voxelToken) && voxelToken.Type == JTokenType.Boolean)
                {
                    settings.VoxelEntities.Should().Be(voxelToken.Value<bool>());
                }
            }
            catch (JsonReaderException)
            {
                // PartialSettingsPayload always emits valid JSON; ignore if mutation path added later.
            }
        }
    }

    [Fact]
    public void TryDeserialize_unknown_fields_do_not_crash()
    {
        var rng = FuzzRandom.Create();
        var baseline = new SavedSettings();
        string baseJson = JsonConvert.SerializeObject(baseline);

        for (int i = 0; i < FuzzRandom.DefaultIterations; i++)
        {
            var obj = JObject.Parse(baseJson);
            obj[$"__fuzz_{FuzzRandom.RandomAscii(rng, 12)}"] = FuzzRandom.RandomUnicode(rng, 8);
            obj["legacyUnknownPhase"] = rng.Next(2) == 0;
            string raw = obj.ToString();

            bool ok = false;
            SavedSettings? settings = null;
            Action act = () => ok = SavedSettingsJson.TryDeserialize(raw, out settings);
            act.Should().NotThrow();
            ok.Should().BeTrue();
            settings.Should().NotBeNull();
            settings!.Version.Should().Be(baseline.Version);
        }
    }

    [Fact]
    public void ApplyTerrainSmoothingMigration_copies_legacy_field_when_present()
    {
        const string raw = """{"TerrainSmoothing":true,"Version":"1.5"}""";
        string migrated = SavedSettingsJson.ApplyTerrainSmoothingMigration(raw);
        migrated.Should().Contain("MountainSlopeSmoothing");

        bool ok = SavedSettingsJson.TryDeserialize(raw, out SavedSettings? settings);
        ok.Should().BeTrue();
        settings!.MountainSlopeSmoothing.Should().BeTrue();
    }

    [Fact]
    public void TryDeserialize_truncated_json_returns_false_without_throw()
    {
        string full = JsonConvert.SerializeObject(new SavedSettings());
        for (int cut = 1; cut < Math.Min(full.Length, 40); cut++)
        {
            string raw = full[..cut];
            bool ok = false;
            Action act = () => ok = SavedSettingsJson.TryDeserialize(raw, out _);
            act.Should().NotThrow();
            ok.Should().BeFalse();
        }
    }
}
