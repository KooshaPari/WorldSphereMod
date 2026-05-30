using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using CompoundSpheres;
using NeoModLoader.utils;
using NeoModLoader.constants;
using System.IO;
using Newtonsoft.Json;
using static WorldSphereMod.CompoundSphereScripts;
using static HarmonyLib.AccessTools;
using WorldSphereMod.NewCamera;
using UnityEngine.Tilemaps;
using WorldSphereMod.General;
using System.Reflection;
using Debug = UnityEngine.Debug;
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
        /// <summary>True when no settings file existed at load time (fresh install).</summary>
        public static bool IsFirstInstall { get; private set; }
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
                if (!SavedSettingsJson.TryDeserialize(raw, out loadedData) || loadedData == null)
                {
                    throw new FileLoadException();
                }
            }
            catch
            {
                IsFirstInstall = true;
                savedSettings.VoxelEntities = true;
                SavedSettings.ApplyPhaseDefaults(savedSettings);
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
                // Temporary override (see comment below at normal-load path).
                savedSettings.SkeletalAnimation = false;
                LogPhaseFlagDefaults(savedSettings);
                SaveSettings();
                return true;
            }
            savedSettings = loadedData;
            // Temporary override: the game's save/load cycle can flip SkeletalAnimation
            // back to true even though our code default is false. Force it off after every
            // load until Phase 6 is stable and we promote the default to true.
            savedSettings.SkeletalAnimation = false;
            LogPhaseFlagDefaults(savedSettings);
            return true;
        }

        static void ApplySchemaVersionMigration(SavedSettings loadedData)
        {
            SavedSettings.ApplyPhaseDefaults(loadedData);

            // Preserve the user's CurrentShape across version bumps.
            // Only phase boolean flags are reset — numeric/scale/shape
            // settings are intentionally kept so the user's chosen mode
            // persists through upgrades.
        }

        static void LogPhaseFlagDefaults(SavedSettings loadedData)
        {
            var currentDefaults = new SavedSettings();
            foreach (var phaseFlag in SavedSettingsPhaseFlags())
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

        static IEnumerable<string> SavedSettingsPhaseFlags()
        {
            return typeof(PhaseAttribute).Assembly
                .GetTypes()
                .Select(type => type.GetCustomAttribute<PhaseAttribute>())
                .Where(phaseAttr => phaseAttr != null)
                .Select(phaseAttr => phaseAttr!.SettingsFlagName)
                .Distinct();
        }

        // go go gadget un-box my worldbox
        public static void Init()
        {
            ClearVoxelMeshCacheOnFirstFrame = true;
            InitProfiler.Measure("LoadSettings", () => LoadSettings());
            InitProfiler.Measure("WorldSphereTab.Begin", () => WorldSphereTab.Begin());
            InitProfiler.Measure("DimensionConverter.Prepare", () => DimensionConverter.Prepare());
            InitProfiler.Measure("Patch", () => Patch());
            // Load AssetBundle/shaders/mesh/material eagerly during Init so
            // they are available even when NML skips PostInit (save loaded
            // before post-init phase). World-dependent parts of Prepare run
            // later in PostInit or on the first VoxelFrameDriver tick.
            InitProfiler.Measure("Sphere.PrepareAssets", () =>
            {
                try { Sphere.PrepareAssets(); }
                catch (System.Exception ex) { Debug.LogError($"[WSM3D] Sphere.PrepareAssets FAILED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); }
            });
            try { WorldSphereMod.Voxel.VoxelMeshCache.Clear(); } catch { }
            // Gated: McPack bundle competes with worldsphere main bundle (NML's
            // AssetBundleUtils throws NRE on duplicate file IDs). Opt-in only.
            if (Core.savedSettings != null && Core.savedSettings.EnableMcPackTextures)
            {
                InitProfiler.Measure("TexturePackImporter.ImportAtLoad", () =>
                {
                    var importResult = WorldSphereMod.Import.TexturePackImporter.TryImportAtLoad();
                    try
                    {
                        WorldSphereMod.Textures.McPackLoader.Initialize(importResult.ManifestStubPath);
                    }
                    catch { /* do not block world startup */ }
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
            // ScheduleBecome3D retry loop REMOVED — it fired at the wrong time
            // during save loads, causing "Cols And Rows must be above 0" errors
            // or infinite re-queuing. The reliable path is General.cs
            // SphereControl.CreateSphere (Postfix on MapBox.finishMakingWorld),
            // which fires for both new-world generation and save loads.
        }

        public static void ApplyPhaseToggle(string flagName, bool newValue)
        {
            WorldSphereMod.Worldspace.PhaseToast.EnsureCreated();
            try
            {
                PhasePatchManager.ApplyPhaseToggle(flagName, newValue);
            }
            catch (System.Exception ex)
            {
                // Log but do NOT return — MonoBehaviour drivers (SunDriver,
                // TimeOfDay, PostFxController, WeatherDriver) are toggled by the
                // specific handlers below, not by Harmony. Returning here would
                // block those drivers from enabling/disabling.
                string reason = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                Debug.LogWarning($"[WSM3D] PhasePatchManager failed for {flagName}: {reason}\n{ex}");
            }

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
            try
            {
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
                    if (newValue) WorldSphereMod.Lighting.ProceduralSky.EnsureCreated();
                    else if (!Core.savedSettings.DayNightCycle) WorldSphereMod.Lighting.ProceduralSky.ApplySetting(false);
                }
                if (flagName == nameof(SavedSettings.PostFX))
                {
                    WorldSphereMod.PostFx.WSM3DPostStack.ApplySetting(newValue);
                }
                if (flagName == nameof(SavedSettings.ColorGradingLut) ||
                    flagName == nameof(SavedSettings.SSAOEnabled) ||
                    flagName == nameof(SavedSettings.SSAOQuality) ||
                    flagName == nameof(SavedSettings.SSGIEnabled) ||
                    flagName == nameof(SavedSettings.BloomEnabled) ||
                    flagName == nameof(SavedSettings.ACESTonemapping))
                {
                    WorldSphereMod.PostFx.WSM3DPostStack.RefreshMaterials();
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
            catch (System.Exception ex)
            {
                string reason = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                string msg = $"{flagName} could not be {(newValue ? "enabled" : "disabled")}: {reason}";
                Debug.LogError($"[WSM3D] {msg}\n{ex}");
                WorldSphereMod.Worldspace.PhaseToast.ShowError(msg);
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
            try
            {
                Sphere.Prepare();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[WSM3D] Sphere.Prepare FAILED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }
        const string HarmonyID = "WorldSphereMod";
        //this mod makes the game 3D, of course im patching alot (rip compatibility)
        //literally the core function of the mod
        static void Patch()
        {
            try
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
                    if (!PhasePatchGate.ShouldApplyHarmonyPatch(type, savedSettings))
                    {
                        continue;
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
                Patcher.PatchAll(typeof(WorldSphereMod.Bridge.BridgeSurvivalBackup));
                Patcher.PatchAll(typeof(WorldSphereMod.Bridge.BridgeLoadSaveHooks));
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
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[WSM3D] Core.Init FAILED: " + ex);
            }
        } 
        public static void Become3D()
        {
            // Guard: Sphere.Begin reads MapBox.width/height and will create a
            // zero-sized SphereManager if they haven't been set yet.
            if (MapBox.width <= 0 || MapBox.height <= 0)
            {
                UnityEngine.Debug.LogError($"[WSM3D] Become3D aborted: MapBox dimensions not ready ({MapBox.width}x{MapBox.height}). Caller should re-queue via SmoothLoader.");
                return;
            }
            // Guard: large maps (e.g. 576x576 = 331K tiles) cause GPU hangs
            // during SphereManager creation. Skip 3D mode until we optimize.
            int totalTiles = MapBox.width * MapBox.height;
            int maxTiles = savedSettings.MaxTilesFor3D;
            if (totalTiles > maxTiles)
            {
                UnityEngine.Debug.LogWarning($"[WSM3D] Become3D skipped: map too large for 3D mode ({MapBox.width}x{MapBox.height} = {totalTiles} tiles, max {maxTiles}). Use a smaller map or flat mode.");
                return;
            }
            // Ensure world-dependent assets (textures, map layers) are prepared.
            // PrepareWorld is idempotent — it no-ops if already called from PostInit.
            try { Sphere.PrepareWorld(); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[WSM3D] Become3D: PrepareWorld failed: " + ex.Message); }
            // Sphere.Begin starts a coroutine that spreads tile+buffer init
            // across frames; the onCreated callback fires once the Manager
            // exists (before buffers finish) and triggers the remaining 3D
            // subsystem setup. DrawTiles is gated on Manager.IsReady so
            // rendering waits for buffer uploads to complete.
            try { Sphere.Begin(); }
            catch (System.Exception ex) { UnityEngine.Debug.LogError("[WSM3D] Sphere.Begin failed: " + ex); }
        }
        static void FinishBecome3D()
        {
            try { CameraManager.MakeCamera3D(); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[WSM3D] MakeCamera3D failed: " + ex.Message); }
            try { WorldSphereMod.Lighting.CubemapLighting.EnsureCreated(); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[WSM3D] CubemapLighting failed: " + ex.Message); }
            try { WorldSphereMod.PostFx.WSM3DPostStack.EnsureCreated(); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[WSM3D] WSM3DPostStack failed: " + ex.Message); }
            try { WorldSphereMod.Lighting.ProceduralSky.EnsureCreated(); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[WSM3D] ProceduralSky.EnsureCreated failed: " + ex.Message); }
            try { Do3DStuff(); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[WSM3D] Do3DStuff failed: " + ex.Message); }
            try { Sphere.LogDiagnostics("[WSM3D] Become3D"); }
            catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[WSM3D] Sphere diagnostics failed: " + ex.Message); }
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
        public delegate void PrepareShape(ref int Width, ref int Height);
        // the layer between the Mod and the compound sphere
        public static class Sphere
        {
            public static void AddShape(Shape shape)
            {
                Shapes.Add(shape);
            }
            public static Quaternion GetRotation(Vector2 position)
            {
                return CurrentShape.cameraRotation(position);
            }
            public delegate Quaternion GetCameraRot(Vector2 tile);
            public struct Shape
            {
                public Shape(To2D to2d, To2DFast to2dfast, GetSphereTilePosition to3d, GetSphereTileRotation rot, Initiation init, GetCameraRange GetCameraRange, GetVector getVector, GetSphereTileScale GetScale, PhaseGate xgate, PhaseGate ygate, PrepareShape isvalid, GetCameraRot getCameraRot)
                {
                    this.To2D = to2d;
                    this.To2DFast = to2dfast;
                    this.To3D = to3d;
                    this.tileRotation = rot;
                    this.GetScale = GetScale;
                    this.Inititation = init;
                    this.GetCameraRange = GetCameraRange;
                    this.XGate = xgate;
                    YGate = ygate;
                    cameraRotation = getCameraRot;
                    this.GetCameraVector = getVector;
                    this.Prepare = isvalid;
                }
                public bool IsWrapped => object.ReferenceEquals(XGate, WrappedGate);
                public PrepareShape Prepare;
                public PhaseGate XGate;
                public PhaseGate YGate;
                public To2D To2D;
                public GetSphereTileScale GetScale;
                public To2DFast To2DFast;
                public GetSphereTilePosition To3D;
                public GetSphereTileRotation tileRotation;
                public GetCameraRot cameraRotation;
                public Initiation Inititation;
                public GetCameraRange GetCameraRange;
                public GetVector GetCameraVector;
            }
            public static PhaseGate XGate => CurrentShape.XGate;
            public static PhaseGate YGate => CurrentShape.YGate;
            public static float Radius => Manager.Radius;
            public static int Width => Manager.Rows;
            public static int Height => Manager.Cols;
            public static bool IsWrapped => CurrentShape.IsWrapped;
            public static Transform CenterCapsule => Manager.transform.childCount > 0 ? Manager.transform.GetChild(0) : null;
            public static bool Exists => Manager != null;
            public static float HeightMult = 0;
            public static bool PerlinNoise = true;
            #region Fancy stuff
            static SphereManager Manager;
            static Mesh CompoundSphereMesh;
            internal static Material CompoundSphereMaterial;
            static Texture2DArray Textures;
            static SphereManagerSettings SphereManagerConfig;
            static Dictionary<Tile, int> TileIDS;
            static Dictionary<int, Color32> TextureAverageCache;
            #endregion
            public static List<MapLayer> BaseLayers;
            public static Dictionary<MapLayer, PixelArray> CachedColors;
            public delegate Vector3 To2D(SphereManager manager, float x, float y, float z);
            public delegate Vector2 To2DFast(SphereManager manager, float x, float y, float z);
            static Shape CurrentShape;
            public static void GetCamerRange(out int Min, out int Max)
            {
                CurrentShape.GetCameraRange(Manager, out Min, out Max);
            }
            public static Vector2 GetCameraVector(float Speed, bool Vertical)
            {
                return CurrentShape.GetCameraVector(Speed, Vertical);
            }
            static List<Shape> Shapes = new List<Shape>()
            {
                new Shape(CylindricalToCartesian, CylindricalToCartesianFast, CartesianToCylindrical, CylindricalRotation, CylindricalInitiation, RenderRange, GetMovementVectorSpherical, SphereTileScaleCylindrical, WrappedGate, DefaultGate, (ref int _, ref int _) => { }, CylindricalRotation), //cylinder
                new Shape(FlatToCartesian, FlatToCartesianFast, CartesianToFlat, FlatRotation, FlatInitiation, RenderRangeFlat, GetMovementVectorFlat, SphereTileScaleFlat, DefaultGate, DefaultGate, (ref int _, ref int _) => { }, FlatRotation), //flat
                new Shape(CartesianToCube, CartesianToCubeFast, CubeToCartesian, CubeRotation, CubeInitiation, RenderRangeCube, GetMovementVectorCube, SphereTileScaleCube, DefaultGate, DefaultGate, Tools.Cube.Prepare, CubeRotation)
            };
            public static void Begin()
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                HeightMult = savedSettings.TileHeight;
                PerlinNoise = Core.savedSettings.PerlinNoise;
                Debug.Log($"[WSM3D][PERF] Sphere.Begin.ShapeAndFlags={sw.Elapsed.TotalMilliseconds:F3}ms");
                sw.Restart();
                CreateSettings();
                Debug.Log($"[WSM3D][PERF] Sphere.Begin.CreateSettings={sw.Elapsed.TotalMilliseconds:F3}ms");
                sw.Restart();
                int width = MapBox.width;
                int height = MapBox.height;
                if (CompoundSphereMaterial == null || CompoundSphereMesh == null)
                {
                    UnityEngine.Debug.LogError("[WSM3D] Sphere.Begin: CompoundSphereMaterial or CompoundSphereMesh missing — skipping CreateSphereManager. Bundle load likely failed.");
                    return;
                }
                Debug.Log($"[WSM3D][PERF] Sphere.Begin.PreCreateManager={sw.Elapsed.TotalMilliseconds:F3}ms");
                sw.Restart();
                // Async path: spread heavy tile+buffer init across frames so
                // the main thread stays responsive during world load.
                MonoBehaviour host = Mod.Object.GetComponent<MonoBehaviour>();
                host.StartCoroutine(SphereManager.Creator.CreateSphereManagerAsync(
                    width, height, SphereManagerConfig,
                    onCreated: mgr =>
                    {
                        Manager = mgr;
                        ConfigureHeightField(mgr, width, height);
                        Debug.Log($"[WSM3D][PERF] Sphere.Begin.ManagerCreated(async)={sw.Elapsed.TotalMilliseconds:F3}ms");
                        Debug.Log($"[WSM3D] Sphere.Begin: shape={savedSettings.CurrentShape} " +
                            $"({(CurrentShape.IsWrapped ? "cylindrical" : "flat")}) " +
                            $"width={width} height={height} radius={Manager.Radius:F3}");
                        FinishBecome3D();
                        // Defensive re-trigger: HdrSkybox / DayNightCycle settings can
                        // toggle ApplySetting() at NML-load time BEFORE IsWorld3D=true,
                        // causing EnsureCreated() to silently bail. Re-call here once
                        // Sphere.Exists is guaranteed true so the pale-blue ambient fix
                        // and procedural sky always run when their flags are enabled.
                        try
                        {
                            if (savedSettings.HdrSkybox)
                                WorldSphereMod.Lighting.CubemapLighting.EnsureCreated();
                        }
                        catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[WSM3D] CubemapLighting re-trigger failed: " + ex.Message); }
                        try
                        {
                            if (savedSettings.HdrSkybox || savedSettings.DayNightCycle)
                                WorldSphereMod.Lighting.ProceduralSky.EnsureCreated();
                        }
                        catch (System.Exception ex) { UnityEngine.Debug.LogWarning("[WSM3D] ProceduralSky re-trigger failed: " + ex.Message); }
                    }));
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
            static Color32 GetTextureAverageColor(int textureIndex)
            {
                if (Textures == null || textureIndex < 0 || textureIndex >= Textures.depth)
                {
                    return new Color32(128, 128, 128, 255);
                }

                TextureAverageCache ??= new Dictionary<int, Color32>();
                if (TextureAverageCache.TryGetValue(textureIndex, out Color32 cached))
                {
                    return cached;
                }

                Color32[] pixels;
                try
                {
                    pixels = Textures.GetPixels32(textureIndex);
                }
                catch
                {
                    return new Color32(128, 128, 128, 255);
                }

                if (pixels == null || pixels.Length == 0)
                {
                    return new Color32(128, 128, 128, 255);
                }

                long r = 0;
                long g = 0;
                long b = 0;
                long a = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 p = pixels[i];
                    r += p.r;
                    g += p.g;
                    b += p.b;
                    a += p.a;
                }

                Color32 average = new Color32(
                    (byte)Mathf.Clamp((int)(r / pixels.Length), 0, 255),
                    (byte)Mathf.Clamp((int)(g / pixels.Length), 0, 255),
                    (byte)Mathf.Clamp((int)(b / pixels.Length), 0, 255),
                    (byte)Mathf.Clamp((int)(a / pixels.Length), 0, 255));
                TextureAverageCache[textureIndex] = average;
                return average;
            }
            // Sample a tile's base color (composed map-layer pixels) at (x,y),
            // honoring X-wrap for cylindrical worlds. Returns false when the
            // tile is out of bounds or unresolvable.
            static bool TrySampleBaseColor(int x, int y, out Color32 color, out WorldTile tile)
            {
                color = default;
                tile = null;
                if (y < 0 || y >= MapBox.height)
                {
                    return false;
                }
                if (Core.Sphere.IsWrapped)
                {
                    x = (int)Tools.MathStuff.Wrap(x, 0, MapBox.width);
                }
                else if (x < 0 || x >= MapBox.width)
                {
                    return false;
                }
                WorldTile sample = World.world.GetTileSimple(x, y);
                if (sample == null)
                {
                    return false;
                }
                int idx = sample.data.tile_id;
                Color32[] worldPixels = World.world.world_layer.pixels;
                if (worldPixels == null || idx < 0 || idx >= worldPixels.Length)
                {
                    return false;
                }
                color = GetBaseColor(idx);
                tile = sample;
                return true;
            }

            // Smooth biome boundaries by blending the tile's base color toward
            // a weighted neighborhood average. This keeps interior detail
            // crisp while softening edges across nearby biome transitions.
            static Color32 BlendBiomeColor(int index, Color32 fallback)
            {
                WorldTile[] tilesList = World.world != null ? World.world.tiles_list : null;
                if (tilesList == null || index < 0 || index >= tilesList.Length)
                {
                    return fallback;
                }

                WorldTile center = tilesList[index];
                if (center == null)
                {
                    return fallback;
                }

                const int radius = 3;
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
                        if (sample.data.tile_id != center.data.tile_id)
                        {
                            weight *= 1.5f;
                        }
                        if (weight <= 0f)
                        {
                            continue;
                        }

                        r += sampleColor.r * weight;
                        g += sampleColor.g * weight;
                        b += sampleColor.b * weight;
                        a += sampleColor.a * weight;
                        totalWeight += weight;
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
            public static Color32 GetAddedColor(int Index)
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
            private static bool _scalesDone = true;
            private static bool _texDone = true;
            private static bool _addedDone = true;
            private static bool _colorsDone = true;

            public static void RefreshSphere()
            {
                // Snapshot whether any tile data actually changed BEFORE we drain
                // the queues. The heightfield mesh rebuild is expensive (~1M verts
                // on a 316² map at 43k tiles) — we must only invalidate it when
                // real tile changes are pending, not on every 0.1s redraw tick.
                bool hadDirtyTiles = Manager.HasDirtyTiles;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                _scalesDone = Manager.RefreshScales();
                long scaleMs = sw.ElapsedMilliseconds;

                sw.Restart();
                _texDone = Manager.RefreshTextures();
                long texMs = sw.ElapsedMilliseconds;

                sw.Restart();
                _addedDone = Manager.RefreshCustom("AddedColors");
                long addedMs = sw.ElapsedMilliseconds;

                sw.Restart();
                RefreshColors();
                long colorMs = sw.ElapsedMilliseconds;

                if (hadDirtyTiles && Manager.UseHeightFieldTerrain)
                {
                    Manager.HeightField.MarkDirty();
                }

                long total = scaleMs + texMs + addedMs + colorMs;
                if (total > 16 && Core.savedSettings != null && Core.savedSettings.ProfilerDump)
                {
                    UnityEngine.Debug.LogWarning($"[WSM3D][PERF] RefreshSphere SLOW: {total}ms " +
                        $"(scales={scaleMs}ms tex={texMs}ms " +
                        $"added={addedMs}ms colors={colorMs}ms)");
                }
                // LogDiagnostics walks every tile via reflection — only when profiling.
                if (Core.savedSettings != null && Core.savedSettings.ProfilerDump)
                    LogDiagnostics("[WSM3D] RefreshSphere");
            }

            public static bool HasPendingUpdates()
            {
                return !_scalesDone || !_texDone || !_addedDone || !_colorsDone;
            }
            public static void RefreshColors()
            {
                if (Manager == null)
                {
                    _colorsDone = true;
                    return;
                }
                _colorsDone = Manager.RefreshColors();
            }
            public static void UpdateLayer(SphereTile Tile)
            {
                Manager.UpdateCustom("AddedColors", Tile.X, Tile.Y);
            }
            public static void UpdateBaseLayer(SphereTile Tile)
            {
                Manager.UpdateColor(Tile.X, Tile.Y);
            }
            public static void PrepareShape(ref int Width, ref int Height)
            {
                CurrentShape = Shapes[savedSettings.CurrentShape];
                CurrentShape.Prepare(ref Width, ref Height);
            }
            public static void Finish()
            {
                if (Manager == null || Manager.gameObject == null)
                {
                    return;
                }
                Manager.Destroy();
            }
            public static void LogDiagnostics(string prefix)
            {
                string cameraName = CameraManager.MainCamera != null ? CameraManager.MainCamera.name : "<null>";
                Vector3 cameraPos = CameraManager.MainCamera != null ? CameraManager.MainCamera.transform.position : default;
                Vector3 centerPos = Manager != null ? Manager.transform.position : default;
                float radius = Manager != null ? Manager.Radius : 0f;
                float cameraDistance = Manager != null && CameraManager.MainCamera != null
                    ? Vector3.Distance(cameraPos, centerPos)
                    : 0f;
                bool cameraInside = Manager != null && CameraManager.MainCamera != null && cameraDistance < radius;

                // Compute the actual camera-to-nearest-tile-surface distance.
                // For cylindrical: the camera sits at (Radius + Height) from
                // the cylinder axis; nearest surface is at Radius, so the gap
                // is Height. For flat: the camera Y minus the tile plane Y=0.
                float cameraToSurface = -1f;
                if (Manager != null && CameraManager.MainCamera != null)
                {
                    if (IsWrapped)
                    {
                        // Cylindrical: distance from camera to cylinder surface
                        float camR = Mathf.Sqrt(cameraPos.x * cameraPos.x + cameraPos.y * cameraPos.y);
                        cameraToSurface = Mathf.Abs(camR - radius);
                    }
                    else
                    {
                        // Flat: camera Y above the tile plane
                        cameraToSurface = Mathf.Abs(cameraPos.y);
                    }
                }

                // Compute world-space tile extent by sampling corner tiles.
                Vector3 tileBoundsMin = default;
                Vector3 tileBoundsMax = default;
                if (Manager != null)
                {
                    Vector3 p00 = Manager.SphereTilePosition(0, 0);
                    Vector3 pMaxMax = Manager.SphereTilePosition(Manager.Rows - 1, Manager.Cols - 1);
                    tileBoundsMin = Vector3.Min(p00, pMaxMax);
                    tileBoundsMax = Vector3.Max(p00, pMaxMax);
                    // For cylindrical, also sample the extremes
                    if (IsWrapped)
                    {
                        Vector3 pMidMax = Manager.SphereTilePosition(Manager.Rows / 2, Manager.Cols - 1);
                        Vector3 pMid0 = Manager.SphereTilePosition(Manager.Rows / 2, 0);
                        tileBoundsMin = Vector3.Min(tileBoundsMin, Vector3.Min(pMidMax, pMid0));
                        tileBoundsMax = Vector3.Max(tileBoundsMax, Vector3.Max(pMidMax, pMid0));
                    }
                }

                int texturedTiles = 0;
                int totalTiles = 0;
                if (Manager != null)
                {
                    System.Reflection.FieldInfo tilesField = typeof(SphereManager).GetField("SphereTiles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (tilesField != null && tilesField.GetValue(Manager) is SphereTile[] tiles)
                    {
                        totalTiles = tiles.Length;
                        for (int i = 0; i < tiles.Length; i++)
                        {
                            if (tiles[i].TextureIndex != 0)
                            {
                                texturedTiles++;
                            }
                        }
                    }
                }
                string meshBoundsLocal = CompoundSphereMesh != null ? CompoundSphereMesh.bounds.ToString() : "<null>";
                int passCount = CompoundSphereMaterial != null ? CompoundSphereMaterial.passCount : -1;
                int renderQueue = CompoundSphereMaterial != null ? CompoundSphereMaterial.renderQueue : -1;
                int managerLayer = Manager != null ? Manager.gameObject.layer : -1;
                int cameraMask = CameraManager.MainCamera != null ? CameraManager.MainCamera.cullingMask : -1;
                string shaderName = CompoundSphereMaterial != null && CompoundSphereMaterial.shader != null
                    ? CompoundSphereMaterial.shader.name : "<null>";
                bool cameraOrtho = CameraManager.MainCamera != null && CameraManager.MainCamera.orthographic;
                float cameraFov = CameraManager.MainCamera != null ? CameraManager.MainCamera.fieldOfView : -1f;
                float cameraNear = CameraManager.MainCamera != null ? CameraManager.MainCamera.nearClipPlane : -1f;
                float cameraFar = CameraManager.MainCamera != null ? CameraManager.MainCamera.farClipPlane : -1f;
                string shape = IsWrapped ? "cylindrical" : "flat";
                Debug.Log(
                    $"{prefix} camera={cameraName} cameraPos={cameraPos} shape={shape} sphereCenter={centerPos} radius={radius:F3} " +
                    $"cameraToOrigin={cameraDistance:F3} cameraToSurface={cameraToSurface:F3} cameraInsideSphere={cameraInside} " +
                    $"cameraOrtho={cameraOrtho} cameraFov={cameraFov:F1} cameraNear={cameraNear:F2} cameraFar={cameraFar:F1} " +
                    $"cameraLayerMask=0x{cameraMask:X8} managerLayer={managerLayer} " +
                    $"meshBoundsLocal={meshBoundsLocal} tileBoundsWorld=({tileBoundsMin} -> {tileBoundsMax}) " +
                    $"shader={shaderName} materialRenderQueue={renderQueue} materialPassCount={passCount} texturedTiles={texturedTiles}/{totalTiles}");
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
                if (Manager == null || !Manager.IsReady) return;
                Manager.DrawTiles(CameraX);
            }
            static void ConfigureHeightField(SphereManager mgr, int mapWidth, int mapHeight)
            {
                bool enabled = savedSettings.UseHeightFieldTerrain && savedSettings.CurrentShape == 0;
                mgr.UseHeightFieldTerrain = enabled;
                if (!enabled) return;

                var hf = mgr.HeightField;

                bool wrapped = CurrentShape.IsWrapped;
                int w = mapWidth;
                int h = mapHeight;

                hf.Configure(
                    sampleHeight: (tx, ty) =>
                    {
                        int sx = wrapped ? ((tx % w) + w) % w : Mathf.Clamp(tx, 0, w - 1);
                        int sy = Mathf.Clamp(ty, 0, h - 1);
                        WorldTile tile = World.world.GetTileSimple(sx, sy);
                        if (tile == null) return 0f;
                        return tile.TileHeight();
                    },
                    sampleColor: (tx, ty) =>
                    {
                        int sx = wrapped ? ((tx % w) + w) % w : Mathf.Clamp(tx, 0, w - 1);
                        int sy = Mathf.Clamp(ty, 0, h - 1);
                        WorldTile tile = World.world.GetTileSimple(sx, sy);
                        if (tile == null) return new Color32(128, 128, 128, 255);
                        return GetColor(tile.data.tile_id);
                    },
                    sampleTexture: (tx, ty) =>
                    {
                        int sx = wrapped ? ((tx % w) + w) % w : Mathf.Clamp(tx, 0, w - 1);
                        int sy = Mathf.Clamp(ty, 0, h - 1);
                        WorldTile tile = World.world.GetTileSimple(sx, sy);
                        if (tile == null) return 0;
                        return WorldTileTexture(tile);
                    },
                    projectPosition: (worldX, worldY, height) =>
                    {
                        return mgr.SphereTilePosition(worldX, worldY, height * HeightMult);
                    }
                );

                // Create a vertex-color material for the height field since the
                // instanced CompoundSphere shader reads StructuredBuffers that
                // don't exist on a plain DrawMesh call.
                Shader vcShader = Shader.Find("Sprites/Default");
                if (vcShader == null) vcShader = ResolveShader("");
                if (vcShader != null)
                {
                    Material hfMat = new Material(vcShader)
                    {
                        color = Color.white,
                    };
                    hf.SetMaterial(hfMat);
                }

                Debug.Log($"[WSM3D] HeightFieldRenderer configured: map={w}x{h} wrapped={wrapped}");
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
            /// <summary>Whether <see cref="PrepareAssets"/> has already run.</summary>
            public static bool AssetsPrepared { get; private set; }
            /// <summary>Whether <see cref="PrepareWorld"/> has already run.</summary>
            public static bool WorldPrepared { get; private set; }

            /// <summary>
            /// Load the AssetBundle, shaders, mesh, and material. Has NO dependency
            /// on <c>World.world</c> so it is safe to call during <c>Init()</c>.
            /// Idempotent — subsequent calls are no-ops.
            /// </summary>
            public static void PrepareAssets()
            {
                if (AssetsPrepared) return;
                AssetsPrepared = true;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                LoadAssets();
                Debug.Log($"[WSM3D][PERF] Sphere.PrepareAssets.LoadAssets={sw.Elapsed.TotalMilliseconds:F3}ms");
            }

            /// <summary>
            /// Build tile textures and cache map-layer colours. Requires
            /// <c>World.world</c> to be initialized. Idempotent.
            /// </summary>
            public static void PrepareWorld()
            {
                if (WorldPrepared) return;
                if (World.world == null || World.world._map_layers == null)
                {
                    Debug.LogWarning("[WSM3D] Sphere.PrepareWorld skipped — World.world not ready yet.");
                    return;
                }
                WorldPrepared = true;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                CreateTextures();
                Debug.Log($"[WSM3D][PERF] Sphere.PrepareWorld.CreateTextures={sw.Elapsed.TotalMilliseconds:F3}ms");
                sw.Restart();
                BaseLayers = new List<MapLayer>(World.world._map_layers);
                BaseLayers.Remove(FlashLayer);
                Debug.Log($"[WSM3D][PERF] Sphere.PrepareWorld.BaseLayersCopy={sw.Elapsed.TotalMilliseconds:F3}ms");
                sw.Restart();
                CreateCachedColors();
                Debug.Log($"[WSM3D][PERF] Sphere.PrepareWorld.CreateCachedColors={sw.Elapsed.TotalMilliseconds:F3}ms");
            }

            /// <summary>
            /// Original entry point kept for backward compatibility. Calls both
            /// <see cref="PrepareAssets"/> and <see cref="PrepareWorld"/>. Safe to
            /// call even if PrepareAssets was already called from Init().
            /// </summary>
            public static void Prepare()
            {
                PrepareAssets();
                PrepareWorld();
            }
            public static int WorldTileTexture(WorldTile Tile)
            {
                Tile Graphic = Tools.getVariation(Tile);
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

                // wsm3d-shaders bundle ships 10 BRP shaders (+ SVC asset); runtime
                // loads only SafeShaders — see ADR-0013 / human gate before
                // expanding the list. Load with try/catch if bundle file is missing.
                WrappedAssetBundle shaderAb = null;
                try { shaderAb = AssetBundleUtils.GetAssetBundle("wsm3d-shaders"); }
                catch { shaderAb = null; }
                if (shaderAb == null)
                {
                    Debug.LogWarning("[WSM3D] AssetBundleUtils.GetAssetBundle('wsm3d-shaders') returned null — shader bundle not baked yet. Consumers will fall back to Shader.Find / Standard.");
                }
                else
                {
                    // DIAGNOSTIC BLOCK DISABLED — see ADR-0013.
                    //
                    // Invoking AssetBundle.LoadAllAssets(typeof(ShaderVariantCollection))
                    // or LoadAllAssets(typeof(Shader)) on wsm3d-shaders triggers Unity's
                    // NATIVE crash handler:
                    //   "Mismatched serialization in builtin class 'Shader'.
                    //    Read 80 bytes but expected 4936 bytes"
                    //   ArgumentException: ManagedStream must be readable
                    //   → process abort intercepted by Unity crash reporter.
                    //
                    // This is a Unity 2022.3 cross-patch-version bundle serialization
                    // bug on some shaders; the C# try/catch CANNOT intercept the native
                    // crash. ADR-0013 mandates per-name GetObject<Shader> via the
                    // SafeShaders gate (below) — DO NOT re-enable bulk enumeration.
                    //
                    // Regression history: the previous diagnostic block invoked
                    // LoadAllAssets here and reintroduced the crash, taking the entire
                    // mod offline. Downstream symptoms: water renders as Standard-
                    // transparent billboard (no Gerstner displacement), voxel actors
                    // fall back to 2D billboards (no WSM3D shader to suppress the
                    // vanilla sprite render), mountain slope smoothing reverts.
                    Debug.Log("[WSM3D] wsm3d-shaders enumeration diagnostic intentionally skipped (ADR-0013 — LoadAllAssets crashes Unity natively).");

                    try
                    {
                        // DO NOT ADD MORE SHADERS to SafeShaders — see ADR-0013.
                        // The other 7 shaders in this bundle produce ManagedStream
                        // errors that trigger Unity's native crash reporter.
                        foreach (var shaderName in SafeShaders)
                        {
                            UnityEngine.Shader sh = null;
                            try
                            {
                                string assetPath = $"assets/wsm3d/shaders/{shaderName.ToLowerInvariant()}.shader";
                                sh = shaderAb.GetObject<UnityEngine.Shader>(assetPath);
                                if (sh == null)
                                {
                                    assetPath = $"Assets/WSM3D/Shaders/{shaderName}.shader";
                                    sh = shaderAb.GetObject<UnityEngine.Shader>(assetPath);
                                }
                            }
                            catch (System.Exception ex)
                            {
                                Debug.LogWarning($"[WSM3D] Shader '{shaderName}' threw during bundle load: {ex.Message}");
                                continue;
                            }
                            if (sh == null)
                            {
                                Debug.LogWarning($"[WSM3D] Shader not in wsm3d-shaders bundle: {shaderName}");
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
                            // Also reject shaders that have a valid name but are
                            // unsupported on this GPU (none of subshaders/fallbacks
                            // are suitable). Using such a shader triggers Unity's
                            // "ERROR: Shader shader is not supported on this GPU"
                            // and can hang the game (Responding=False).
                            if (!sh.isSupported)
                            {
                                Debug.LogError($"[WSM3D] Shader '{shaderName}' (resolved name='{sh.name}') is not supported on this GPU — skipping LoadedShaders cache. Consumer will fall back.");
                                continue;
                            }
                            LoadedShaders[shaderName] = sh;
                            Debug.Log($"[WSM3D] Loaded shader from wsm3d-shaders bundle: WSM3D/{shaderName} -> {sh.name}");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning("[WSM3D] Shader load: " + ex.Message);
                    }
                    // Log which shaders actually made it into the cache so
                    // "LoadedShaders[count=2]" in the log can be diagnosed.
                    Debug.Log($"[WSM3D] LoadedShaders[count={LoadedShaders.Count}]: {string.Join(", ", LoadedShaders.Keys)}");
                }

                // Inspect CompoundSphereMaterial's shader. If its shader was
                // bundled corrupted (empty .name), the terrain tiles render as black
                // trapezoids — user-reported 2026-05-23. Reassign to
                // Standard with a tan _Color so terrain at least shows up.
                if (CompoundSphereMaterial != null)
                {
                    string shName = CompoundSphereMaterial.shader != null ? CompoundSphereMaterial.shader.name : "<null>";
                    UnityEngine.Debug.Log($"[WSM3D] CompoundSphereMaterial.shader = '{shName}'");
                    // Unity substitutes 'Hidden/InternalErrorShader' when a
                    // shader reference fails to resolve at runtime — that's
                    // what produces the black terrain void users see when
                    // the bundled shader is missing/corrupted. Treat it the
                    // same as null/empty.
                    bool isBroken = CompoundSphereMaterial.shader == null
                                 || string.IsNullOrEmpty(shName)
                                 || shName.StartsWith("Hidden/Internal", System.StringComparison.OrdinalIgnoreCase);
                    if (isBroken)
                    {
                        // The CompoundSphere shader uses StructuredBuffers
                        // (Matrixes, Scales, Colors, Textures) for indirect
                        // instancing. Generic fallback shaders (Unlit/Color,
                        // Standard, etc.) cannot read these buffers, so ALL
                        // instances render at identity transform as 1-unit
                        // meshes at origin — effectively invisible terrain.
                        //
                        // Priority: try "CompoundSphere" by name first (works
                        // if the shader is baked into the worldsphere bundle
                        // and was registered with Unity). Then try generic
                        // fallbacks for at least a visible (though incorrectly
                        // positioned) terrain.
                        Shader? fallback = null;
                        string chosen = "<none>";

                        // First: try to recover the CompoundSphere shader from
                        // the bundle or Unity's global lookup.
                        var csShader = Shader.Find("CompoundSphere");
                        if (csShader != null && !string.IsNullOrEmpty(csShader.name))
                        {
                            fallback = csShader;
                            chosen = "CompoundSphere (Shader.Find)";
                        }

                        // Second: try generic shaders as last resort. These
                        // will NOT support the StructuredBuffer instancing —
                        // terrain will likely be invisible or a single tile
                        // at origin.
                        if (fallback == null)
                        {
                            // 60f1 strips Unlit/URP/Particles — only OpaqueVertexColor
                            // (bundle), Sprites/Default and Standard survive at runtime.
                            string[] candidates =
                            {
                                "WSM3D/OpaqueVertexColor",
                                "Sprites/Default",
                                "Standard",
                            };
                            foreach (var n in candidates)
                            {
                                if (LoadedShaders.TryGetValue(n.Substring(n.LastIndexOf('/') + 1), out var cached) && cached != null) { fallback = cached; chosen = n + " (cache)"; break; }
                                var sh2 = Shader.Find(n);
                                if (sh2 != null) { fallback = sh2; chosen = n; break; }
                            }
                        }

                        if (fallback != null)
                        {
                            CompoundSphereMaterial.shader = fallback;
                            Color tan = new Color(0.55f, 0.50f, 0.40f, 1f);
                            CompoundSphereMaterial.color = tan;
                            try { CompoundSphereMaterial.SetColor("_BaseColor", tan); } catch { }
                            try { CompoundSphereMaterial.SetColor("_Color", tan); } catch { }
                            try
                            {
                                CompoundSphereMaterial.EnableKeyword("_EMISSION");
                                CompoundSphereMaterial.SetColor("_EmissionColor", new Color(0.55f, 0.50f, 0.40f, 1f));
                            } catch { }
                            bool isGenericFallback = !chosen.Contains("CompoundSphere");
                            if (isGenericFallback)
                            {
                                UnityEngine.Debug.LogError($"[WSM3D] TERRAIN WILL BE INVISIBLE: CompoundSphereMaterial shader is broken (was '{shName}'), " +
                                    $"fell back to '{chosen}' which cannot read the StructuredBuffer instancing data (Matrixes/Scales/Colors). " +
                                    "Rebake the worldsphere AssetBundle with the CompoundSphere shader included.");
                            }
                            else
                            {
                                UnityEngine.Debug.LogWarning($"[WSM3D] CompoundSphereMaterial shader recovered to '{chosen}' (resolved name='{fallback.name}').");
                            }
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
                    UnityEngine.Debug.LogError("[WSM3D] SkyBox.mat missing from bundle — CameraManager.Begin skipped; sky will fall back to default.");
                }
                if (CompoundSphereMaterial != null && LibraryMaterials.instance != null)
                {
                    LibraryMaterials.instance._night_affected_colors.Add(CompoundSphereMaterial);
                }
            }

            // ----------------------------------------------------------------
            // ADR-0013: only OpaqueVertexColor survives the current 62f3-bake ->
            // 60f1-runtime cross-version load. The full-set expansion re-crashes
            // at runtime with 8x ManagedStream serialization-mismatch errors
            // ("Read 6572 bytes but expected 6600") — VerifyBuiltBundle is a
            // 62f3-EDITOR check, not a 60f1-RUNTIME check, so it false-positives.
            // DO NOT expand this list until the bundle is baked with the EXACT
            // runtime Unity version (2022.3.60f1). Until then, water/sky/postfx/
            // foliage degrade to Standard via Core.Sphere.ResolveShader.
            // ----------------------------------------------------------------
            public static readonly string[] SafeShaders = new[]
            {
                "OpaqueVertexColor",
            };

            // Static cache of bundle-loaded WSM3D/* shaders. Consumers look
            // here BEFORE Shader.Find — AssetBundle shaders aren't auto-
            // registered in Unity's global lookup, so Shader.Find returns
            // null for them unless they're also Always-Included.
            public static readonly System.Collections.Generic.Dictionary<string, UnityEngine.Shader> LoadedShaders =
                new System.Collections.Generic.Dictionary<string, UnityEngine.Shader>();

            // WorldBox's Unity 60f1 runtime ships a STRIPPED built-in shader set:
            // every Unlit/* and Universal Render Pipeline/* probe returns null at
            // runtime (confirmed live 2026-05-29), so those fallbacks produced the
            // neon-magenta / NullReferenceException actors. The ONLY safe last
            // resort is "Standard". Resolve a bundle shader by SafeShaders key,
            // else fall back to Standard — NEVER to Unlit/* or URP/*.
            public static UnityEngine.Shader ResolveShader(string bundleName)
            {
                if (!string.IsNullOrEmpty(bundleName)
                    && LoadedShaders.TryGetValue(bundleName, out var bundled)
                    && bundled != null)
                {
                    return bundled;
                }
                return UnityEngine.Shader.Find("Standard");
            }

            // True only when the named bundle shader actually deserialized and is
            // GPU-supported (it made it into LoadedShaders). Feature paths that
            // REQUIRE a bundle-only shader (MeshWater->GerstnerWater,
            // HdrSkybox->ProceduralSky) gate on this and skip the bundle path
            // entirely when it returns false — the degraded Standard path renders
            // instead of reaching for a missing Unlit/URP shader.
            public static bool HasBundleShader(string bundleName) =>
                !string.IsNullOrEmpty(bundleName)
                && LoadedShaders.TryGetValue(bundleName, out var sh)
                && sh != null;

            public static SphereTile GetTile(int X, int Y)
            {
                return Manager[X, Y];
            }
            static void CreateSettings()
            {
                SphereManagerConfig = new SphereManagerSettings(
                    CurrentShape.Inititation,
                    CurrentShape.To3D,
                    CurrentShape.tileRotation,
                    CurrentShape.GetScale,
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
                TextureAverageCache = new Dictionary<int, Color32>();
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
