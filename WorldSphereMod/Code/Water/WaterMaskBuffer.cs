using UnityEngine;

namespace WorldSphereMod.Water
{
    public static class WaterMaskBuffer
    {
        public static float[]? Depths;
        public static float SeaLevel;
        static float _maxDepth;

        public static void RebuildMask()
        {
            int width = MapBox.width;
            int height = MapBox.height;
            int count = width * height;
            if (Depths == null || Depths.Length != count)
            {
                Depths = new float[count];
            }
            // Nudge sea level down by 0.15 so waves don't clip above shoreline tiles.
            // TrueHeight(17) = 2.0 (ice/sea reference); sand is 2.2. The small offset
            // keeps the mesh surface just below the terrain-water boundary.
            SeaLevel = Tools.TrueHeight(17) - 0.15f;

            WorldTile[] tiles = World.world.tiles_list;
            int n = tiles.Length;
            float maxD = 0f;
            for (int i = 0; i < n; i++)
            {
                WorldTile t = tiles[i];
                if (t == null) continue;
                int idx = t.data.tile_id;
                if ((uint)idx >= (uint)count) continue;
                float tileHeight = Tools.TrueHeight(t.GetHeight(), t.main_type.render_z);
                float depth = SeaLevel - tileHeight;
                float d = depth > 0f ? depth : 0f;
                Depths[idx] = d;
                if (d > maxD) maxD = d;
            }
            _maxDepth = maxD;
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

        public static float MaxDepth()
        {
            return _maxDepth;
        }

        public static void Clear()
        {
            Depths = null;
            SeaLevel = 0f;
            _maxDepth = 0f;
        }
    }
}
