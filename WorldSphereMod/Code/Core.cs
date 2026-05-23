using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using CompoundSpheres;
using NeoModLoader.utils;
using NeoModLoader.constants;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static WorldSphereMod.CompoundSphereScripts;
using static HarmonyLib.AccessTools;
using WorldSphereMod.NewCamera;
using UnityEngine.Tilemaps;
using WorldSphereMod.General;
using System.Reflection;
using WorldSphereMod.Effects;
using System;
using WorldSphereMod.TileMapToSphere;
using WorldSphereMod.UI;
using WorldSphereMod.QuantumSprites;
using ai.behaviours;
using System.Linq;
namespace WorldSphereMod
{
        public static class Core
    {
        public static SavedSettings savedSettings = new SavedSettings();
        public static string SettingsVersion = "2.3";

        public static Harmony Patcher;
        internal static bool ClearVoxelMeshCacheOnFirstFrame;
        public static void SaveSettings()
        {
            string json = JsonConvert.SerializeObject(savedSettings, Formatting.Indented);
            File.WriteAllText($"{Paths.ModsConfigPath}/WorldSphereMod.json", json);
        }
        public static bool LoadSettings()
        {
            SavedSettings? loadedData;
            try
            {
                string raw = File.ReadAllText($"{Paths.ModsConfigPath}/WorldSphereMod.json");
                if (raw.Contains("\"TerrainSmoothing\"") && !raw.Contains("\"MountainSlopeSmoothing\""))
                {
                    try
                    {
                        JObject obj = JObject.Parse(raw);
                        if (obj["MountainSlopeSmoothing"] == null && obj["TerrainSmoothing"] != null)
                        {
                            obj["MountainSlopeSmoothing"] = obj["TerrainSmoothing"]!.Value<bool>();
                        }
                        raw = obj.ToString();
                    }
                    catch
                    {
                        // Fall back to the raw JSON below if the migration parse fails.
                    }
                }
                loadedData = JsonConvert.DeserializeObject<SavedSettings>(raw);
                if (loadedData == null) throw new FileLoadException();
            }
            catch
            {
                SaveSettings();
                return false;
            }
            // Version mismatch: keep the deserialized values (Json.NET will have filled
            // in the v1.5 fields it recognised and left the v2 fork additions at their
            // defaults). Bump Version forward and re-save so subsequent loads are clean.
            // This preserves the user's existing preferences across a v1.5 → v2.0 upgrade
            // rather than discarding them.
            if (loadedData.Version != SettingsVersion)
            {
                ApplySchemaVersionMigration(loadedData);
                loadedData.Version = SettingsVersion;
                savedSettings = loadedData;
                LogPhaseFlagDefaults(savedSettings);
                SaveSettings();
                return true;
            }
            savedSettings = loadedData;
            LogPhaseFlagDefaults(savedSettings);
            return true;
        }

        static void ApplySchemaVersionMigration(SavedSettings loadedData)
        {
            var currentDefaults = new SavedSettings();
            var phaseFlags = typeof(PhaseAttribute).Assembly
                .GetTypes()
                .Select(type => type.GetCustomAttribute<PhaseAttribute>())
                .Where(phaseAttr => phaseAttr != null)
                .Select(phaseAttr => phaseAttr!.SettingsFlagName)
                .Distinct();

            foreach (var phaseFlag in phaseFlags)
            {
                if (string.IsNullOrWhiteSpace(phaseFlag))
                {
                    continue;
                }

                var field = typeof(SavedSettings).GetField(phaseFlag);
                if (field == null || field.FieldType != typeof(bool))
                {
                    continue;
                }

                field.SetValue(loadedData, field.GetValue(currentDefaults));
            }
        }

        static void LogPhaseFlagDefaults(SavedSettings loadedData)
        {
            var currentDefaults = new SavedSettings();
            var phaseFlags = typeof(PhaseAttribute).Assembly
                .GetTypes()
                .Select(type => type.GetCustomAttribute<PhaseAttribute>())
                .Where(phaseAttr => phaseAttr != null)
                .Select(phaseAttr => phaseAttr!.SettingsFlagName)
                .Distinct();

            foreach (var phaseFlag in phaseFlags)
            {
                if (string.IsNullOrWhiteSpace(phaseFlag))
                {
                    continue;
                }

                var field = typeof(SavedSettings).GetField(phaseFlag);
                if (field == null || field.FieldType != typeof(bool))
                {
                    continue;
                }

                bool loaded = (bool)field.GetValue(loadedData)!;
                bool defaults = (bool)field.GetValue(currentDefaults)!;
                Debug.Log($"[WSM3D] Settings sanity: {phaseFlag} loaded={loaded} default={defaults}");
            }
        }

        // go go gadget un-box my worldbox
        public static void Init()
        {
            ClearVoxelMeshCacheOnFirstFrame = true;
            InitProfiler.Measure("LoadSettings", () => LoadSettings());
            InitProfiler.Measure("WorldSphereTab.Begin", () => WorldSphereTab.Begin());
            InitProfiler.Measure("DimensionConverter.Prepare", () => DimensionConverter.Prepare());
            InitProfiler.Measure("Patch", () => Patch());
            try { WorldSphereMod.Voxel.VoxelMeshCache.Clear(); } catch { }
            // Gated: McPack bundle competes with worldsphere main bundle (NML's
            // AssetBundleUtils throws NRE on duplicate file IDs). Opt-in only.
            if (Core.savedSettings != null && Core.savedSettings.EnableMcPackTextures)
            {
                InitProfiler.Measure("McPackLoader.Initialize", () =>
                {
                    WorldSphereMod.Textures.McPackLoader.Initialize();
                });
            }
            InitProfiler.Measure("Lighting.SunDriver.Init", () =>
            {
                if (Core.IsWorld3D)
                {
                    WorldSphereMod.Lighting.SunDriver.Init();
                }
            });
            DoSomeOtherStuff();
        }

