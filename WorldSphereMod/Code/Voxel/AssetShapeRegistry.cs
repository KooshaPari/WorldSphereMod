using System.Collections.Generic;
using UnityEngine;

namespace WorldSphereMod.Voxel
{
    public enum ShapeHint
    {
        Cylinder,
        OrganicBlob,
        LongX,
        LongZ,
        Tall,
        Flat,
        Mirror,
        Auto,
    }

    public static class AssetShapeRegistry
    {
        static readonly (string prefix, ShapeHint hint)[] _prefixHints =
        {
            ("human", ShapeHint.Flat),
            ("dwarf", ShapeHint.Flat),
            ("elf", ShapeHint.Flat),
            ("orc", ShapeHint.Flat),
            ("goblin", ShapeHint.Flat),
            ("tree", ShapeHint.Cylinder),
            ("bush", ShapeHint.Cylinder),
            ("flower", ShapeHint.Cylinder),
            ("barrel", ShapeHint.Cylinder),
            ("pot", ShapeHint.Cylinder),
            ("rock", ShapeHint.OrganicBlob),
            ("stone", ShapeHint.OrganicBlob),
            ("boulder", ShapeHint.OrganicBlob),
            ("mountain", ShapeHint.OrganicBlob),
            ("animal", ShapeHint.Flat),
            ("wolf", ShapeHint.Flat),
            ("bird", ShapeHint.Cylinder),
            ("eagle", ShapeHint.Cylinder),
            ("fish", ShapeHint.Cylinder),
            ("snake", ShapeHint.Cylinder),
            ("spider", ShapeHint.Cylinder),
            ("sheep", ShapeHint.Cylinder),
            ("horse", ShapeHint.Cylinder),
            ("cow", ShapeHint.Cylinder),
            ("rabbit", ShapeHint.Cylinder),
            ("crab", ShapeHint.Cylinder),
            ("zombie", ShapeHint.Cylinder),
            ("skeleton", ShapeHint.Cylinder),
            ("wall", ShapeHint.LongX),
            ("barracks", ShapeHint.LongX),
            ("bunker", ShapeHint.LongX),
            ("dock", ShapeHint.LongX),
            ("road", ShapeHint.Flat),
            ("bridge", ShapeHint.LongX),
            ("path", ShapeHint.Flat),
            ("tower", ShapeHint.Tall),
            ("lighthouse", ShapeHint.Tall),
            ("mast", ShapeHint.Tall),
            ("obelisk", ShapeHint.Tall),
            ("pillar", ShapeHint.Cylinder),
            ("boat", ShapeHint.Mirror),
            ("ship", ShapeHint.Mirror),
            ("wagon", ShapeHint.Mirror),
            ("cart", ShapeHint.Mirror),
            ("car", ShapeHint.Mirror),
            ("tank", ShapeHint.Mirror),
            ("vehicle", ShapeHint.Mirror),
        };

        public static ShapeHint GetShapeHint(string assetId)
        {
            if (string.IsNullOrEmpty(assetId)) return ShapeHint.Auto;
            string lower = assetId.ToLowerInvariant();
            foreach (var (prefix, hint) in _prefixHints)
            {
                if (lower.StartsWith(prefix) || lower.Contains("_" + prefix) || lower.Contains(prefix + "_"))
                    return hint;
            }
            return ShapeHint.Auto;
        }

        // Resolves the InflationStyle string used by SpriteVoxelizer dispatch.
        // Honors a non-"auto" global override in SavedSettings.VoxelInflationStyle.
        public static string ResolveStyle(string assetId, Sprite sprite)
        {
            string globalOverride = Core.savedSettings?.VoxelInflationStyle ?? "auto";
            if (!string.IsNullOrEmpty(globalOverride) && globalOverride.ToLowerInvariant() != "auto")
                return globalOverride;

            ShapeHint hint = GetShapeHint(assetId);

            return hint switch
            {
                ShapeHint.Cylinder => "lathe",
                ShapeHint.OrganicBlob => "organicblob",
                ShapeHint.LongX => "extruded",
                ShapeHint.LongZ => "extruded",
                ShapeHint.Tall => "lathe",
                ShapeHint.Flat => "pertexel",
                ShapeHint.Mirror => "balloon",
                ShapeHint.Auto => "balloon",
                _ => "balloon",
            };
        }
    }
}
