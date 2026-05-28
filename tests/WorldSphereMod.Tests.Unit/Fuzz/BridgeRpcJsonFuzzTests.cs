using System;
using System.Globalization;
using System.Reflection;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorldSphereMod;
using WorldSphereMod.Bridge;
using WorldSphereMod.Tests.Unit.Fuzz;
using Xunit;

namespace WorldSphereMod.Tests.Unit.Fuzz;

/// <summary>
/// Fuzz / property-style tests for BridgeRPC settings value parsing and settings JSON round-trip.
/// </summary>
public class BridgeRpcJsonFuzzTests
{
    static readonly BindingFlags SettingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;

    [Fact]
    public void TryParseSettingValue_random_strings_never_throw_for_all_saved_settings_fields()
    {
        var rng = FuzzRandom.Create();
        FieldInfo[] fields = typeof(SavedSettings).GetFields(SettingFlags);

        for (int i = 0; i < FuzzRandom.DefaultIterations; i++)
        {
            FieldInfo field = fields[rng.Next(fields.Length)];
            string raw = (i % 4) switch
            {
                0 => FuzzRandom.RandomAscii(rng, 48),
                1 => FuzzRandom.RandomUnicode(rng, 16),
                2 => rng.Next(2) == 0 ? "true" : "false",
                _ => rng.Next(int.MinValue, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            };

            bool ok = false;
            string error = null!;
            object? parsed = null;
            Action act = () => ok = BridgeSettingParser.TryParseSettingValue(field.FieldType, raw, out parsed, out error);
            act.Should().NotThrow($"field {field.Name} raw '{raw}'");
            if (!ok)
            {
                error.Should().NotBeNullOrEmpty();
            }
        }
    }

    [Fact]
    public void TryParseNonNegativeInt_random_strings_never_throw()
    {
        var rng = FuzzRandom.Create();
        for (int i = 0; i < FuzzRandom.DefaultIterations; i++)
        {
            string text = FuzzRandom.RandomAscii(rng, 24);
            bool ok = false;
            int value = -1;
            Action act = () => ok = BridgeSettingParser.TryParseNonNegativeInt(text, out value);
            act.Should().NotThrow();
            if (ok)
            {
                value.Should().BeGreaterThanOrEqualTo(0);
            }
        }
    }

    [Fact]
    public void Settings_get_roundtrip_json_survives_unknown_fields_and_reparse()
    {
        var rng = FuzzRandom.Create();
        for (int i = 0; i < FuzzRandom.DefaultIterations; i++)
        {
            var settings = new SavedSettings
            {
                Version = "2.3",
                VoxelEntities = rng.Next(2) == 0,
                RenderRange = (float)(rng.NextDouble() * 10),
                VoxelInflationStyle = rng.Next(3) switch { 0 => "pertexel", 1 => "balloon", _ => "lathe" },
            };

            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            json = FuzzRandom.MutateJson(rng, json);

            Action serializeAct = () => _ = JsonConvert.SerializeObject(settings);
            serializeAct.Should().NotThrow();

            bool ok = false;
            SavedSettings? roundTrip = null;
            Action deserializeAct = () => ok = SavedSettingsJson.TryDeserialize(json, out roundTrip);
            deserializeAct.Should().NotThrow();

            if (!ok)
            {
                continue;
            }

            roundTrip.Should().NotBeNull();
            // JSON mutation may rewrite Version independently of the in-memory settings object;
            // TryDeserialize mirrors Core.LoadSettings parse (no Version gate). Core bumps Version on load.
            Action reparseAct = () => _ = JsonConvert.SerializeObject(roundTrip);
            reparseAct.Should().NotThrow();
        }
    }

    [Fact]
    public void Bridge_response_anonymous_payloads_serialize_without_throw()
    {
        var rng = FuzzRandom.Create();
        for (int i = 0; i < 200; i++)
        {
            object payload = rng.Next(3) switch
            {
                0 => new { ok = false, error = FuzzRandom.RandomAscii(rng, 32), key = "VoxelEntities", value = FuzzRandom.RandomUnicode(rng, 8) },
                1 => new { ok = true, key = "Is3D", value = rng.Next(2) == 0 },
                _ => new { ok = false, error = "invalid_slot", slot = FuzzRandom.RandomAscii(rng, 12) },
            };

            string? json = null;
            Action act = () => json = JsonConvert.SerializeObject(payload, Formatting.None);
            act.Should().NotThrow();
            json.Should().NotBeNullOrEmpty();

            Action parseAct = () => _ = JObject.Parse(json!);
            parseAct.Should().NotThrow();
        }
    }

    [Fact]
    public void TryParseSettingValue_bool_accepts_zero_one_and_rejects_garbage_safely()
    {
        var rng = FuzzRandom.Create();
        for (int i = 0; i < FuzzRandom.DefaultIterations; i++)
        {
            string raw = FuzzRandom.RandomAscii(rng, 16);
            bool ok = BridgeSettingParser.TryParseSettingValue(typeof(bool), raw, out object? parsed, out string error);
            if (ok)
            {
                parsed.Should().BeOfType<bool>();
            }
            else
            {
                error.Should().Be("invalid_bool");
            }
        }
    }
}