        public static void ApplyPhaseToggle(string flagName, bool newValue)
        {
            PhasePatchManager.ApplyPhaseToggle(flagName, newValue);
            // Invalidate voxel cache + material when render-affecting flags change.
            // Without this, toggling VoxelEntities / ProceduralBuildings / etc has no
            // visible delta because cached meshes + materials persist across the
            // setting change (user-reported "all switches activate nothing").
            if (flagName == nameof(SavedSettings.VoxelEntities) ||
                flagName == nameof(SavedSettings.ProceduralBuildings) ||
                flagName == nameof(SavedSettings.CrossedQuadFoliage) ||
                flagName == nameof(SavedSettings.MeshWater) ||
                flagName == nameof(SavedSettings.SkeletalAnimation))
            {
                try { WorldSphereMod.Voxel.VoxelMeshCache.Clear(); } catch { }
                try { WorldSphereMod.Voxel.VoxelRender.Reset(); } catch { }
            }
            if (flagName == nameof(SavedSettings.HighShadows))
            {
                WorldSphereMod.Lighting.SunDriver.ApplyShadowSettings();
            }
            if (flagName == nameof(SavedSettings.WorldspaceUI) && newValue)
            {
                WorldSphereMod.Worldspace.WorldUIRenderer.EnsureCreated();
            }
            if (flagName == nameof(SavedSettings.MountainSlopeSmoothing))
            {
                if (newValue)
                {
                    WorldSphereMod.Terrain.MountainSlopeSurface.EnsureActive();
                }
                else
                {
                    WorldSphereMod.Terrain.MountainSlopeSurface.Destroy();
                }
            }
            if (flagName == nameof(SavedSettings.DayNightCycle) && newValue)
            {
                WorldSphereMod.Lighting.TimeOfDay.EnsureCreated();
                WorldSphereMod.Lighting.ProceduralSky.EnsureCreated();
            }
            if (flagName == nameof(SavedSettings.DayNightCycle) && !newValue)
            {
                WorldSphereMod.Lighting.ProceduralSky.ApplySetting(false);
            }
            if (flagName == nameof(SavedSettings.HdrSkybox))
            {
                WorldSphereMod.Lighting.CubemapLighting.ApplySetting(newValue);
            }
            if (flagName == nameof(SavedSettings.ColorGradingLut))
            {
                WorldSphereMod.Lighting.ColorGradingLUT.ApplySetting(newValue);
            }
            if (flagName == nameof(SavedSettings.SSAOEnabled))
            {
                WorldSphereMod.PostFx.ScreenSpaceAO.ApplySetting(newValue);
            }
            if (flagName == nameof(SavedSettings.SSGIEnabled))
            {
                WorldSphereMod.PostFx.ScreenSpaceGI.ApplySetting(newValue);
            }
            if (flagName == nameof(SavedSettings.WeatherRain) ||
                flagName == nameof(SavedSettings.WeatherSnow) ||
                flagName == nameof(SavedSettings.WeatherLightning))
            {
                if (Core.savedSettings.WeatherRain || Core.savedSettings.WeatherSnow || Core.savedSettings.WeatherLightning)
                {
                    WorldSphereMod.Weather.WeatherDriver.EnsureCreated();
                }
                else
                {
                    WorldSphereMod.Weather.WeatherDriver.Teardown();
                }
            }
    }

