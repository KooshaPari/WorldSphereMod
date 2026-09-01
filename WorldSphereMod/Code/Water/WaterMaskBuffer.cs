using UnityEngine;

namespace WorldSphereMod.Water
{
    public static class WaterMaskBuffer
    {
        public static float[]? Depths;
        public static bool[]? IsWaterTile;
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
            if (IsWaterTile == null || IsWaterTile.Length != count)
            {
                IsWaterTile = new bool[count];
            }
            // Sink mesh well BELOW shore tiles so the surface never clips above sand
            // or sits on top of the shoreline. TrueHeight(17)=2.0 (sea reference);
            // sand sits at ~2.2 so a -0.5 offset keeps water visibly under shore.
            SeaLevel = Tools.TrueHeight(17) - 0.5f;

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
                // Only true ocean/lake tiles get a water surface.  A sand or
                // ground tile that happens to be below sea level is still
                // shore — exclude it so the mesh doesn't overshoot the coast.
                var tt = t.main_type;
                bool isWater = tt != null
                    && (tt.liquid || tt.ocean)
                    && !tt.sand
                    && !tt.ground
                    && tileHeight <= SeaLevel;
                IsWaterTile[idx] = isWater;
                if (isWater && d > maxD) maxD = d;
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
            if (IsWaterTile == null) return false;
            if ((uint)tileIndex >= (uint)IsWaterTile.Length) return false;
            return IsWaterTile[tileIndex];
        }

        public static float MaxDepth()
        {
            return _maxDepth;
        }

        public static void Clear()
        {
            Depths = null;
            IsWaterTile = null;
            SeaLevel = 0f;
            _maxDepth = 0f;
        }
    }
}
