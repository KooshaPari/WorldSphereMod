using CompoundSpheres;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using WorldSphereMod.NewCamera;
using static WorldSphereMod.TileMapToSphere.TileMapToSphere;
namespace WorldSphereMod.TileMapToSphere
{
    [HarmonyPatch(typeof(ZoneCamera), nameof(ZoneCamera.update))]
    class getzone3D
    {
        static ZoneCamera ZoneCamera => World.world.zone_camera;
        static bool Prefix()
        {
            if (Core.IsWorld3D)
            {
                UpdateZones();
                return false;
            }
            return true;
        }
        static void UpdateZones()
        {
            ZoneCamera.clear();
            int CameraX = (int)CameraManager.Position.x / 8;
            Core.Sphere.GetCamerRange(out int Min, out int Max);
            Min /= 8;
            Max /= 8;
            for (int i = Min; i <= Max; i++)
            {
                int I = (int)Tools.MathStuff.Wrap(CameraX, i, ZoneCamera._zone_manager.zones_total_x);
                for(int j = 0; j < ZoneCamera._zone_manager.zones_total_y; j++)
                {
                    TileZone tZone = ZoneCamera._zone_manager.getZone(I, j);
                    if(tZone == null)
                    {
                        continue;
                    }
                    ZoneCamera._visible_zones.Add(tZone);
                    tZone.visible = true;
                    if (i == (Min+Max)/2 && j == ZoneCamera._zone_manager.zones_total_y / 2)
                    {
                        tZone.visible_main_centered = true;
                    }
                }
            }
        }
    }
    [HarmonyPatch(typeof(WorldTilemap), nameof(WorldTilemap.generate))]
    class Generate3D {
        static void Prefix(int pCount)
        {
            Regenerate(pCount);
        }
        static void Regenerate(int pCount)
        {
            ScaleQueue = new TileQueue(pCount);
            ColorQueue = new TileQueue(pCount);
            TextureQueue = new TileQueue(pCount);
        }
    }
    [HarmonyPatch(typeof(WorldTilemap), nameof(WorldTilemap.addToQueueToRedraw))]
    class Queue3D
    {
        static bool Prefix(WorldTile pTile)
        {
            if (Core.GeneratingSphere)
            {
                return false;
            }
            if (Core.IsWorld3D)
            {
                AddToQueue(pTile);
                return false;
            }
            return true;
        }
        static void Postfix()
        {
            if (Core.GeneratingSphere)
            {
                return;
            }
            if (Core.IsWorld3D && Core.savedSettings.BiomeBlending)
            {
                TileMapToSphere.MarkBiomeBlendDirty();
            }
        }
    }
    public class TileQueue
    {
        public HashSet<WorldTile>[] TilesByZone;
        public HashSet<TileZone> DirtyZones;
        public List<TileZone> ClearList;
        public int Count => DirtyZones.Count;
        public TileQueue(int pCount)
        {
            DirtyZones = new HashSet<TileZone>();
            ClearList = new List<TileZone>();
            TilesByZone = new HashSet<WorldTile>[pCount];
            for (int i = 0; i < pCount; i++)
            {
                TilesByZone[i] = new HashSet<WorldTile>(64);
            }
        }
        public bool HasZone(TileZone Zone)
        {
            return DirtyZones.Contains(Zone);
        }
        public void AddTile(WorldTile pTile)
        {
            TileZone tZone = pTile.zone;
            DirtyZones.Add(tZone);
            TilesByZone[tZone.id].Add(pTile);
        }
        public HashSet<WorldTile> GetTiles(TileZone pZone)
        {
            return TilesByZone[pZone.id];
        }
        public void ClearZone(TileZone pZone)
        {
            ClearList.Add(pZone);
        }
        public void Clear()
        {
            DirtyZones.ExceptWith(ClearList);
            ClearList.Clear();
        }
        public void Dispose()
        {
            DirtyZones.Clear();
            ClearList.Clear();
        }
    }
    [HarmonyPatch(typeof(WorldTilemap), nameof(WorldTilemap.redrawTiles))]
    public static class TileMapToSphere
    {
        static bool _biomeBlendDirty;
        public static void AddToQueue(WorldTile pTile)
        {
            ScaleQueue.AddTile(pTile);
            TextureQueue.AddTile(pTile);
        }
        public static void AddToColorQueue(WorldTile pTile)
        {
            ColorQueue.AddTile(pTile);
        }
        public static TileQueue TextureQueue;
        public static TileQueue ColorQueue;
        public static TileQueue ScaleQueue;
        public static bool UpdateTextures => MapBox.isRenderGameplay();
        public static void CheckScale(WorldTile pTile)
        {
            int height = pTile.GetHeight();
            if (pTile.last_rendered_pos_tile.z != height)
            {
                AddTileToScaleQueue(pTile);
                pTile.last_rendered_pos_tile.z = height;
            }
        }
        public static void CheckTexture(WorldTile pTile)
        {
            TileTypeBase tTypeToDraw = pTile.DisplayedType();
            if (pTile.last_rendered_tile_type != tTypeToDraw)
            {
                pTile.last_rendered_tile_type = tTypeToDraw;
                AddTileToTextureQueue(pTile);
            }
        }
        static void Dispose()
        {
            ColorQueue?.Dispose();
            TextureQueue?.Dispose();
            ScaleQueue?.Dispose();
        }
        public static void MarkBiomeBlendDirty()
        {
            _biomeBlendDirty = true;
        }
        public static bool Prefix(bool pForceAll)
        {
            if (Core.GeneratingSphere && pForceAll)
            {
                Dispose();
                return false;
            }
            return true;
        }

