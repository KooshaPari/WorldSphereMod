using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace WorldSphereMod.Tests.Unit.Fuzz;

/// <summary>Seeded random helpers for property-style fuzz loops (no FsCheck dependency).</summary>
internal static class FuzzRandom
{
    public const int DefaultIterations = 400;
    public const int Seed = 0xC0FFEE;

    public static Random Create(int? seed = null) => new Random(seed ?? Seed);

    public static string RandomAscii(Random rng, int maxLen)
    {
        int len = rng.Next(0, maxLen + 1);
        var sb = new StringBuilder(len);
        for (int i = 0; i < len; i++)
        {
            sb.Append((char)rng.Next(32, 127));
        }
        return sb.ToString();
    }

    public static string RandomUnicode(Random rng, int maxChars)
    {
        int len = rng.Next(0, maxChars + 1);
        var sb = new StringBuilder(len);
        for (int i = 0; i < len; i++)
        {
            sb.Append((char)rng.Next(0, 0x10FFFF));
        }
        return sb.ToString();
    }

    public static string MutateJson(Random rng, string json)
    {
        int op = rng.Next(6);
        return op switch
        {
            0 => json.Length == 0 ? "{" : json[..rng.Next(1, json.Length)],
            1 => json + RandomAscii(rng, 32),
            2 => RandomAscii(rng, 8) + json,
            3 => json.Insert(rng.Next(0, Math.Max(1, json.Length)), RandomUnicode(rng, 4)),
            4 => json.Replace("{", rng.Next(2) == 0 ? "[" : "{", StringComparison.Ordinal),
            _ => json,
        };
    }

    public static string PartialSettingsPayload(Random rng, IReadOnlyList<string> knownFields)
    {
        var obj = new JObject();
        int count = rng.Next(0, Math.Min(knownFields.Count, 8) + 1);
        for (int i = 0; i < count; i++)
        {
            string field = knownFields[rng.Next(knownFields.Count)];
            obj[field] = rng.Next(4) switch
            {
                0 => rng.Next(2) == 0,
                1 => rng.Next(-3, 100),
                2 => RandomAscii(rng, 12),
                _ => null,
            };
        }
        return obj.ToString(Newtonsoft.Json.Formatting.None);
    }
}
