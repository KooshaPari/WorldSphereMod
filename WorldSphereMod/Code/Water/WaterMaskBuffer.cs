using UnityEngine;

namespace WorldSphereMod.Water
{
    public static class WaterMaskBuffer
    {
        public static float[]? Depths;
        public static float SeaLevel;

        public static void RebuildMask()
        {
            int width = MapBox.width;
            int height = MapBox.height;
            int count = width * height;
            if (Depths == null || Depths.Length != count)
            {
                Depths = new float[count];
            }
            SeaLevel = Tools.TrueHeight(17);

            // tiles_list is the canonical flat WorldTile array; data.tile_id matches SphereTile.Index().
            var tiles = World.world.tiles_list;
            int n = tiles.Length;
            for (int i = 0; i < n; i++)
            {
                WorldTile t = tiles[i];
                if (t == null) continue;
                int idx = t.data.tile_id;
                if ((uint)idx >= (uint)count) continue;
                float tileHeight = Tools.TrueHeight(t.GetHeight(), t.main_type.render_z);
                float depth = SeaLevel - tileHeight;
                Depths[idx] = depth > 0f ? depth : 0f;
            }
        }

        public static float DepthAt(int tileIndex)
        {
            if (Depths == null) return 0f;
            if ((uint)tileIndex >= (uint)Depths.Length) return 0f;
            return Depths[tileIndex];
        }

        public static bool IsWater(int tileIndex)
        {
            if (Depths == null) return false;
            if ((uint)tileIndex >= (uint)Depths.Length) return false;
            return Depths[tileIndex] > 0f;
        }

        public static void Clear()
        {
            Depths = null;
            SeaLevel = 0f;
        }
    }
}