        static void DoSomeOtherStuff()
        {
            Constants.PerpBuildings.Add("stockpile_acidproof", true);
            Constants.PerpBuildings.Add("stockpile_fireproof", true);
            Constants.PerpBuildings.Add("stockpile", true);
            Constants.PerpProjectiles.Add("arrow", true);

            AssetManager.hotkey_library.action_hotkeys = AssetManager.hotkey_library.action_hotkeys.AddToArray(AssetManager.hotkey_library.add(new HotkeyAsset()
            {
                id = "Perspective",
                default_key_1 = KeyCode.F5,
                check_window_not_active = true,
                ignore_mod_keys = true,
                allow_unit_control = true,
                check_controls_locked = false,
                just_pressed_action = delegate (HotkeyAsset _)
                {
                    AssetManager.powers.get("first_person").toggle_action("first_person");
                    PowerButtonSelector.instance.checkToggleIcons();
                }
            }));

        }
        // load the textures after mods are loaded incase some mods add new world tiles
        public static void PostInit()
        {
            Sphere.Prepare();
        }
        const string HarmonyID = "WorldSphereMod";
        //this mod makes the game 3D, of course im patching alot (rip compatibility)
        //literally the core function of the mod
        static void Patch()
        {

            Patcher = new Harmony(HarmonyID);

            // Conditional patching: types with [Phase] attribute are only patched if their
            // phase gate is enabled in SavedSettings. This avoids IL detour overhead for
            // disabled phases (~80-150ms per disabled phase at Init time).
            var types = typeof(PhaseAttribute).Assembly.GetTypes();
            foreach (var type in types)
            {
                var phaseAttr = type.GetCustomAttribute<PhaseAttribute>();
                var hasPatch = type.GetCustomAttribute<HarmonyPatch>() != null;
                if (phaseAttr != null)
                {
                    // Skip this type if its phase flag is off.
                    var flagField = typeof(SavedSettings).GetField(phaseAttr.SettingsFlagName);
                    if (flagField == null) continue; // Flag doesn't exist (shouldn't happen).
                    var flagValue = (bool)flagField.GetValue(savedSettings);
                    if (!flagValue) continue; // Phase is disabled, skip patching.
                }

                // Only patch this type if it has a [HarmonyPatch] attribute.
                if (hasPatch)
                {
                    Patcher.CreateClassProcessor(type).Patch();
                    if (phaseAttr != null)
                    {
                        PhasePatchManager.MarkTypePatched(type);
                    }
                }
            }

            Patcher.PatchAll(typeof(WorldSphereMod.Bridge.BridgePerFrameTick));
            Patcher.PatchAll(typeof(SphereControl));
            Patcher.PatchAll(typeof(Dist3D));
            Patcher.PatchAll(typeof(EffectPatches));
            Patcher.PatchAll(typeof(MovementEnhancement));
            Patcher.PatchAll(typeof(Drop3D));
            Patcher.PatchAll(typeof(FixCrabzilla));
            Patcher.PatchAll(typeof(AddLayers));
            Patcher.PatchAll(typeof(QuantumSpritePatches));
            Patcher.PatchAll(typeof(WorldLoop));
            Patcher.PatchAll(typeof(SourcePatches));

            MethodInfo WorldLoopPatch = Method(typeof(WorldLoop), nameof(WorldLoop.Tiles));
            Patcher.Patch(Method(typeof(GeneratorTool), nameof(GeneratorTool.getTile)), new HarmonyMethod(WorldLoopPatch));
            Patcher.Patch(Method(typeof(MapBox), nameof(MapBox.GetTile)), new HarmonyMethod(WorldLoopPatch));

            MethodInfo Lerp3DPatch = Method(typeof(Lerp3D), nameof(Lerp3D.Transpiler));
            Patcher.Patch(Method(typeof(PlayerControl), nameof(PlayerControl.clickedStart)), null, null, new HarmonyMethod(Lerp3DPatch));

            HarmonyMethod brushTranspiler = new HarmonyMethod(Method(typeof(BrushTranspiler), nameof(BrushTranspiler.Transpiler)));
            Patcher.Transpile(Method(typeof(MapAction), nameof(MapAction.applyTileDamage)), brushTranspiler);
            Patcher.Transpile(Method(typeof(MapBox), nameof(MapBox.loopWithBrush), new Type[] { typeof(WorldTile), typeof(BrushData), typeof(PowerActionWithID), typeof(string) }), brushTranspiler);
            Patcher.Transpile(Method(typeof(MapBox), nameof(MapBox.loopWithBrush), new Type[] { typeof(WorldTile), typeof(BrushData), typeof(PowerAction), typeof(GodPower) }), brushTranspiler);
            Patcher.Transpile(Method(typeof(BehWormDigEat), nameof(BehWormDigEat.loopWithBrush)), brushTranspiler);
            Patcher.Transpile(Method(typeof(MapBox), nameof(MapBox.loopWithBrushPowerForDropsRandom)), brushTranspiler);

            MethodInfo EffectPatch = Method(typeof(EffectPatches), nameof(EffectPatches.BasePatch));
            Patcher.Patch(Method(typeof(BaseEffect), nameof(BaseEffect.prepare), new Type[] { }), null, new HarmonyMethod(EffectPatch));
            Patcher.Patch(Method(typeof(BaseEffect), nameof(BaseEffect.prepare), new Type[] {typeof(WorldTile), typeof(float) }), null, new HarmonyMethod(EffectPatch));
            Patcher.Patch(Method(typeof(BaseEffect), nameof(BaseEffect.prepare), new Type[] {typeof(Vector2), typeof(float) }), null, new HarmonyMethod(EffectPatch));
            //may allah forgive me
            HarmonyMethod MapLayerTranspiler = new HarmonyMethod(Method(typeof(AddLayers), nameof(AddLayers.MapLayerTranspiler)));
            Patcher.Transpile(Method(typeof(DebugLayer), nameof(DebugLayer.drawBuildings)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(BurnedTilesLayer), nameof(BurnedTilesLayer.UpdateDirty)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(ConwayLife), nameof(ConwayLife.UpdateVisual)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(DebugLayer), nameof(DebugLayer.clear)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(DebugLayer), nameof(DebugLayer.drawCitizenJobs)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(DebugLayer), nameof(DebugLayer.drawConstructionTiles)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(DebugLayer), nameof(DebugLayer.drawProfession)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(DebugLayer), nameof(DebugLayer.drawTargetedBy)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(DebugLayer), nameof(DebugLayer.drawUnitKingdoms)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(DebugLayer), nameof(DebugLayer.drawUnitsInside)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(DebugLayer), nameof(DebugLayer.drawUnitTiles)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(DebugLayer), nameof(DebugLayer.fill), new Type[] {typeof(List<WorldTile>), typeof(Color), typeof(bool)}), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(DebugLayer), nameof(DebugLayer.fill), new Type[] { typeof(WorldTile[]), typeof(Color), typeof(bool) }), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(DebugLayerCursor), nameof(DebugLayerCursor.fill), new Type[] { typeof(List<WorldTile>), typeof(Color), typeof(bool) }), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(DebugLayerCursor), nameof(DebugLayerCursor.fill), new Type[] { typeof(WorldTile[]), typeof(Color), typeof(bool) }), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(DebugLayerCursor), nameof(DebugLayerCursor.drawIsland)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(ExplosionsEffects), nameof(ExplosionsEffects.UpdateDirty)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(FireLayer), nameof(FireLayer.UpdateDirty)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(LavaLayer), nameof(LavaLayer.drawLavaPixel)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(LavaLayer), nameof(LavaLayer.updateLava)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(PathFindingVisualiser), nameof(PathFindingVisualiser.showPath)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(PixelFlashEffects), nameof(PixelFlashEffects.UpdateDirty)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(UnitLayer), nameof(UnitLayer.UpdateDirty)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(WorldLayerEdges), nameof(WorldLayerEdges.redraw)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(WorldLayerEdges), nameof(WorldLayerEdges.redrawTile)), MapLayerTranspiler);
            Patcher.Transpile(Method(typeof(ZoneCalculator), nameof(ZoneCalculator.applyMetaColorsToZone)), AddLayers.ZoneLayerTranspiler);
            Patcher.Transpile(Method(typeof(ZoneCalculator), nameof(ZoneCalculator.colorZone)), AddLayers.ZoneLayerTranspiler);
            Patcher.Transpile(Method(typeof(ZoneCalculator), nameof(ZoneCalculator.colorZone)), AddLayers.AVerySpecificTranspiler);
            Patcher.Transpile(Method(typeof(ZoneCalculator), nameof(ZoneCalculator.applyMetaColorsToZoneFull)), AddLayers.ZoneLayerTranspiler);
            Patcher.Transpile(Method(typeof(ZoneCalculator), nameof(ZoneCalculator.applyMetaColorsToZoneFull)), AddLayers.AVerySpecificTranspiler);

            Patcher.Transpile(Method(typeof(Actor), nameof(Actor.updateMovement)), Move3D.Transpiler);
            Patcher.Transpile(Method(typeof(Actor), nameof(Actor.tryToAttack)), Move3D.Transpiler);
            Patcher.Transpile(Method(typeof(MapBox), nameof(MapBox.checkAttackFor)), Move3D.Transpiler);
            Patcher.Transpile(Method(typeof(Actor), nameof(Actor.updatePossessedMovementTowards)), Move3D.Transpiler);
            Patcher.Transpile(Method(typeof(CombatActionLibrary), nameof(CombatActionLibrary.getAttackTargetPosition)), Move3D.Transpiler);
            Patcher.Transpile(Method(typeof(MusicBoxContainerTiles), nameof(MusicBoxContainerTiles.calculatePan)), Move3D.Transpiler);

            HarmonyMethod previewPatch = new HarmonyMethod(Method(typeof(PreviewPatch), nameof(PreviewPatch.Prefix)));
            HarmonyMethod previewPatchpostfix = new HarmonyMethod(Method(typeof(PreviewPatch), nameof(PreviewPatch.Postfix)));
            Patcher.Patch(AccessTools.Method(typeof(PreviewHelper), nameof(PreviewHelper.convertMapToTexture)), previewPatch, previewPatchpostfix);
            Patcher.Patch(AccessTools.Method(typeof(PreviewHelper), nameof(PreviewHelper.getCurrentWorldPreview)), previewPatch, previewPatchpostfix);

            Patcher.Transpile(Method(typeof(MoveCamera), nameof(MoveCamera.zoomToBounds)), MinZoomTranspiler.Transpiler);
            Patcher.Transpile(Method(typeof(MoveCamera), nameof(MoveCamera.updateMobileCamera)), MinZoomTranspiler.Transpiler);

            Patcher.Transpile(Method(typeof(HeatRayEffect), nameof(HeatRayEffect.update)), DisableSettingPositions.Transpiler);

            //this is where the fun begins 
            DimensionConverter.ConvertPositions(Method(typeof(Boulder), nameof(Boulder.updateCurrentPosition)), 1);
            DimensionConverter.ConvertPositions(Method(typeof(Boulder), nameof(Boulder.actionLanded)));
            DimensionConverter.ConvertQuantum(Method(typeof(Santa), nameof(Santa.updatePosition)), DimensionConverter.YToZ);
            DimensionConverter.ConvertQuantum(Method(typeof(HeatRayEffect), nameof(HeatRayEffect.play)), DimensionConverter.ToQuantum);

            DimensionConverter.ConvertQuantum(Method(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawShadowsBuildings)), DimensionConverter.ToShadow);
            DimensionConverter.ConvertQuantum(Method(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawFires)), DimensionConverter.ToFire);
            DimensionConverter.ConvertQuantum(Method(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawShadowsUnit)), DimensionConverter.ToShadow);
            DimensionConverter.ConvertPositions(Method(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawUnitAttackRange)));
            DimensionConverter.ConvertPositions(Method(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawUnitSize)));
            DimensionConverter.ConvertPositions(Method(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawUnitsAvatars)));
            DimensionConverter.ConvertPositions(Method(typeof(QuantumSpriteLibrary), nameof(QuantumSpriteLibrary.drawLightAreas)));

            DimensionConverter.ConvertPositions(Method(typeof(GroupSpriteObject), nameof(GroupSpriteObject.setPosOnly), new Type[] {typeof(Vector2)}));
            DimensionConverter.ConvertPositions(Method(typeof(GroupSpriteObject), nameof(GroupSpriteObject.setPosOnly), new Type[] { typeof(Vector2).MakeByRefType() }));
            DimensionConverter.ConvertPositions(Method(typeof(GroupSpriteObject), nameof(GroupSpriteObject.setPosOnly), new Type[] { typeof(Vector3).MakeByRefType() }));
            DimensionConverter.ConvertPositions(Method(typeof(GroupSpriteObject), nameof(GroupSpriteObject.set), new Type[] { typeof(Vector2).MakeByRefType(), typeof(Vector3).MakeByRefType() }));
            DimensionConverter.ConvertQuantum(Method(typeof(GroupSpriteObject), nameof(GroupSpriteObject.set), new Type[] { typeof(Vector2).MakeByRefType(), typeof(float) }), DimensionConverter.ToQuantum);
            DimensionConverter.ConvertQuantum(Method(typeof(GroupSpriteObject), nameof(GroupSpriteObject.set), new Type[] { typeof(Vector3).MakeByRefType(), typeof(float) }), DimensionConverter.ToQuantumWithHeight);
            DimensionConverter.ConvertPositions(Method(typeof(GroupSpriteObject), nameof(GroupSpriteObject.set), new Type[] { typeof(Vector3).MakeByRefType(), typeof(Vector2).MakeByRefType() }));
            DimensionConverter.ConvertPositions(Method(typeof(GroupSpriteObject), nameof(GroupSpriteObject.set), new Type[] { typeof(Vector3).MakeByRefType(), typeof(Vector3).MakeByRefType() }));
        } 
        public static void Become3D()
        {
            // Wrap each step in try/catch so a NRE in any single subsystem (notably
            // CompoundSpheres.SphereManager.Init at line 107 — Melvin's library
            // throws when called too early or with stale state) does not break the
            // SmoothLoader retry loop.
            try { Sphere.Begin(); }
            catch (System.Exception ex) { UnityEngine.Debug.LogError("[WSM3D] Sphere.Begin failed: " + ex); }
            try { CameraManager.MakeCamera3D(); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[WSM3D] MakeCamera3D failed: " + ex.Message); }
            try { WorldSphereMod.Lighting.CubemapLighting.EnsureCreated(); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[WSM3D] CubemapLighting failed: " + ex.Message); }
            try { WorldSphereMod.Lighting.ColorGradingLUT.EnsureCreated(); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[WSM3D] ColorGradingLUT failed: " + ex.Message); }
            // Drive ProceduralSky.EnsureCreated AFTER IsWorld3D has flipped true.
            // The earlier InitProfiler-wrapped call during Mod.Init early-returns
            // because Core.IsWorld3D is still false at that point — so the skybox
            // bind never happens. This is the diagnosed root cause of "HDR cubemap
            // not visible despite flag=true" 2026-05-22.
            try { WorldSphereMod.Lighting.ProceduralSky.EnsureCreated(); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[WSM3D] ProceduralSky.EnsureCreated failed: " + ex.Message); }
            try { Do3DStuff(); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[WSM3D] Do3DStuff failed: " + ex.Message); }
        }
        static void Do3DStuff()
        {
            World.world.heat_ray_fx.ray.transform.localPosition = Vector3.zero;
            QuantumSpriteLibrary.light_areas.color = new Color(1, 1, 1, 0.5f);
            World.world.heat_ray_fx.ray.transform.eulerAngles = new Vector3(180, 0, 0);
        }
        public static void Become2D()
        {
            Sphere.Finish();
            CameraManager.MakeCamera2D();
            do2DStuff();
        }
        static void do2DStuff()
        {
            QuantumSpriteLibrary.light_areas.color = new Color(1, 1, 1, 1f);
            World.world.heat_ray_fx.ray.transform.localPosition = new Vector3(0, 2000);
            World.world.heat_ray_fx.ray.transform.eulerAngles = Vector3.zero;
        }
        
        public static PixelFlashEffects FlashLayer => World.world.flash_effects;
        public static bool Generated = false;
        public static bool GeneratingSphere => savedSettings.Is3D && !Generated;
        public static bool IsWorld3D => Sphere.Exists;
        // the layer between the Mod and the compound sphere
        public static class Sphere
        {
            public static void AddShape(Shape shape)
            {
                Shapes.Add(shape);
            }
            public static Quaternion GetRotation(Vector2 position)
            {
                return CurrentShape.tileRotation(position);
            }
            public delegate Quaternion GetRot(Vector2 Pos);
            public struct Shape
            {
                public Shape(To2D to2d, To2DFast to2dfast, GetSphereTilePosition to3d, GetRot rot, Initiation init, GetCameraRange GetCameraRange, bool IsWrapped)
                {
                    this.To2D = to2d;
                    this.To2DFast = to2dfast;
                    this.To3D = to3d;
                    this.tileRotation = rot;
                    this.Inititation = init;
                    this.GetCameraRange = GetCameraRange;
                    this.IsWrapped = IsWrapped;
                }
                public bool IsWrapped;
                public To2D To2D;
                public To2DFast To2DFast;
                public GetSphereTilePosition To3D;
                public GetRot tileRotation;
                public Initiation Inititation;
                public GetCameraRange GetCameraRange;
            }
            public static bool IsWrapped => CurrentShape.IsWrapped;
            public static float Radius => Manager.Radius;
            public static int Width => Manager.Rows;
            public static int Height => Manager.Cols;
            public static Transform CenterCapsule => Manager.transform.GetChild(0);
            public static bool Exists => Manager != null;
            public static float HeightMult = 0;
            public static bool PerlinNoise = true;
            #region Fancy stuff
            static SphereManager Manager;
            static Mesh CompoundSphereMesh;
            static Material CompoundSphereMaterial;
            static Texture2DArray Textures;
            static SphereManagerSettings SphereManagerConfig;
            static Dictionary<Tile, int> TileIDS;
            #endregion
            public static List<MapLayer> BaseLayers;
            public static Dictionary<MapLayer, PixelArray> CachedColors;
            public delegate Vector3 To2D(SphereManager manager, float x, float y, float z);
            public delegate Vector2 To2DFast(SphereManager manager, float x, float y, float z);
            static Shape CurrentShape;
            public static void GetCamerRange(out int Min, out int Max)
            {
                CurrentShape.GetCameraRange(Sphere.Manager, out Min, out Max);
            }
            static List<Shape> Shapes = new List<Shape>()
            {
                new Shape(CylindricalToCartesian, CylindricalToCartesianFast, CartesianToCylindrical, CylindricalRotation, CylindricalInitiation, RenderRange, true), //cylinder
                new Shape(FlatToCartesian, FlatToCartesianFast, CartesianToFlat, FlatRotation, FlatInitiation, RenderRangeFlat, false)//flat
            };
            public static void Begin()
            {
                CurrentShape = Shapes[savedSettings.CurrentShape];
                HeightMult = savedSettings.TileHeight;
                PerlinNoise = Core.savedSettings.PerlinNoise;
                CreateSettings();
                int width = MapBox.width;
                int height = MapBox.height;
                // Guard: SphereManager.Init line 107 dereferences SphereTileMaterial
                // without null-check (pre-DLL fix). If material/mesh failed to load
                // from bundle, skip CreateSphereManager entirely -- world remains 2D
                // but doesn't pale-blue-crash.
                if (CompoundSphereMaterial == null || CompoundSphereMesh == null)
                {
                    UnityEngine.Debug.LogError("[WSM3D] Sphere.Begin: CompoundSphereMaterial or CompoundSphereMesh missing — skipping CreateSphereManager. Bundle load likely failed.");
                    return;
                }
                Manager = SphereManager.Creator.CreateSphereManager(width, height, SphereManagerConfig);
            }
            static Color32 GetBaseColor(int index)
            {
                Color32 dst = World.world.world_layer.pixels[index];

                int r = dst.r * dst.a;
                int g = dst.g * dst.a;
                int b = dst.b * dst.a;
                int a = dst.a;

                foreach (MapLayer layer in BaseLayers)
                {
                    Color32 src = layer.pixels[index];
                    if (src.a == 0) continue;

                    int invSrcA = 255 - src.a;

                    r = (src.r * src.a + r * invSrcA) / 255;
                    g = (src.g * src.a + g * invSrcA) / 255;
                    b = (src.b * src.a + b * invSrcA) / 255;
                    a = (src.a + a * invSrcA / 255);
                }

                return new Color32((byte)r, (byte)g, (byte)b, (byte)Mathf.Clamp(a, 0, 255));
            }
            static Color32 BlendBiomeColor(int index, Color32 fallback)
            {
                if (World.world == null || index < 0)
                {
                    return fallback;
                }

                WorldTile center = World.world.tiles_list[index];
                const int radius = 2;
                float totalWeight = 0f;
                float r = 0f;
                float g = 0f;
                float b = 0f;
                float a = 0f;

                for (int dy = -radius; dy <= radius; dy++)
                {
                    int y = center.y + dy;
                    if (y < 0 || y >= MapBox.height)
                    {
                        continue;
                    }

                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        float distance = Mathf.Sqrt((dx * dx) + (dy * dy));
                        if (distance > radius)
                        {
                            continue;
                        }

                        int x = center.x + dx;
                        if (Core.Sphere.IsWrapped)
                        {
                            x = (int)Tools.MathStuff.Wrap(x, 0, MapBox.width);
                        }
                        else if (x < 0 || x >= MapBox.width)
                        {
                            continue;
                        }

                        WorldTile sample = World.world.GetTileSimple(x, y);
                        if (sample == null)
                        {
                            continue;
                        }

                        Color32 sampleColor = GetBaseColor(sample.data.tile_id);
                        if (sampleColor.a == 0)
                        {
                            continue;
                        }

                        float weight = 1f - (distance / (radius + 1f));
                        if (weight <= 0f)
                        {
                            continue;
                        }

                        totalWeight += weight;
                        r += sampleColor.r * weight;
                        g += sampleColor.g * weight;
                        b += sampleColor.b * weight;
                        a += sampleColor.a * weight;
                    }
                }

                if (totalWeight <= 0f)
                {
                    return fallback;
                }

                return new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(r / totalWeight), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(g / totalWeight), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(b / totalWeight), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(a / totalWeight), 0, 255));
            }
            public static Color32 GetColor(int index)
            {
                Color32 baseColor = GetBaseColor(index);
                if (!Core.savedSettings.BiomeBlending)
                {
                    return baseColor;
                }
                return BlendBiomeColor(index, baseColor);
            }
            public static Color GetAddedColor(int Index)
            {
                return FlashLayer.pixels[Index].Normalised();
            }
            public static void UpdateScale(SphereTile Tile)
            {
                Manager.UpdateScale(Tile.X, Tile.Y);
            }
            public static void UpdateTexture(SphereTile Tile)
            {
                Manager.UpdateTexture(Tile.X, Tile.Y);
            }
            public static void RefreshSphere()
            {
                Manager.RefreshScales();
                Manager.RefreshTextures();
                Manager.RefreshCustom("AddedColors");
                RefreshColors();
            }
            public static void RefreshColors()
            {
                if (Manager == null)
                {
                    return;
                }
                Manager.RefreshColors();
            }
            public static void UpdateLayer(SphereTile Tile)
            {
                Manager.UpdateCustom("AddedColors", Tile.X, Tile.Y);
            }
            public static void UpdateBaseLayer(SphereTile Tile)
            {
                Manager.UpdateColor(Tile.X, Tile.Y);
            }
            public static void Finish()
            {
                if (Manager == null || Manager.gameObject == null)
                {
                    return;
                }
                Manager.Destroy();
            }
            public static Vector3 TilePosWithHeight(float X, float Y, float Z)
            {
                return CurrentShape.To2D(Manager, X, Y, Z);
            }
            public static Vector2 TilePos(float X, float Y, float Z)
            {
                return CurrentShape.To2DFast(Manager, X, Y, Z);
            }
            public static void DrawTiles(int CameraX)
            {
                Manager.DrawTiles(CameraX);
            }
            static void CreateCachedColors()
            {
                CachedColors = new Dictionary<MapLayer, PixelArray>();
                foreach (var layer in World.world._map_layers)
                {
                    CachedColors.Add(layer, new PixelArray(layer));
                }
            }
            public static Vector3 SpherePos(float X, float Y, float Height = 0)
            {
                return Manager.SphereTilePosition(X, Y, Height);
            }
            public static void Prepare()
            {
                LoadAssets();
                CreateTextures();
                BaseLayers = new List<MapLayer>(World.world._map_layers);
                BaseLayers.Remove(FlashLayer);
                CreateCachedColors();
            }
            public static int WorldTileTexture(WorldTile Tile)
            {
                Tile Graphic = World.world.tilemap.getVariation(Tile);
                if(Graphic == null)
                {
                    return 0;
                }
                if (TileIDS.TryGetValue(Graphic, out int ID)) {
                    return ID;
                }
                return 0;
            }
            static void LoadAssets()
            {
                WrappedAssetBundle ab = AssetBundleUtils.GetAssetBundle("worldsphere");
                if (ab == null)
                {
                    Debug.LogError("[WSM3D] AssetBundleUtils.GetAssetBundle('worldsphere') returned null — likely an NML duplicate-bundle conflict. Skipping LoadAssets. Mesh/material/skybox not available this session.");
                    return;
                }
                try { Mod.LogAssetBundleInventory(ab); }
                catch (System.Exception ex) { Debug.LogWarning("[WSM3D] LogAssetBundleInventory threw: " + ex.Message); }
                CompoundSphereMesh = ab.GetObject<Mesh>("assets/worldspheremod/compoundspheremesh.asset")
                    ?? ab.GetObject<Mesh>("assets/wsm3d/legacyassets/compoundspheremesh.asset");
                CompoundSphereMaterial = ab.GetObject<Material>("assets/worldspheremod/compoundspherematerial.mat")
                    ?? ab.GetObject<Material>("assets/wsm3d/legacyassets/compoundspherematerial.mat");
                // Null-guard each asset get so a missing SkyBox.mat in the
                // combined-bake bundle doesn't NRE here and trip NML's
                // post-init error handler (which disables the entire mod —
                // root cause of pale-blue/black-water/no-smoothing on
                // 2026-05-22).
                if (CompoundSphereMesh == null)
                    Debug.LogError("[WSM3D] CompoundSphereMesh missing from bundle.");
                if (CompoundSphereMaterial == null)
                    Debug.LogError("[WSM3D] CompoundSphereMaterial missing from bundle.");
                // Inspect CompoundSphereMaterial's shader. If its shader was
                // bundled corrupted (empty .name like the 4-of-6 broken
                // shaders above), the terrain tiles render as black
                // trapezoids — user-reported 2026-05-23. Reassign to
                // Standard with a tan _Color so terrain at least shows up.
                if (CompoundSphereMaterial != null)
                {
                    string shName = CompoundSphereMaterial.shader != null ? CompoundSphereMaterial.shader.name : "<null>";
                    Debug.Log($"[WSM3D] CompoundSphereMaterial.shader = '{shName}'");
                    if (CompoundSphereMaterial.shader == null || string.IsNullOrEmpty(shName))
                    {
                        Shader std = Shader.Find("Standard");
                        if (std != null)
                        {
                            CompoundSphereMaterial.shader = std;
                            CompoundSphereMaterial.color = new Color(0.55f, 0.50f, 0.40f, 1f);
                            try { CompoundSphereMaterial.SetColor("_BaseColor", new Color(0.55f, 0.50f, 0.40f, 1f)); } catch { }
                            Debug.LogWarning("[WSM3D] CompoundSphereMaterial had broken shader; reassigned to Standard with tan tint.");
                        }
                    }
                }
                var skyboxMat = ab.GetObject<Material>("assets/worldspheremod/SkyBox.mat")
                    ?? ab.GetObject<Material>("assets/wsm3d/legacyassets/skybox.mat");
                if (skyboxMat != null && skyboxMat.shader != null)
                {
                    CameraManager.Begin(skyboxMat.shader);
                }
                else
                {
                    Debug.LogError("[WSM3D] SkyBox.mat missing from bundle — CameraManager.Begin skipped; sky will fall back to default.");
                }
                if (CompoundSphereMaterial != null && LibraryMaterials.instance != null)
                {
                    LibraryMaterials.instance._night_affected_colors.Add(CompoundSphereMaterial);
                }

                // Force-load WSM3D/* shaders into a static cache. AssetBundle.
                // LoadAsset<Shader> returns the shader instance but does NOT
                // register it in Unity's global Shader.Find database (that
                // requires Always-Included Shaders in Graphics Settings).
                // So we stash the loaded references here, and consumers read
                // from LoadedShaders dict instead of relying on Shader.Find.
                try
                {
                    foreach (var shaderName in new[] { "OpaqueVertexColor", "GerstnerWater", "ScreenSpaceAO", "ColorGradingLUT", "ProceduralSky", "Impostor" })
                    {
                        string assetPath = $"assets/wsm3d/shaders/{shaderName.ToLowerInvariant()}.shader";
                        var sh = ab.GetObject<UnityEngine.Shader>(assetPath);
                        if (sh == null)
                        {
                            Debug.LogWarning($"[WSM3D] Shader not in bundle: {assetPath}");
                            continue;
                        }
                        // Reject corrupted shader assets: a Shader object whose
                        // .name is null/empty was emitted by Unity bake but failed
                        // to compile its passes. Caching it would route every
                        // consumer through a magenta-rendering instance. Leave
                        // these out of the dict so the consumer falls through to
                        // Shader.Find / Standard fallback.
                        if (string.IsNullOrEmpty(sh.name))
                        {
                            Debug.LogError($"[WSM3D] Shader '{shaderName}' loaded with empty name — bake produced corrupted asset, skipping LoadedShaders cache. Consumer will fall back.");
                            continue;
                        }
                        LoadedShaders[shaderName] = sh;
                        Debug.Log($"[WSM3D] Loaded shader from bundle: WSM3D/{shaderName} -> {sh.name}");
                    }
                }
                catch (System.Exception ex) { Debug.LogWarning("[WSM3D] Shader load: " + ex.Message); }
            }

            // Static cache of bundle-loaded WSM3D/* shaders. Consumers look
            // here BEFORE Shader.Find — AssetBundle shaders aren't auto-
            // registered in Unity's global lookup, so Shader.Find returns
            // null for them unless they're also Always-Included.
            public static readonly System.Collections.Generic.Dictionary<string, UnityEngine.Shader> LoadedShaders =
                new System.Collections.Generic.Dictionary<string, UnityEngine.Shader>();
            public static SphereTile GetTile(int X, int Y)
            {
                return Manager[X, Y];
            }
            static void CreateSettings()
            {
                SphereManagerConfig = new SphereManagerSettings(
                    CurrentShape.Inititation,
                    CurrentShape.To3D,
                    delegate(SphereTile tile) { return CurrentShape.tileRotation(tile.Position); },
                    SphereTileScale,
                    SphereTileColor,
                    SphereTileTexture,
                    getdisplaymode,
                    Textures,
                    CompoundSphereMesh,
                    CompoundSphereMaterial,
                    CurrentShape.GetCameraRange,
                    new List<IBufferData>() { new CustomBufferData<Vector3>("AddedColors", 12, SphereTileAddedColor) }
               );
            }
            static void CreateTextures()
            {
                List<Sprite> Sprites = new List<Sprite>();
                TileIDS = new Dictionary<Tile, int>();
                foreach (TileType type in AssetManager.tiles.list)
                {
                    AddTile(type);
                }
                foreach (TopTileType type in AssetManager.top_tiles.list)
                {
                    AddTile(type);
                }
                Textures = new Texture2DArray(8, 8, Sprites.Count, TextureFormat.RGBA32, true, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                for (int i = 0; i < Sprites.Count; i++)
                {
                    Textures.SetPixels32(GetTruePixels(Sprites[i]), i);
                }
                Textures.Apply();
                void AddTile(TileTypeBase Tile)
                {
                    TileSprites sprites = Tile.sprites;
                    if (sprites == null)
                    {
                        return;
                    }
                    foreach (Tile tile in sprites._tiles)
                    {
                        if (TileIDS.TryAdd(tile, Sprites.Count))
                        {
                            Sprites.Add(tile.sprite);
                        }
                    }
                }
                Color32[] GetTruePixels(Sprite sprite)
                {
                    if (sprite.texture.width > 8 || sprite.texture.height > 8)
                    {
                        //seperate a sprite from its atlas
                        //this shit took me hours to solve
                        return sprite.PixelsFromSpriteAtlas();
                    }
                    return Tools.ExpandArray(sprite.texture.GetPixels32(), 64);
                }
            }
        }
    }
}
