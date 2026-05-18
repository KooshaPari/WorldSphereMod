using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace WorldSphereMod.ProcGen
{
    public enum RoofStyle
    {
        Inferred,
        Flat,
        Gable,
        Hipped
    }

    [Serializable]
    public struct DoorSpec
    {
        public int x;
        public int y;
        public int w;
        public int h;
    }

    public class BuildingRules
    {
        public string? AssetId;
        public RoofStyle Roof;
        public int Stories;
        public float FootprintDepth;
        public DoorSpec[] Doors;
        public DoorSpec[] Windows;
        public bool PerpendicularRoof;

        public BuildingRules()
        {
            AssetId = null;
            Roof = RoofStyle.Inferred;
            Stories = 0;
            FootprintDepth = 0f;
            Doors = Array.Empty<DoorSpec>();
            Windows = Array.Empty<DoorSpec>();
            PerpendicularRoof = false;
        }

        public static BuildingRules Default => new BuildingRules();
    }

    public static class BuildingRulesLoader
    {
        public static Dictionary<string, BuildingRules> LoadFromDirectory(string path)
        {
            var registry = new Dictionary<string, BuildingRules>();
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return registry;

            string[] files;
            try
            {
                files = Directory.GetFiles(path, "*.json");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldSphereMod.ProcGen] BuildingRulesLoader: failed to enumerate '{path}': {ex.Message}");
                return registry;
            }

            foreach (var file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    BuildingRules? r = JsonConvert.DeserializeObject<BuildingRules>(json);
                    if (r == null) continue;
                    MergeInto(registry, r);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[WorldSphereMod.ProcGen] BuildingRulesLoader: failed to parse '{file}': {ex.Message}");
                }
            }

            return registry;
        }

        public static void MergeInto(Dictionary<string, BuildingRules> registry, BuildingRules r)
        {
            if (registry == null || r == null) return;
            if (string.IsNullOrEmpty(r.AssetId)) return;
            registry[r.AssetId!] = r;
        }
    }

    public static class BuildingRulesRegistry
    {
        static readonly ConcurrentDictionary<string, BuildingRules> _rules =
            new ConcurrentDictionary<string, BuildingRules>();

        public static void Register(string assetId, BuildingRules rules)
        {
            if (string.IsNullOrEmpty(assetId) || rules == null) return;
            _rules[assetId] = rules;
            // Drop the cached mesh so the next frame regenerates against the new rules.
            ProcGenCache.Invalidate(assetId);
        }

        public static BuildingRules Resolve(string assetId)
        {
            if (string.IsNullOrEmpty(assetId)) return BuildingRules.Default;
            return _rules.TryGetValue(assetId, out var r) ? r : BuildingRules.Default;
        }

        public static void Invalidate(string assetId)
        {
            if (string.IsNullOrEmpty(assetId)) return;
            ProcGenCache.Invalidate(assetId);
        }
    }
}