        static void CheckScales(TileZone pZone)
        {
            if (!ScaleQueue.HasZone(pZone))
            {
                return;
            }
            HashSet<WorldTile> tTiles = ScaleQueue.GetTiles(pZone);
            foreach (WorldTile tTile in tTiles)
            {
                CheckScale(tTile);
            }
            tTiles.Clear();
            ScaleQueue.ClearZone(pZone);
        }
        static void CheckTextures(TileZone pZone)
        {
            if (!TextureQueue.HasZone(pZone))
            {
                return;
            }
            HashSet<WorldTile> tTiles = TextureQueue.GetTiles(pZone);
            foreach (WorldTile tTile in tTiles)
            {
                CheckTexture(tTile);
            }
            tTiles.Clear();
            TextureQueue.ClearZone(pZone);
        }
        static void CheckColors(TileZone pZone)
        {
            if (!ColorQueue.HasZone(pZone))
            {
                return;
            }
            HashSet<WorldTile> tTiles = ColorQueue.GetTiles(pZone);
            foreach (WorldTile tTile in tTiles)
            {
                CheckColor(tTile);
            }
            tTiles.Clear();
            ColorQueue.ClearZone(pZone);
        }
        static void CheckColor(WorldTile tTile)
        {
            AddTileToColorQueue(tTile);
        }
        static void checkZoneToRender(TileZone pZone)
        {
            CheckScales(pZone);
            if (UpdateTextures)
            {
                CheckTextures(pZone);
            }
            else
            {
                CheckColors(pZone);
            }
        }
        public static void Redraw3DTiles()
        {
            if (TextureQueue.Count == 0 && ScaleQueue.Count == 0 && ColorQueue.Count == 0)
            {
                return;
            }
            for (int iZone = 0; iZone < World.world.zone_camera._visible_zones.Count; iZone++)
            {
                TileZone tZone2 = World.world.zone_camera._visible_zones[iZone];
                checkZoneToRender(tZone2);
            }
            Finish();
            if (_biomeBlendDirty && Core.savedSettings.BiomeBlending)
            {
                _biomeBlendDirty = false;
                Core.Sphere.RefreshColors();
            }
        }
        static void Finish()
        {
            ScaleQueue.Clear();
            if (UpdateTextures)
            {
                TextureQueue.Clear();
            }
            else
            {
                ColorQueue.Clear();
            }
        }
        public static void AddTileToTextureQueue(WorldTile pTile)
        {
            SphereTile Tile = pTile.WorldToSphere();
            Core.Sphere.UpdateTexture(Tile);
        }
        public static void AddTileToScaleQueue(WorldTile pTile)
        {
            SphereTile Tile = pTile.WorldToSphere();
            Core.Sphere.UpdateScale(Tile);
        }
        public static void AddTileToColorQueue(WorldTile pTile)
        {
            SphereTile Tile = pTile.WorldToSphere();
            Core.Sphere.UpdateBaseLayer(Tile);
        }
    }
    [HarmonyPatch(typeof(WorldTilemap), nameof(WorldTilemap.enableTiles))]
    public class DisableTileMap
    {
        static void Prefix(ref bool pValue)
        {
            if (Core.IsWorld3D)
            {
                pValue = false;
            }
        }
    }
    [HarmonyPatch(typeof(MapBox), nameof(MapBox.renderStuff))]
    public static class RefreshSphere
    {
        static void render3DStuff()
        {
            QuantumSpriteManager.update();
            Bench.bench("redraw_tiles", "game_total", false);
            if (Core.IsWorld3D)
            {
                Redraw3DTiles();
            }
            else
            {
                World.world.tilemap.redrawTiles();
            }
            Bench.benchEnd("redraw_tiles", "game_total", false, 0L, false);
            Bench.bench("update_debug_texts", "game_total", false);
            World.world.updateDebugGroupSystem();
            Bench.benchEnd("update_debug_texts", "game_total", false, 0L, false);
            if (World.world._redraw_timer > 0f)
            {
                World.world._redraw_timer -= Time.deltaTime;
            }
            else
            {
                World.world._redraw_timer = 0.1f;
                Bench.bench("redraw_mini_map", "game_total", false);
                if (World.world.tiles_dirty.Count > 0)
                {
                    World.world.redrawMiniMap(false);
                }
                Bench.benchEnd("redraw_mini_map", "game_total", false, 0L, false);
                Bench.bench("Refresh Sphere", "game_total");
                if (Core.IsWorld3D)
                {
                    // Upstream terrain mesh source: SphereManager.Refresh* / Sphere.RefreshSphere().
                    Core.Sphere.RefreshSphere();
                }
                Bench.benchEnd("Refresh Sphere", "game_total");
            }
        }
        static bool Prefix()
        {
          render3DStuff();
          return false;
        }
    }
    public static class AddLayers
    {
        static MethodInfo GetPixel => AccessTools.Method(typeof(AddLayers), nameof(GetPixelArray));
        static MethodInfo SetPixel => AccessTools.Method(typeof(PixelArray), "set_Item");
        static FieldInfo Pixels => AccessTools.Field(typeof(Core.Sphere), nameof(Core.Sphere.CachedColors));
        static CodeMatch FindPixels => new CodeMatch((CodeInstruction instruction) => instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field && field.Name == "pixels");
        static CodeMatch FindStelem => new CodeMatch(OpCodes.Stelem);
        //same as the other one, but only for the world layer because it does not manage itself, Mapbox manages it (what a fucking wimp)
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.updateDirtyTile))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> WorldTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            try {
                CodeMatcher Matcher = new CodeMatcher(instructions, generator);
                while (Matcher.FindNext(FindPixels))
                {
                    Matcher.RemoveInstruction();
                    Matcher.Insert(new CodeInstruction(OpCodes.Callvirt, GetPixel));
                    Matcher.Advance(-2);
                    CodeInstruction instruct = Matcher.Instruction;
                    Matcher.Insert(new CodeInstruction(OpCodes.Ldsfld, Pixels));
                    Tools.MoveLabels(instruct, Matcher.Instruction);
                    Matcher.MatchForward(false, FindStelem);
                    if (Matcher.Pos < 0 || Matcher.IsInvalid) { global::UnityEngine.Debug.LogWarning("[WSM3D] WorldTranspiler: Stelem not found — partial transpile"); break; }
                    Matcher.RemoveInstruction();
                    Matcher.Insert(new CodeInstruction(OpCodes.Callvirt, SetPixel));
                }
                return Matcher.Instructions();
            } catch (System.Exception ex) { global::UnityEngine.Debug.LogWarning("[WSM3D] WorldTranspiler failed: " + ex.GetType().Name + " — returning original"); return instructions; }
        }
        //this transpiler replaces all references in a maplayer from its color array to a custom color array that tracks the changes made
        public static IEnumerable<CodeInstruction> MapLayerTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            try {
                CodeMatcher Matcher = new CodeMatcher(instructions, generator);
                while (Matcher.FindNext(FindPixels))
                {
                    Matcher.RemoveInstruction();
                    Matcher.Insert(new CodeInstruction(OpCodes.Callvirt, GetPixel));
                    Matcher.Advance(-1);
                    CodeInstruction instruct = Matcher.Instruction;
                    Matcher.Insert(new CodeInstruction(OpCodes.Ldsfld, Pixels));
                    Tools.MoveLabels(instruct, Matcher.Instruction);
                    Matcher.MatchForward(false, FindStelem);
                    if (Matcher.Pos < 0 || Matcher.IsInvalid) { global::UnityEngine.Debug.LogWarning("[WSM3D] MapLayerTranspiler: Stelem not found — partial transpile"); break; }
                    Matcher.RemoveInstruction();
                    Matcher.Insert(new CodeInstruction(OpCodes.Callvirt, SetPixel));
                }
                return Matcher.Instructions();
            } catch (System.Exception ex) { global::UnityEngine.Debug.LogWarning("[WSM3D] MapLayerTranspiler failed: " + ex.GetType().Name + " — returning original"); return instructions; }
        }

        public static PixelArray GetPixelArray(Dictionary<MapLayer, PixelArray> pixels, MapLayer layer)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException(nameof(pixels));
            }

            if (layer == null)
            {
                throw new ArgumentNullException(nameof(layer));
            }

            if (pixels.TryGetValue(layer, out PixelArray cached) && cached != null)
            {
                return cached;
            }

            cached = new PixelArray(layer);
            pixels[layer] = cached;
            return cached;
        }
        //why maxim
        public static IEnumerable<CodeInstruction> ZoneLayerTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            try {
                CodeMatcher Matcher = new CodeMatcher(instructions);
                Matcher.MatchForward(false, FindPixels);
                if (Matcher.Pos < 0 || Matcher.IsInvalid) { global::UnityEngine.Debug.LogWarning("[WSM3D] ZoneLayerTranspiler: pixels field not found — skipping"); return instructions; }
                Matcher.RemoveInstruction();
                Matcher.Insert(new CodeInstruction(OpCodes.Callvirt, GetPixel));
                Matcher.Advance(-1);
                CodeInstruction instruct = Matcher.Instruction;
                Matcher.Insert(new CodeInstruction(OpCodes.Ldsfld, Pixels));
                Tools.MoveLabels(instruct, Matcher.Instruction);
                while (Matcher.FindNext(FindStelem))
                {
                    Matcher.RemoveInstruction();
                    Matcher.Insert(new CodeInstruction(OpCodes.Callvirt, SetPixel));
                }
                return Matcher.Instructions();
            } catch (System.Exception ex) { global::UnityEngine.Debug.LogWarning("[WSM3D] ZoneLayerTranspiler failed: " + ex.GetType().Name + " — returning original"); return instructions; }
        }
        //why the fuck maxim
        public static IEnumerable<CodeInstruction> AVerySpecificTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            try {
                CodeMatcher Matcher = new CodeMatcher(instructions);
                Matcher.MatchForward(false, new CodeMatch(OpCodes.Ldelem));
                if (Matcher.Pos < 0 || Matcher.IsInvalid) { global::UnityEngine.Debug.LogWarning("[WSM3D] AVerySpecificTranspiler: Ldelem not found — skipping"); return instructions; }
                Matcher.RemoveInstruction();
                Matcher.Insert(new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(PixelArray), "get_Item")));
                return Matcher.Instructions();
            } catch (System.Exception ex) { global::UnityEngine.Debug.LogWarning("[WSM3D] AVerySpecificTranspiler failed: " + ex.GetType().Name + " — returning original"); return instructions; }
        }
        [HarmonyPatch(typeof(MapLayer), nameof(MapLayer.clear))]
        [HarmonyPrefix]
        static bool ClearPatch(MapLayer __instance)
        {
            // Null-guards: clearWorld fires during world generation BEFORE
            // Core.Sphere + CachedColors are initialized. Without these guards
            // an NRE here blocks the load sequence ('005: Becoming 2D!' loop).
            if (__instance == null || __instance.pixels == null) return false;
            if (Core.Sphere.CachedColors == null) return true;
            PixelArray buf;
            if (!Core.Sphere.CachedColors.TryGetValue(__instance, out buf) || buf == null) return true;
            try
            {
                if (__instance.pixels_to_update != null) __instance.pixels_to_update.Clear();
                UnityEngine.Color32 color = UnityEngine.Color.clear;
                int n = __instance.pixels.Length;
                for (int i = 0; i < n; i++) buf[i] = color;
                __instance.updatePixels();
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("[WSM3D] ClearPatch: " + ex.Message + " - falling back to vanilla");
                return true;
            }
            return false;
        }
        [HarmonyPatch(typeof(MapLayer), nameof(MapLayer.createTextureNew))]
        [HarmonyPrefix]
        static bool TextureNew3D(MapLayer __instance)
        {
            if (!(__instance.texture == null) && MapBox.width == __instance.textureWidth && MapBox.height == __instance.texture.height)
            {
                return true;
            }
            if (__instance.sprRnd.sprite != null && __instance.textureWidth != 0)
            {
                Texture2DStorage.addToStorage(__instance.sprRnd.sprite, __instance.textureWidth, __instance.textureHeight);
            }
            __instance.textureWidth = MapBox.width;
            __instance.textureHeight = MapBox.height;
            __instance.sprRnd.sprite = Texture2DStorage.getSprite(__instance.textureWidth, __instance.textureHeight);
            __instance.texture = __instance.sprRnd.sprite.texture;
            __instance.textureID = __instance.texture.GetHashCode();
            int num = __instance.texture.height * __instance.texture.width;
            Color32 color = Color.clear;
            __instance.pixels = new Color32[num];
            // Lazy-init Core.Sphere.CachedColors entry for this MapLayer if
            // missing. User saw pale-blue blank world because this loop NREd
            // when CachedColors[__instance] was null/missing, aborting MapBox
            // texture init mid-recreateSizes → terrain tiles render with no
            // texture → pale-blue fallback.
            if (Core.Sphere.CachedColors == null)
            {
                Core.Sphere.CachedColors = new System.Collections.Generic.Dictionary<MapLayer, PixelArray>();
            }
            if (!Core.Sphere.CachedColors.TryGetValue(__instance, out PixelArray cached) || cached == null)
            {
                PixelArray.AddLayer(__instance, 0);
                cached = Core.Sphere.CachedColors[__instance];
            }
            for (int i = 0; i < num; i++)
            {
                cached[i] = color;
            }
            __instance.updatePixels();
            return false;
        }
        [HarmonyPatch(typeof(MapLayer), nameof(MapLayer.updatePixels))]
        [HarmonyPrefix]
        static bool DontUpdate(MapLayer __instance)
        {
            return !Core.IsWorld3D;
        }
    }
    public class PixelArray
    {
        public static void AddLayer(MapLayer Layer, int I)
        {
            if (!Core.IsWorld3D)
            {
                return;
            }
            if (Layer == null || World.world == null || World.world.tiles_list == null || I < 0)
            {
                return;
            }
            if (Layer.IsBase())
            {
                AddToColorQueue(World.world.tiles_list[I]);
                return;
            }
            try
            {
                Core.Sphere.UpdateLayer(World.world.tiles_list[I].WorldToSphere());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WSM3D] PixelArray.AddLayer failed for {Layer?.name ?? "<null>"}[{I}]: {ex.Message}");
            }
        }
        MapLayer Layer;
        public PixelArray(MapLayer Layer)
        {
            this.Layer = Layer;
        }
        void Set(int I, Color32 Color)
        {
            if (Layer == null || Layer.pixels == null || I < 0 || I >= Layer.pixels.Length)
            {
                return;
            }
            //somehow more laggy then updating tiles which havent changed
           // if (!Layer.pixels[I].EqualsColor(Color))
            {
                Layer.pixels[I] = Color;
                AddLayer(Layer, I);
            }
        }
        public Color32 this[int x]
        {
            get { return Layer.pixels[x]; }
            set
            {
                Set(x, value);
            }
        }
    }
}
