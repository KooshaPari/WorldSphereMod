using System.Collections.Generic;

namespace WorldSphereMod.Import
{
    /// <summary>
    /// Curated Minecraft block texture names to WSM3D semantic texture classes.
    /// See docs/journeys/scratch/mc-texture-pack-importer-spec.md.
    /// </summary>
    public static class TexturePackRegistry
    {
        public static IReadOnlyDictionary<string, string> DefaultMappings { get; } =
            new Dictionary<string, string>
            {
                ["grass_block_top"] = "biome_grass",
                ["grass_block_side"] = "biome_grass_side",
                ["dirt"] = "biome_dirt",
                ["stone"] = "mountain_rock",
                ["cobblestone"] = "building_cobble",
                ["oak_planks"] = "building_plank",
                ["log_oak"] = "building_wood",
                ["water_still"] = "water_surface",
                ["water_flow"] = "water_surface",
                ["sand"] = "desert_sand",
                ["snow"] = "tundra_snow",
            };

        public static bool TryGetWsm3dClass(string mcBlockName, out string wsm3dClass)
        {
            if (string.IsNullOrWhiteSpace(mcBlockName))
            {
                wsm3dClass = string.Empty;
                return false;
            }

            return DefaultMappings.TryGetValue(mcBlockName.Trim(), out wsm3dClass!);
        }
    }
}
