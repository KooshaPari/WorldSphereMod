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
        // 2.6 -> 2.7: the 2.5→2.6 bump fixed the one-time migration but users whose
        // JSON was already at "2.6" with VoxelEntities=false (saved before the
        // ApplyPhaseDefaults entry existed) hit the version-match path (line 78) and
        // migration never re-fired — stale-false was preserved. The fix: bump to 2.7
        // AND make ApplySchemaVersionMigration FORCE-SET VoxelEntities=true +
        // CrossedQuadFoliage=true unconditionally (not relying on default-if-absent).
        // 2.7 -> 2.8: same pattern — mods_config JSON at "2.7" with VoxelEntities=false
        // persisted from before the force-set was saved back. Version-match path skips
        // migration so stale-false survives. Bump forces migration to re-fire. (#208)
        // 2.8 -> 2.9: live JSON at 2.8 has ActorVoxelScaleFactor=0.5 (net 4× actors),
        // BuildingVoxelScaleFactor=0.25 (net 2× buildings), FoliageVoxelScaleFactor=0.2
        // (net 1.6× foliage). All three produce visibly oversized 3D entities in-game
        // (user-reported P0 2026-06-04). Bumping the schema forces ApplyPhaseDefaults
        // to re-run and reset the per-entity scale factors to 0.125 (net 1×).
        // 2.10 -> 2.12: worldspace nametags + health bars visibly oversized
        // (user-reported P0 2026-06-04, task #191 / #208). Bump forces
        // ApplyPhaseDefaults to re-fire and re-pin NameplateBaseScale +
        // the localScale clamps in NameplateWorld.LateUpdate + HealthBar.Attach.
        // 2.12 = water-flat-sealevel active (#208); forces fresh migration.
        // 2.13 = worldspace nametags + health bars further shrunk at default
        // zoom (user-reported P0 2026-06-04, task #208). Bump forces migration.
        // 2.14 = DayNightCycle default-on for render-foundation sun-cycle verification.
        public static string SettingsVersion = "2.14";

        public static Harmony Patcher;
        internal static bool ClearVoxelMeshCacheOnFirstFrame;
        private static bool _phaseDiagLogged;
        private static bool _heightDiagLogged = false;
        private static bool _runtimeLightingConfigured;
        /// <summary>True when no settings file existed at load time (fresh install).</summary>
        public static bool IsFirstInstall { get; private set; }
        static void SafeInvoke(string context, Action action)
        {
            try { action(); }
            catch (System.Exception ex) { Debug.LogWarning("[WSM3D] " + context + ": " + ex.Message); }
        }
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
            // Force-on VoxelEntities on every load — belt-and-suspenders guard against
            // stale-false persisted in the JSON surviving a version-match (the 2.7 user
            // had VoxelEntities=false saved which the version-match path kept). Remove
            // this override once the setting is confirmed stable and user-togglable. (#208)
            savedSettings.VoxelEntities = true;
            LogPhaseFlagDefaults(savedSettings);
            return true;
        }

        static void ApplySchemaVersionMigration(SavedSettings loadedData)
        {
            SavedSettings.ApplyPhaseDefaults(loadedData);
            loadedData.VoxelEntities = true;
            loadedData.CrossedQuadFoliage = true;

            // FORCE-SET critical render flags unconditionally.
            // ApplyPhaseDefaults sets these to true, but we also write them
            // here explicitly so that if a future ApplyPhaseDefaults omits one
            // of these entries, migration still guarantees the correct state.
            // This is the canonical fix for the 2.6-stale-false regression:
            // a persisted JSON at version 2.6 with VoxelEntities=false was
            // kept because the version-match guard bypassed migration entirely.
            // Now every migration (any old-version → current) force-overwrites
            // these flags regardless of the persisted value.
            loadedData.VoxelEntities = true;
            loadedData.CrossedQuadFoliage = true;

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

        /// <summary>
        /// Re-ensure critical phase patches are applied. Corrects any False toggles that
        /// fired during init/load before Patcher was ready or from save-load clobbers.
        /// Must be called after Patcher exists (PostInit or later). (#208 billboard fix)
        /// </summary>
        public static void EnsurePhasePatches()
        {
            if (Patcher == null) return;
            if (savedSettings.VoxelEntities)
            {
                SafeInvoke("EnsurePhasePatches VoxelEntities", () => ApplyPhaseToggle(nameof(SavedSettings.VoxelEntities), true));
            }
            if (savedSettings.CrossedQuadFoliage)
            {
                SafeInvoke("EnsurePhasePatches CrossedQuadFoliage", () => ApplyPhaseToggle(nameof(SavedSettings.CrossedQuadFoliage), true));
            }
            // Force these flags=true regardless of what ApplyPhaseDefaults set.
            // ApplyPhaseDefaults runs before EnsurePhasePatches and resets them to false.
            savedSettings.ProceduralBuildings = true;
            savedSettings.MeshWater = true;
            bool proceduralBuildingsPatchInstalled = false;
            SafeInvoke("EnsurePhasePatches ProceduralBuildings", () => {
                ApplyPhaseToggle(nameof(SavedSettings.ProceduralBuildings), true);
                proceduralBuildingsPatchInstalled = IsProceduralBuildingsPatchInstalled();
            });
            SafeInvoke("EnsurePhasePatches MeshWater", () => ApplyPhaseToggle(nameof(SavedSettings.MeshWater), true));
            if (!_phaseDiagLogged)
            {
                Debug.Log($"[WSM3D][PHASE-DIAG] ProceduralBuildings={savedSettings.ProceduralBuildings} patchInstalled={proceduralBuildingsPatchInstalled}");
                _phaseDiagLogged = true;
            }
        }

        static bool IsProceduralBuildingsPatchInstalled()
        {
            if (Patcher == null) return false;
            try
            {
                foreach (var method in Patcher.GetPatchedMethods())
                {
                    if (method != null && method.DeclaringType == typeof(WorldSphereMod.ProcGen.BuildingProcRender.ProcMeshEmit))
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // go go gadget un-box my worldbox
        public static void Init()
        {
            ClearVoxelMeshCacheOnFirstFrame = true;
            InitProfiler.Measure("LoadSettings", () => LoadSettings());
            Sphere.HeightMult = Mathf.Max(savedSettings.TileHeight, 1f);
            Debug.Log($"[WSM3D][HEIGHT-DIAG] Init pre-bootstrap HeightMult={Sphere.HeightMult} TileHeightSetting={savedSettings.TileHeight}");
            InitProfiler.Measure("RuntimeLighting.Configure", () => ConfigureRuntimeLighting());
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

        static void ConfigureRuntimeLighting()
        {
            if (_runtimeLightingConfigured) return;
            _runtimeLightingConfigured = true;

            ReassertRenderFoundationAmbient();

            if (RenderSettings.sun != null && RenderSettings.sun.type == LightType.Directional)
            {
                return;
            }

            Light sun = null;
            Light[] lights = UnityEngine.Object.FindObjectsOfType<Light>();
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].type == LightType.Directional)
                {
                    sun = lights[i];
                    break;
                }
            }

            if (sun == null)
            {
                GameObject sunGo = new GameObject("WSM3D.RuntimeSun");
                sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1f;
                sun.color = Color.white;
                sun.enabled = true;
            }

            RenderSettings.sun = sun;
        }

        internal static void ReassertRenderFoundationAmbient()
        {
            if (savedSettings != null && (savedSettings.DayNightCycle || savedSettings.HdrSkybox))
            {
                return;
            }

            RenderSettings.ambientLight = new Color(0.4f, 0.4f, 0.4f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
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
                // MountainSlopeSmoothing: slope smoothing is intrinsic to the fork's
                // height-field mesh now (corner-averaged heights + analytic normals +
                // Perlin micro-displacement). No main-mod surface to toggle.
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
            // EnsurePhasePatches was defined but never called — this was the billboard
            // root cause: BuildingVoxelEmit / ActorVoxelEmit Postfixes never installed,
            // voxel emit loop never ran, processed=0, all sprites stayed 2D. (#208)
            SafeInvoke("EnsurePhasePatches failed", () => EnsurePhasePatches());
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
                // Input-capture recorder hooks (passive; gated by SavedSettings.InputCaptureEnabled).
                Patcher.PatchAll(typeof(WorldSphereMod.Capture.CaptureClickHook));
                Patcher.PatchAll(typeof(WorldSphereMod.Capture.CaptureSelectToolHook));
                Patcher.PatchAll(typeof(WorldSphereMod.Capture.CaptureNewWorldHook));
                Patcher.PatchAll(typeof(WorldSphereMod.Capture.CaptureLoadSaveHook));
                Patcher.PatchAll(typeof(WorldSphereMod.Capture.CaptureSetSpeedByIdHook));
                Patcher.PatchAll(typeof(WorldSphereMod.Capture.CaptureSetSpeedByAssetHook));
                Patcher.PatchAll(typeof(WorldSphereMod.Capture.CaptureZoomHook));
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
                // Lava now renders as a 3D corner-averaged emissive surface in the fork
                // (HeightFieldRenderer LiquidKind.Lava, wired in ConfigureHeightField),
                // mirroring how the 2D water billboard was retired for the 3D water
                // surface. The flat LavaLayer overlay transpiles are therefore retired —
                // re-enable only if the 3D lava surface is disabled.
                // Patcher.Transpile(Method(typeof(LavaLayer), nameof(LavaLayer.drawLavaPixel)), MapLayerTranspiler);
                // Patcher.Transpile(Method(typeof(LavaLayer), nameof(LavaLayer.updateLava)), MapLayerTranspiler);
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
            // If _map_layers is empty (WorldBox not yet populated — common on save-load
            // when finishMakingWorld fires before layer init), defer via coroutine and
            // poll until Count>0 before calling PrepareWorld+Begin. (#208 terrain-white)
            if (World.world != null && World.world._map_layers != null && World.world._map_layers.Count == 0)
            {
                UnityEngine.Debug.LogWarning("[WSM3D] Become3D: _map_layers empty — deferring to coroutine to wait for population.");
                var host = UnityEngine.GameObject.FindObjectOfType<UnityEngine.MonoBehaviour>();
                if (host != null)
                {
                    host.StartCoroutine(Become3DWhenLayersReady());
                    return;
                }
            }
            Become3DImmediate();
        }

        static System.Collections.IEnumerator Become3DWhenLayersReady()
        {
            int waited = 0;
            while (World.world == null || World.world._map_layers == null || World.world._map_layers.Count == 0)
            {
                waited++;
                if (waited > 300) // ~5s at 60fps; bail out to avoid infinite loop
                {
                    UnityEngine.Debug.LogError("[WSM3D] Become3DWhenLayersReady: timed out waiting for _map_layers to populate after 300 frames. Proceeding anyway.");
                    break;
                }
                yield return null;
            }
            UnityEngine.Debug.Log($"[WSM3D] Become3DWhenLayersReady: _map_layers populated after {waited} frames (Count={World.world?._map_layers?.Count ?? -1}).");
            Become3DImmediate();
        }

        static void Become3DImmediate()
        {
            SafeInvoke("Become3D: PrepareWorld failed", () => Sphere.PrepareWorld());
            // ONE-SHOT DIAGNOSTIC (A): sample 5 tile heights and core height settings.
            // confirm terrain-height investigation (#208).
            try
            {
                if (!_heightDiagLogged && World.world != null && MapBox.width > 0 && MapBox.height > 0)
                {
                    int w = MapBox.width; int h = MapBox.height;
                    int[,] pts = { { w / 8, h / 8 }, { w / 4, h / 2 }, { w / 2, h / 4 }, { 3 * w / 4, 3 * h / 4 }, { w - 2, h - 2 } };
                    float minHeight = float.PositiveInfinity;
                    float maxHeight = float.NegativeInfinity;
                    int validSamples = 0;
                    for (int i = 0; i < 5; i++)
                    {
                        int px = pts[i, 0];
                        int py = pts[i, 1];
                        try
                        {
                            WorldTile tile = World.world.GetTileSimple(px, py);
                            // Sample RAW WorldBox elevation (GetHeight), not the fork's
                            // TileHeight() — the latter routes through Tile.WorldToSphere()
                            // -> Core.Sphere.GetTile(), which NREs at Become3D ENTRY because
                            // Sphere.Begin() (which builds the Manager + SphereTiles) has not
                            // run yet. GetHeight() is the native tile field and is populated
                            // as soon as the world tiles exist, so the flat-terrain auto-boost
                            // below can actually evaluate on both bridge + menu load paths.
                            float tileHeight = tile == null ? float.NaN : tile.GetHeight();
                            if (!float.IsNaN(tileHeight))
                            {
                                if (tileHeight < minHeight) minHeight = tileHeight;
                                if (tileHeight > maxHeight) maxHeight = tileHeight;
                                validSamples++;
                            }
                            Debug.Log($"[WSM3D][HEIGHT-DIAG] tile=({px},{py}) GetHeight()={tileHeight} HeightMult={Sphere.HeightMult} TileHeightSetting={savedSettings.TileHeight}");
                        }
                        catch (System.Exception tileEx)
                        {
                            Debug.LogWarning($"[WSM3D][HEIGHT-DIAG] sample failed at tile=({px},{py}): " + tileEx.Message);
                        }
                    }
                    if (validSamples >= 3)
                    {
                        float span = maxHeight - minHeight;
                        Debug.Log($"[WSM3D][HEIGHT-DIAG] terrain sample span={span:F4} min={minHeight:F4} max={maxHeight:F4} HeightMult={Sphere.HeightMult}");
                        // GetHeight() is in raw integer elevation steps: a genuinely flat
                        // region spans 0, any relief spans >=1. 0.5 cleanly separates the two.
                        const float flatSpanThreshold = 0.5f;
                        if (span <= flatSpanThreshold && savedSettings.TileHeight <= 1f)
                        {
                            float oldMult = Sphere.HeightMult;
                            Sphere.HeightMult = Mathf.Clamp(savedSettings.TileHeight * 6f, 1f, 8f);
                            Debug.LogWarning($"[WSM3D][HEIGHT-DIAG] auto-boosted flat terrain: HeightMult {oldMult} -> {Sphere.HeightMult}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[WSM3D][HEIGHT-DIAG] terrain sample span unavailable: validSamples={validSamples} (using current HeightMult={Sphere.HeightMult})");
                    }
                    _heightDiagLogged = true;
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[WSM3D][HEIGHT-DIAG] sample failed: " + ex.Message); }
            // ONE-SHOT COLOR DIAGNOSTIC: confirm GetTileColor returns non-black biome RGB.
            // PASS: at least one tile has R≠G or G≠B (divergent channels = real biome color).
            // FAIL: all (128,128,128) = Textures null/not built; all (0,0,0,0) = still pixel buffer.
            try
            {
                if (World.world != null && MapBox.width > 0 && MapBox.height > 0)
                {
                    int cw = MapBox.width; int ch = MapBox.height;
                    int[,] cpts = { {cw/8,ch/8},{cw/4,ch/2},{cw/2,ch/4},{3*cw/4,3*ch/4},{cw-2,ch-2} };
                    for (int ci = 0; ci < 5; ci++)
                    {
                        int cpx = cpts[ci,0]; int cpy = cpts[ci,1];
                        WorldTile ctile = World.world.GetTileSimple(cpx, cpy);
                        Color32 cc = Sphere.GetTileColor(ctile);
                        int ctex = ctile != null ? Sphere.WorldTileTexture(ctile) : -1;
                        Debug.Log($"[WSM3D][COLOR-DIAG] tile=({cpx},{cpy}) type={ctile?.main_type?.id ?? "null"} texIdx={ctex} RGBA=({cc.r},{cc.g},{cc.b},{cc.a})");
                    }
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[WSM3D][COLOR-DIAG] failed: " + ex.Message); }
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
            SafeInvoke("MakeCamera3D failed", () => CameraManager.MakeCamera3D());
            // WorldspaceUI on world-load: EnsureCreated is normally only triggered by a
            // live flag toggle; when the JSON already has WorldspaceUI=true the renderer
            // was never started. Start it here so nameplates + healthbars appear at load.
            SafeInvoke("WorldUIRenderer.EnsureCreated failed", () => WorldSphereMod.Worldspace.WorldUIRenderer.EnsureCreated());
            // SUN=NULL ROOT-CAUSE FIX: the mod-load SunDriver.Init() (PostInit) ran
            // while Core.IsWorld3D was false (Sphere did not exist yet) and early-
            // returned, so the directional sun was never created and RenderSettings.sun
            // stayed null -> near-black terrain. Re-run it here where IsWorld3D is true.
            // Init() is idempotent (no-ops if Sun already exists).
            SafeInvoke("SunDriver.Init failed", () => WorldSphereMod.Lighting.SunDriver.Init());
            // Start the day/night driver so the sun is actively pumped, but only when
            // the user enabled DayNightCycle. When it's off, the static sun + ambient
            // floor from Init() keep the scene lit (no forced day/night).
            if (savedSettings.DayNightCycle)
            {
                SafeInvoke("TimeOfDay.EnsureCreated failed", () => WorldSphereMod.Lighting.TimeOfDay.EnsureCreated());
            }
            SafeInvoke("CubemapLighting failed", () => WorldSphereMod.Lighting.CubemapLighting.EnsureCreated());
            SafeInvoke("WSM3DPostStack failed", () => WorldSphereMod.PostFx.WSM3DPostStack.EnsureCreated());
            SafeInvoke("ProceduralSky.EnsureCreated failed", () => WorldSphereMod.Lighting.ProceduralSky.EnsureCreated());
            ReassertRenderFoundationAmbient();
            SafeInvoke("Do3DStuff failed", () => Do3DStuff());
            SafeInvoke("Sphere diagnostics failed", () => Sphere.LogDiagnostics("[WSM3D] Become3D"));
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
            // Render-foundation machine-verification handles: the last terrain
            // height-field material + mesh applied this world, so the bridge
            // /telemetry can report terrainMaterialShader / terrainMeshVertCount
            // without per-frame cost (read on telemetry request only).
            public static UnityEngine.Material LastTerrainMaterial;
            public static UnityEngine.Mesh LastTerrainMesh;

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
            public static Texture2DArray TerrainTextureArray => Textures;
            public static float HeightMult = 0;
            public static bool PerlinNoise = true;
            #region Fancy stuff
            static SphereManager Manager;
            // #199 GPU-compute go-live: a GpuSphereManager wired IN PARALLEL with the
            // CPU Manager for the instanced actor/voxel tile path. The CPU Manager
            // stays the coordinate/terrain (HeightField) authority. Null until the
            // async GPU creator completes; null whenever CompoundCompute is unavailable
            // (legacy CPU-only path). All consumer calls are null-guarded.
            static CompoundSpheres.Gpu.GpuSphereManager GpuManager;
            static CompoundSpheres.Gpu.GpuSphereManagerSettings GpuManagerConfig;
            public static SphereManager ManagerInstance => Manager;
            static Mesh CompoundSphereMesh;
            internal static Material CompoundSphereMaterial;
            // GPU-compute keystone shader (CompoundSphereCompute), loaded from the
            // wsm3d-shaders bundle in LoadAssets. Non-null once the bundle ships the
            // baked .compute; the GPU CompoundSpheres.Gpu manager / LegacyManagerShim
            // bind its CSMatrices/CSColors kernels to it. Null => legacy CPU path.
            internal static UnityEngine.ComputeShader CompoundCompute;
            static Texture2DArray Textures;
            static Texture2D TerrainTextureAtlas;
            static int TerrainTextureAtlasCols;
            static int TerrainTextureAtlasRows;
            const int TerrainTextureAtlasTileSize = 8;
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
                        SafeInvoke("CubemapLighting re-trigger failed", () => {
                            if (savedSettings.HdrSkybox)
                                WorldSphereMod.Lighting.CubemapLighting.EnsureCreated();
                        });
                        SafeInvoke("ProceduralSky re-trigger failed", () => {
                            if (savedSettings.HdrSkybox || savedSettings.DayNightCycle)
                                WorldSphereMod.Lighting.ProceduralSky.EnsureCreated();
                        });
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

            /// <summary>
            /// Get biome color for a tile from the center texel of its terrain sprite
            /// texture slice in the texture array built by CreateTextures(). In 3D mode,
            /// tilemap.redrawTiles() is intercepted + bypassed, so world_layer.pixels and
            /// MapLayer.pixels are never painted — we bypass that buffer entirely and use
            /// a texture-slice cache here. This keeps color sampling aligned to the
            /// source sprite data and avoids atlas-UV remap mistakes.
            /// (#208 terrain-gray root cause fix, center-pixel fidelity follow-up)
            /// </summary>
            public static Color32 GetTileColor(WorldTile tile)
            {
                if (tile == null) return new Color32(128, 128, 128, 255);
                int texIdx = WorldTileTexture(tile);
                Color32 c = GetTexturePixelColor(texIdx, tile.x, tile.y);
                // If texIdx<=0 and we got the mid-gray fallback, try pixel buffer too.
                if (texIdx <= 0)
                {
                    int w = MapBox.width;
                    int idx = tile.y * w + tile.x;
                    Color32[] wp = World.world?.world_layer?.pixels;
                    if (wp != null && idx >= 0 && idx < wp.Length && wp[idx].a > 0)
                        return wp[idx];
                }
                return c;
            }

            static int WrapTextureCoord(int value, int size)
            {
                int wrapped = value % size;
                return wrapped < 0 ? wrapped + size : wrapped;
            }

            static uint TextureTileHash(int textureIndex, int tileX, int tileY)
            {
                unchecked
                {
                    uint hash = (uint)(tileX * 374761393 + tileY * 668265263 + textureIndex * 11);
                    hash ^= hash >> 13;
                    hash *= 1274126177u;
                    hash ^= hash >> 16;
                    return hash;
                }
            }

            static Color32 GetTexturePixelColor(int textureIndex, int tileX, int tileY)
            {
                if (Textures == null || textureIndex < 0 || textureIndex >= Textures.depth)
                {
                    return new Color32(128, 128, 128, 255);
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

                int w = Textures.width;
                int h = Textures.height;
                if (w <= 0 || h <= 0)
                {
                    return new Color32(128, 128, 128, 255);
                }

                // Sample a small neighborhood in the texture slice so biomes with
                // patterned art no longer become a single flat color per tile.
                uint hash = TextureTileHash(textureIndex, tileX, tileY);
                int jitterX = (int)(hash & 3u) - 1;       // -1..2
                int jitterY = (int)((hash >> 2) & 3u) - 1; // -1..2

                int px0 = WrapTextureCoord(jitterX, w);
                int px1 = WrapTextureCoord(((w - 1) >> 1) + jitterX, w);
                int px2 = WrapTextureCoord((w - 1) + jitterX, w);
                int py0 = WrapTextureCoord(jitterY, h);
                int py1 = WrapTextureCoord(((h - 1) >> 1) + jitterY, h);
                int py2 = WrapTextureCoord((h - 1) + jitterY, h);

                int sampleCount = 0;
                float r = 0f;
                float g = 0f;
                float b = 0f;

                for (int sy = 0; sy < 3; ++sy)
                {
                    for (int sx = 0; sx < 3; ++sx)
                    {
                        int sampleX = sx == 0 ? px0 : (sx == 1 ? px1 : px2);
                        int sampleY = sy == 0 ? py0 : (sy == 1 ? py1 : py2);
                        int sampleIndex = sampleY * w + sampleX;
                        if (sampleIndex < 0 || sampleIndex >= pixels.Length) continue;

                        Color32 sample = pixels[sampleIndex];
                        r += sample.r;
                        g += sample.g;
                        b += sample.b;
                        ++sampleCount;
                    }
                }

                if (sampleCount == 0)
                {
                    return new Color32(128, 128, 128, 255);
                }

                return new Color32((byte)Mathf.RoundToInt(r / sampleCount), (byte)Mathf.RoundToInt(g / sampleCount), (byte)Mathf.RoundToInt(b / sampleCount), 255);
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
                // FIX: use position index (y*width+x) not tile_id (#208 terrain-gray)
                int idx = y * MapBox.width + x;
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

                        // FIX: use position index (y*width+x) not tile_id (#208 terrain-gray)
                        Color32 sampleColor = GetBaseColor(y * MapBox.width + x);
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
                // Snapshot whether any HEIGHT-AFFECTING tile data changed BEFORE we
                // drain the queues. The heightfield mesh rebuild is expensive (~1M
                // verts on a 316² map at 43k tiles) — we must only invalidate it
                // when terrain GEOMETRY actually changes.
                //
                // PERF (per-frame rebuild storm fix): we previously gated on
                // HasDirtyTiles, which includes the COLOR + TEXTURE queues. On a
                // live world the simulation (water flow, fire, lava, mob tints)
                // re-enqueues color/texture updates for visible tiles EVERY frame,
                // so HasDirtyTiles was effectively always true → MarkDirty every
                // frame → a full ~5s geometry rebuild every frame = the storm seen
                // in Player.log. Terrain SHAPE only changes when a tile's elevation
                // (scale) changes, so gate the geometry rebuild on HasDirtyHeights
                // (the scale queue) alone. Color churn no longer triggers a rebuild;
                // it is consumed by RefreshColors into the GPU color buffer.
                bool hadDirtyHeights = Manager.HasDirtyHeights;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                _scalesDone = Manager.RefreshScales();
                long scaleMs = sw.ElapsedMilliseconds;

                sw.Restart();
                _texDone = Manager.RefreshTextures();
                // #199 Phase 3: mirror the texture-buffer flush to the GPU manager.
                // GPU Refresh* use the per-buffer dirty queue (Textures.Refresh()),
                // NOT the LegacyManagerShim O(N)/frame full-scan (risk #6).
                GpuManager?.RefreshTextures();
                long texMs = sw.ElapsedMilliseconds;

                sw.Restart();
                _addedDone = Manager.RefreshCustom("AddedColors");
                // AddedColors buffer is registered in CreateGpuSettings (risk #3),
                // so this RefreshCustom will not throw KeyNotFoundException.
                GpuManager?.RefreshCustom("AddedColors");
                long addedMs = sw.ElapsedMilliseconds;

                sw.Restart();
                RefreshColors();
                long colorMs = sw.ElapsedMilliseconds;

                if (hadDirtyHeights && Manager.UseHeightFieldTerrain)
                {
                    // Risk #5: keep GPU RefreshScales INSIDE the hadDirtyHeights gate.
                    // Tile elevation only changes when the scale (height) queue is
                    // dirty; flushing every frame re-broke the 3.2s/frame rebuild
                    // storm. The GPU scale flush uses the dirty-queue (Scales.Refresh()).
                    GpuManager?.RefreshScales();
                    // Mirror ProfilerDump into the fork so the parallelized
                    // HeightField.Rebuild emits its Stopwatch breakdown ONLY when
                    // profiling is on (per the debug-console-overlay spam trap).
                    CompoundSpheres.HeightFieldRenderer.ProfileRebuild =
                        Core.savedSettings != null && Core.savedSettings.ProfilerDump;
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
                // #199 Phase 3: mirror the base-color flush to the GPU manager via its
                // own dirty-queue (Colors.Refresh()) — NOT the shim full-scan (risk #6).
                GpuManager?.RefreshColors();
            }
            public static void UpdateLayer(SphereTile Tile)
            {
                Manager.UpdateCustom("AddedColors", Tile.X, Tile.Y);
                // #199 Phase 3: mark the same AddedColors slot dirty on the GPU buffer.
                // GpuSphereManager.UpdateCustom takes a flat index (X*Cols+Y).
                GpuManager?.UpdateCustom("AddedColors", (Tile.X * Height) + Tile.Y);
            }
            public static void UpdateBaseLayer(SphereTile Tile)
            {
                Manager.UpdateColor(Tile.X, Tile.Y);
                // #199 Phase 3: mirror the per-tile base-color dirty mark to the GPU
                // dirty queue. GpuSphereManager.UpdateColor(X,Y) maps to (X*Cols+Y).
                GpuManager?.UpdateColor(Tile.X, Tile.Y);
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
                if (Core.savedSettings.ProfilerDump)
                {
                    Debug.Log(
                        $"{prefix} camera={cameraName} cameraPos={cameraPos} shape={shape} sphereCenter={centerPos} radius={radius:F3} " +
                        $"cameraToOrigin={cameraDistance:F3} cameraToSurface={cameraToSurface:F3} cameraInsideSphere={cameraInside} " +
                        $"cameraOrtho={cameraOrtho} cameraFov={cameraFov:F1} cameraNear={cameraNear:F2} cameraFar={cameraFar:F1} " +
                        $"cameraLayerMask=0x{cameraMask:X8} managerLayer={managerLayer} " +
                        $"meshBoundsLocal={meshBoundsLocal} tileBoundsWorld=({tileBoundsMin} -> {tileBoundsMax}) " +
                        $"shader={shaderName} materialRenderQueue={renderQueue} materialPassCount={passCount} texturedTiles={texturedTiles}/{totalTiles}");
                }
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
                // #201: HeightFieldRenderer is shape-AGNOSTIC. Its mesh build operates
                // purely on Rows×Cols + corner-averaging and projects every vertex via
                // the injected projectPosition => mgr.SphereTilePosition, which is the
                // active shape's own (cylindrical/flat/cube) To2D projector. So the smooth
                // mesh renders correctly for ALL shapes; the old `&& CurrentShape == 0`
                // gate needlessly pinned it to one shape (and left land blocky elsewhere).
                bool enabled = savedSettings.UseHeightFieldTerrain;
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
                        // Bypass world_layer.pixels/MapLayer.pixels (always zero in 3D mode
                        // because tilemap.redrawTiles is intercepted by Queue3D Prefix which
                        // returns false when IsWorld3D=true). Use tile sprite texture average
                        // color directly from the Texture2DArray built in CreateTextures(). (#208)
                        return GetTileColor(tile);
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

                Shader vcShader = ResolveShader("");
                if (vcShader != null)
                {
                    Material hfMat = new Material(vcShader)
                    {
                        color = Color.white,
                    };

                    // BLACK-TERRAIN GUARD (restores commit 77661bc0, lost when
                    // TerrainSmoothing.cs was deleted in the f1b0ad9e merge).
                    // The land mesh carries per-vertex corner-averaged colors as its
                    // sole albedo. Whatever shader we resolved samples _MainTex and
                    // multiplies it into albedo:
                    //   - Mobile/VertexLit / Mobile/Diffuse / Diffuse: albedo = color * tex2D(_MainTex)
                    //   - Standard: albedo *= _MainTex
                    // All declare _MainTex = "white" {}, but some 60f1 runtimes leave
                    // it NULL at runtime -> tex2D() = (0,0,0,0) -> albedo zeroed ->
                    // the entire terrain surface renders BLACK. Force the built-in
                    // white pixel so vertex colors survive. VoxelRender has the same guard.
                    if (hfMat.HasProperty("_MainTex"))
                        hfMat.SetTexture("_MainTex", Texture2D.whiteTexture);
                    if (hfMat.HasProperty("_BaseMap"))
                        hfMat.SetTexture("_BaseMap", Texture2D.whiteTexture);

                    int terrainTexArrayLayers = 0;
                    bool usesTerrainTexArray = false;
                    Texture2DArray terrainTexArray = TerrainTextureArray;
                    if (hfMat.HasProperty("_TerrainTexArray"))
                    {
                        hfMat.SetTexture("_TerrainTexArray", terrainTexArray);
                        usesTerrainTexArray = terrainTexArray != null;
                        terrainTexArrayLayers = terrainTexArray != null ? terrainTexArray.depth : 0;
                    }
                    if (hfMat.HasProperty("_UseTerrainTexArray"))
                    {
                        hfMat.SetFloat("_UseTerrainTexArray", usesTerrainTexArray && terrainTexArrayLayers > 0 ? 1f : 0f);
                    }

                    if (hfMat.HasProperty("_EmissionColor"))
                    {
                        hfMat.DisableKeyword("_EMISSION");
                        hfMat.SetColor("_EmissionColor", Color.black);
                    }

                    // DIAG: surface the resolved terrain shader + emission floor so a
                    // black/dark-terrain regression can be diagnosed from Player.log
                    // without guessing which shader path the runtime took.
                    Debug.LogWarning($"[WSM3D] ConfigureHeightField terrain material: shader='{vcShader.name}' " +
                        $"hasEmission={hfMat.HasProperty("_EmissionColor")} " +
                        $"emission={(hfMat.HasProperty("_EmissionColor") ? hfMat.GetColor("_EmissionColor").ToString() : "n/a")} " +
                        $"hasMainTex={hfMat.HasProperty("_MainTex")} " +
                        $"terrainTexArray={usesTerrainTexArray} layers={terrainTexArrayLayers}");

                    hf.SetMaterial(hfMat);
                    EnsureMeshNormals(hf.Mesh, "terrain");
                    LastTerrainMaterial = hfMat;
                    LastTerrainMesh = hf.Mesh;
                }

                // Water now lives IN THE FORK as a corner-averaged sub-mesh at the
                // water level (below sand) — no more main-mod billboard overlay.
                // Sea level uses the sea-reference height (ice boundary). Keeping this
                // fixed flat at sea level matches world-box water intent and avoids a
                // painted-seabed look when shore elevation rises above the old sink.
                // Heights are passed RAW (pre-HeightMult) because projectPosition
                // applies HeightMult, matching the land sampleHeight units.
                //
                // Fix #208: water surface must be at a FIXED sea level across the
                // whole map, not at the per-tile seabed elevation. The renderer
                // already does this via a "constant water level" latch (RebuildWater
                // line ~1127), but a malformed sampleWaterLevel callback (returning
                // seabed) would let the surface sink. Pin the callback to a literal
                // seaLevel return so a single water tile seeds the constant and the
                // rest of the ocean sits at the same elevation. Seabed still comes
                // from the tile's TileHeight for depth-shading; we just guarantee
                // surface level = max(seaLevel, tile.TileHeight()) so a basin
                // tile whose TileHeight > seaLevel does not pull the surface up.
                float seaLevel = Tools.TrueHeight(17);
                Debug.Log("[WSM3D][BANNER] water-flat-sealevel v2.12 active seaLevel=" + seaLevel.ToString("F3") +
                          " h=" + h + " w=" + w + " wrapped=" + wrapped);
                bool meshWaterEnabled = savedSettings.MeshWater;
                hf.ConfigureWater(
                    sampleIsWater: (tx, ty) =>
                    {
                        int sx = wrapped ? ((tx % w) + w) % w : Mathf.Clamp(tx, 0, w - 1);
                        int sy = Mathf.Clamp(ty, 0, h - 1);
                        WorldTile tile = World.world.GetTileSimple(sx, sy);
                        if (tile == null || !meshWaterEnabled) return false;
                        return IsWaterTile(tile.main_type, tile.top_type, tile.TileHeight(), seaLevel);
                    },
                    sampleWaterLevel: (tx, ty) => seaLevel, // Keep all rendered water vertices at one sea-level plane; depth coupling happens via sampledSeabed only.
                    sampleSeabed: (tx, ty) =>
                    {
                        int sx = wrapped ? ((tx % w) + w) % w : Mathf.Clamp(tx, 0, w - 1);
                        int sy = Mathf.Clamp(ty, 0, h - 1);
                        WorldTile tile = World.world.GetTileSimple(sx, sy);
                        if (tile == null) return seaLevel;
                        float tileH = tile.TileHeight();
                        // Clamp seabed <= seaLevel so depth is never negative; this
                        // also means corners with seabed > seaLevel report seabed
                        // (rare, mostly sandbars) and the depth-bake will be 0
                        // (no water column there). Surface itself is pinned via
                        // sampleWaterLevel above.
                        return tileH < seaLevel ? tileH : seaLevel;
                    }
                );

                // Translucent water material (alpha-blended) so terrain reads through.
                if (meshWaterEnabled)
                {
                    Color waterColor = new Color(0.20f, 0.45f, 0.65f, 0.72f);
                    Shader waterShader = null;

                    if (HasBundleShader("GerstnerWater"))
                    {
                        Shader bundledWater = Sphere.LoadedShaders["GerstnerWater"];
                        if (bundledWater != null && bundledWater.passCount > 0)
                        {
                            waterShader = bundledWater;
                        }
                    }
                    if (waterShader == null)
                    {
                        waterShader = ResolveShader("");
                    }

                    if (waterShader == null)
                    {
                        Debug.LogWarning("[WSM3D] ConfigureHeightField water shader unresolved; skipping water material.");
                        return;
                    }

                    Material waterMat = new Material(waterShader) { name = "WSM3D.HeightFieldWater" };
                    if (waterMat.HasProperty("_Color"))
                        waterMat.SetColor("_Color", waterColor);
                    waterMat.color = waterColor;
                    // Standard Transparent mode: ZWrite off, SrcAlpha/OneMinusSrcAlpha, queue 3000.
                    if (waterMat.HasProperty("_Mode")) waterMat.SetFloat("_Mode", 3f);
                    waterMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    waterMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    waterMat.SetInt("_ZWrite", 0);
                    waterMat.EnableKeyword("_ALPHABLEND_ON");
                    waterMat.DisableKeyword("_ALPHATEST_ON");
                    waterMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    waterMat.renderQueue = 3000;
                    hf.SetWaterMaterial(waterMat);
                }

                // -----------------------------------------------------------------
                // Generalized liquid surfaces (lava / swamp / acid). Each reuses the
                // fork's corner-averaged sub-mesh + analytic-normal + depth-shading
                // path (HeightFieldRenderer.ConfigureLiquid) with its own
                // classification and per-type material. Lava now renders as a 3D
                // emissive surface here instead of the flat 2D LavaLayer overlay.
                //
                // Classification (WorldBox tile data):
                //   LAVA  : main_type.lava (the canonical lava flag, same one
                //           FoliageTileRender/BridgeServer already key on).
                //   SWAMP : tile string id starts with "swamp" (swamp_low/_high),
                //           and the tile is a liquid/ground swamp surface.
                //   ACID  : tile string id contains "acid" (WorldBox ships acid as a
                //           cloud/status effect, so terrain acid is rare; the layer is
                //           wired+materialed and lights up if/when an acid tile exists).
                // Each liquid's surface sits at its own tile height (level == seabed ==
                // TileHeight) so it conforms to its basin exactly like water.
                ConfigureLiquidSurface(hf, LiquidKind.Lava, w, h, wrapped,
                    classify: tt => tt.lava,
                    color: new Color(1.00f, 0.35f, 0.05f, 0.92f),
                    emissive: new Color(1.40f, 0.45f, 0.06f, 1f));
                ConfigureLiquidSurface(hf, LiquidKind.Swamp, w, h, wrapped,
                    classify: tt => !tt.lava && IdContains(tt, "swamp"),
                    color: new Color(0.22f, 0.32f, 0.12f, 0.78f),
                    emissive: null);
                ConfigureLiquidSurface(hf, LiquidKind.Acid, w, h, wrapped,
                    classify: tt => IdContains(tt, "acid"),
                    color: new Color(0.45f, 0.95f, 0.10f, 0.72f),
                    emissive: new Color(0.30f, 0.80f, 0.05f, 1f));

                // -----------------------------------------------------------------
                // TERRAIN-OVERLAY family: feed per-tile overlay state (snow / tumor
                // corruption / burnt / frozen) from WorldBox tile data into the
                // fork's land vertex-color bake. Cheap vertex tint, no extra mesh.
                // Data sources (decompiled Assembly-CSharp):
                //   snow       — main_type.id starts with "snow" (snow_sand/hills/
                //                block/summit) OR a snow top_type; biome cold.
                //   corruption — top_type.creep == true (tumor_low/high,
                //                corruption_low/high, biomass). DYNAMIC: spreads via
                //                CorruptedTreesManager; we dirty on change (below).
                //   burnt      — WorldTile.burned_stages > 0 (or isOnFire()).
                //   frozen     — WorldTile.data.frozen OR main_type.id == "ice".
                // -----------------------------------------------------------------
                hf.ConfigureTerrainOverlay((tx, ty) =>
                {
                    int sx = wrapped ? ((tx % w) + w) % w : Mathf.Clamp(tx, 0, w - 1);
                    int sy = Mathf.Clamp(ty, 0, h - 1);
                    WorldTile tile = World.world.GetTileSimple(sx, sy);
                    return SampleTileOverlay(tile);
                });

                Debug.Log($"[WSM3D] HeightFieldRenderer configured: map={w}x{h} wrapped={wrapped} seaLevel={seaLevel:F2} (+lava/swamp/acid surfaces +terrain overlays)");
            }

            /// <summary>
            /// Read the TERRAIN-OVERLAY state for a single WorldBox tile and pack it
            /// into the fork's POD <see cref="HeightFieldRenderer.TerrainOverlay"/>.
            /// Uses the real Assembly-CSharp fields; private/uncertain ones are
            /// guarded so a WorldBox update can't NRE the renderer.
            /// </summary>
            static CompoundSpheres.HeightFieldRenderer.TerrainOverlay SampleTileOverlay(WorldTile tile)
            {
                var ov = default(CompoundSpheres.HeightFieldRenderer.TerrainOverlay);
                if (tile == null) return ov;

                try
                {
                    var main = tile.main_type;
                    var top = tile.top_type;

                    // SNOW: snow_* ground tiles read full; cold/forever-frozen biomes
                    // get a lighter dusting so peaks/tundra still read snowy.
                    if (IdContains(main, "snow"))
                        ov.Snow = 1f;
                    else if (main != null && main.forever_frozen)
                        ov.Snow = Mathf.Max(ov.Snow, 0.5f);

                    // FROZEN: explicit per-tile frozen flag, or literal ice tile.
                    bool frozen = false;
                    try { frozen = tile.data != null && tile.data.frozen; } catch { }
                    if (frozen || IdContains(main, "ice"))
                        ov.Frozen = 1f;

                    // CORRUPTION / TUMOR (dynamic): any creep top tile — tumor_low/
                    // high, corruption_low/high, biomass. creep is the WorldBox flag
                    // that marks the spreading corruption family.
                    if (top != null && top.creep)
                        ov.Corruption = 1f;
                    else if (main != null && main.creep)
                        ov.Corruption = Mathf.Max(ov.Corruption, 1f);

                    // BURNT: burned_stages accumulates 0..15 after fire; scale to 0..1.
                    int burned = 0;
                    try { burned = tile.burned_stages; } catch { }
                    if (burned > 0)
                        ov.Burnt = Mathf.Clamp01(burned / 12f);
                    else
                    {
                        bool onFire = false;
                        try { onFire = tile.isOnFire(); } catch { }
                        if (onFire) ov.Burnt = 0.6f;
                    }
                }
                catch { /* never let overlay sampling break the terrain bake */ }

                return ov;
            }

            static void EnsureMeshNormals(Mesh mesh, string label)
            {
                if (mesh == null || mesh.vertexCount == 0) return;
                try
                {
                    Vector3[] normals = mesh.normals;
                    if (normals == null || normals.Length != mesh.vertexCount)
                    {
                        mesh.RecalculateNormals();
                        Debug.Log($"[WSM3D] Recalculated missing {label} mesh normals.");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[WSM3D] " + label + " mesh normal check failed: " + ex.Message);
                }
            }

            /// <summary>True if the tile type's string id contains <paramref name="needle"/> (case-insensitive).</summary>
            static bool IdContains(TileTypeBase tt, string needle)
            {
                if (tt == null) return false;
                string id = tt.id;
                return !string.IsNullOrEmpty(id) && id.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
            }

            /// <summary>
            /// Resolve whether a tile should render as open water for the mesh-water
            /// surface. Covers canonical liquid/ocean flags plus name-based river/lake
            /// cases where the flagging is inconsistent across map packs.
            /// </summary>
            static bool IsWaterTile(TileTypeBase mainType, TileTypeBase topType, float tileHeight, float seaLevel)
            {
                return IsWaterTilePublic(mainType, topType, tileHeight, seaLevel);
            }

            /// <summary>Public shim so the bridge diag endpoint can re-evaluate the same predicate (#208 water-flat verify).</summary>
            public static bool IsWaterTilePublic(TileTypeBase mainType, TileTypeBase topType, float tileHeight, float seaLevel)
            {
                if (mainType == null)
                {
                    return false;
                }

                bool looksLikeWater = mainType.liquid || mainType.ocean
                    || IdContains(mainType, "water")
                    || IdContains(mainType, "river")
                    || IdContains(mainType, "lake")
                    || IdContains(mainType, "sea")
                    || (topType != null && (IdContains(topType, "water") || IdContains(topType, "river") || IdContains(topType, "lake") || IdContains(topType, "sea")));

                return looksLikeWater
                    && !mainType.sand
                    && !mainType.ground
                    && tileHeight <= seaLevel;
            }

            /// <summary>
            /// Wire one non-water liquid surface into the fork's height field: a
            /// classifier (over the tile's main_type), a level/seabed sampler (the
            /// tile's own height, so the surface conforms to its basin), and a
            /// per-type translucent (lava also emissive) material via ResolveShader
            /// (bundle-or-Standard).
            /// </summary>
            static void ConfigureLiquidSurface(
                HeightFieldRenderer hf,
                LiquidKind kind,
                int w, int h, bool wrapped,
                Func<TileTypeBase, bool> classify,
                Color color,
                Color? emissive)
            {
                Func<int, int, WorldTile> getTile = (tx, ty) =>
                {
                    int sx = wrapped ? ((tx % w) + w) % w : Mathf.Clamp(tx, 0, w - 1);
                    int sy = Mathf.Clamp(ty, 0, h - 1);
                    return World.world.GetTileSimple(sx, sy);
                };

                hf.ConfigureLiquid(
                    kind,
                    sampleIsLiquid: (tx, ty) =>
                    {
                        WorldTile tile = getTile(tx, ty);
                        if (tile == null) return false;
                        var tt = tile.main_type;
                        return tt != null && classify(tt);
                    },
                    sampleLevel: (tx, ty) =>
                    {
                        WorldTile tile = getTile(tx, ty);
                        return tile == null ? 0f : tile.TileHeight();
                    },
                    sampleSeabed: (tx, ty) =>
                    {
                        WorldTile tile = getTile(tx, ty);
                        return tile == null ? 0f : tile.TileHeight();
                    });

                Shader shader = ResolveShader("");
                if (shader == null) return;
                Material mat = new Material(shader) { name = "WSM3D.HeightField" + kind };
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
                mat.color = color;
                bool translucent = color.a < 0.999f;
                if (translucent)
                {
                    if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3f);
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = 3000;
                }
                if (emissive.HasValue && mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", emissive.Value);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                hf.SetLiquidMaterial(kind, mat);
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
            /// Reset the PrepareWorld guard so the next PrepareWorld() / Become3D call
            /// re-reads map layers and pixel arrays from the newly loaded world.
            /// Must be called on every save-load and generate-new-world transition —
            /// failing to do so leaves BaseLayers pointing at stale/empty initial data
            /// and GetBaseColor returns white for every tile. (#208 terrain-white)
            /// </summary>
            public static void ResetPrepared()
            {
                WorldPrepared = false;
                BaseLayers = null;
                CachedColors = null;
                // Reset LOD hysteresis so stale Cull entries from the prior world don't
                // delay voxel-tier entry for the new world's entities. (#208 lod-impostor)
                WorldSphereMod.LOD.LodSelector.ResetHysteresis();
            }

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
                // GUARD: _map_layers can be non-null but empty (Count==0) when
                // finishMakingWorld fires before WorldBox has populated the list.
                // Do NOT set WorldPrepared=true here — that would poison the flag
                // and prevent re-running once layers are populated. (#208 terrain-white)
                int layerCount = World.world._map_layers.Count;
                Debug.Log($"[WSM3D] Sphere.PrepareWorld: _map_layers.Count={layerCount} (0 = not yet populated, will retry)");
                if (layerCount == 0)
                {
                    Debug.LogWarning("[WSM3D] Sphere.PrepareWorld deferred — _map_layers is empty (WorldBox not yet populated layers). Become3D will retry via coroutine.");
                    return;
                }
                WorldPrepared = true;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                CreateTextures();
                Debug.Log($"[WSM3D][PERF] Sphere.PrepareWorld.CreateTextures={sw.Elapsed.TotalMilliseconds:F3}ms");
                sw.Restart();
                BaseLayers = new List<MapLayer>(World.world._map_layers);
                BaseLayers.Remove(FlashLayer);
                Debug.Log($"[WSM3D][PERF] Sphere.PrepareWorld.BaseLayersCopy={sw.Elapsed.TotalMilliseconds:F3}ms (layerCount={BaseLayers.Count})");
                sw.Restart();
                // CreateCachedColors BEFORE tilemap.redrawTiles() — the AddLayers
                // transpiler redirects MapLayer pixel writes to CachedColors via
                // GetPixelArray(Core.Sphere.CachedColors, layer). If CachedColors is
                // null when redrawTiles fires, GetPixelArray throws ArgumentNullException
                // and all pixel writes are lost → biome colors stay zero. (#208)
                CreateCachedColors();
                Debug.Log($"[WSM3D][PERF] Sphere.PrepareWorld.CreateCachedColors={sw.Elapsed.TotalMilliseconds:F3}ms");
                sw.Restart();
                // Force a synchronous 2D tilemap repaint AFTER CachedColors is ready,
                // so world_layer.pixels and every BaseLayers[i].pixels are populated
                // with real biome colors. In 3D mode tilemap.redrawTiles() is bypassed
                // every frame (Redraw3DTiles runs instead), so pixels stay all-zero
                // and GetBaseColor returns (0,0,0,0) → vertex color (0,0,0) → gray
                // under emission floor. (#208 terrain-gray root cause)
                try
                {
                    if (World.world?.tilemap != null)
                    {
                        World.world.tilemap.redrawTiles();
                        // Sample a few pixels to confirm non-zero biome color in log.
                        var wp = World.world.world_layer?.pixels;
                        int w = MapBox.width; int h = MapBox.height;
                        string sample = wp != null && w > 0 && h > 0
                            ? $"[{w/4},{h/4}]=({wp[h/4*w+w/4].r},{wp[h/4*w+w/4].g},{wp[h/4*w+w/4].b}) [{w/2},{h/2}]=({wp[h/2*w+w/2].r},{wp[h/2*w+w/2].g},{wp[h/2*w+w/2].b})"
                            : "(no pixels)";
                        Debug.Log($"[WSM3D] PrepareWorld: tilemap.redrawTiles() done — sample pixels: {sample}");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[WSM3D] PrepareWorld: tilemap.redrawTiles() failed — biome colors may be zero: " + ex.Message);
                }
                Debug.Log($"[WSM3D][PERF] Sphere.PrepareWorld.TilemapRepaint={sw.Elapsed.TotalMilliseconds:F3}ms");
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
                SafeInvoke("LogAssetBundleInventory threw", () => Mod.LogAssetBundleInventory(ab));
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
                // #204/#208: the ENTIRE wsm3d-shaders bundle is corrupt — baked against a
                // Shader serialization layout that mismatches the runtime Unity (2022.3.60f1),
                // so EVERY GetObject from it native-aborts (uncatchable ManagedStream error →
                // Crash!!!). Confirmed even OpaqueVertexColor aborts at Core.cs:1613
                // (Read 2872 vs expected 3980). Per-shader allowlisting cannot help. Until a
                // serialization-matched re-bake lands, skip the bundle ENTIRELY: leave shaderAb
                // null so the whole shader+compute load block below is bypassed. All shaders
                // fall back to Shader.Find/Standard; terrain still renders via the separate
                // 'worldsphere' bundle's CompoundSphere shader (loaded above, unaffected).
                if (ShaderBundleAvailable)
                {
                    try { shaderAb = AssetBundleUtils.GetAssetBundle("wsm3d-shaders"); }
                    catch { shaderAb = null; }
                }
                if (shaderAb == null)
                {
                    Debug.LogWarning("[WSM3D] wsm3d-shaders bundle not loaded (ShaderBundleAvailable=" + ShaderBundleAvailable + "). All shaders fall back to Shader.Find / Standard; GPU-compute path skipped.");
                }
                else
                {
                    // Gate removed: IsVerifiedSafeShaderBundle / FindShaderBundleManifest
                    // used Assembly.GetExecutingAssembly().Location which is empty/temp
                    // under NML's runtime Roslyn compile, causing the manifest to never
                    // be found and the gate to return false — keeping LoadedShaders empty
                    // and postFX dead (count=0). The per-shader loop below is fully
                    // crash-safe (try/catch + null + empty-name + !isSupported guards);
                    // no additional gate is needed. ADR-0013 note retained for context.
                    Debug.Log($"[WSM3D] hasBundleCache={shaderAb != null}; starting per-shader load (Assembly.Location gate removed).");
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
                        // SafeShaders is the per-name allowlist (see ADR-0013 / #204).
                        // only WSM3D/OpaqueVertexColor is known-good right now.
                        // Keep the loop narrow and explicitly skip any post-FX/broken
                        // names before touching AssetBundle.GetObject<Shader> so native
                        // deserialize crashes cannot occur from shader loading.
                        //
                        foreach (var shaderName in SafeShaders)
                        {
                            // #208 PLAYER-TEST FLIP REVERT 2026-06-05: pragma + SVC +2
                            // was insufficient — the 60f1 player still reads 80 bytes
                            // (expected 4520/4924/4952) for the three postFX shaders and
                            // 8 bytes (expected 2484) for CompoundSphereCompute. The C#
                            // try/catch saved us from a native abort, but shaders are
                            // rejected as "loaded with empty name" and consumers fall
                            // back. Next: IPreprocessShaders / IUnityLinker XML approach
                            // for forcing variant retention, or build the bundle with
                            // Unity 2022.3.60f1 to match the player's serializer.

                            // Never call GetObject for known-bad post-FX shader names
                            // from this bundle (BrpBloom / BrpACES) even if they are
                            // reintroduced later by another branch.
                            if (CorruptedShaderNames.Contains(shaderName))
                            {
                                Debug.LogWarning($"[WSM3D] Skipping GetObject for corrupted shader '{shaderName}' to avoid native crash during asset deserialization.");
                                continue;
                            }

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

                    // GPU-compute keystone (ADR-sota-gpu-compute-adoption): load the
                    // baked CompoundSphereCompute ComputeShader so the GPU-driven
                    // CompoundSpheres.Gpu manager / LegacyManagerShim can bind its
                    // CSMatrices/CSColors kernels (GpuKernels.Matrix/.Color). This is
                    // the binding that makes the buffer-driven GPU path constructible
                    // (no per-instance _Color cbuffer -> no magenta/green class).
                    // NOTE: a ComputeShader is NOT a UnityEngine.Shader, so it loads
                    // via GetObject<ComputeShader> on the .compute asset path.
                    try
                    {
                        UnityEngine.ComputeShader cs =
                            shaderAb.GetObject<UnityEngine.ComputeShader>("assets/wsm3d/shaders/compoundspherecompute.compute");
                        if (cs == null)
                            cs = shaderAb.GetObject<UnityEngine.ComputeShader>("Assets/WSM3D/Shaders/CompoundSphereCompute.compute");
                        if (cs != null)
                        {
                            CompoundCompute = cs;
                            Debug.Log($"[WSM3D] Loaded GPU-compute keystone: CompoundSphereCompute (kernels {CompoundSpheres.Gpu.GpuKernels.Matrix}/{CompoundSpheres.Gpu.GpuKernels.Color}) supported={SystemInfo.supportsComputeShaders}");
                        }
                        else
                        {
                            Debug.LogWarning("[WSM3D] CompoundSphereCompute not in wsm3d-shaders bundle — GPU-compute path unavailable; legacy CPU SphereManager path stays active.");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning("[WSM3D] CompoundSphereCompute load threw: " + ex.Message + " — GPU-compute path unavailable.");
                    }
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

                        if (fallback == null)
                        {
                            foreach (var n in BuiltInShaderFallbacks)
                            {
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

            // ADR-0013 (UPDATED 2026-05-31, #204) — the ManagedStream / "Mismatched
            // serialization in builtin class 'Shader'" crash was ROOT-CAUSED to the
            // bake pipeline emitting a variant-STRIPPED 80-byte shader stub, NOT a
            // patch-version mismatch. The earlier "re-bake with 60f1 didn't help"
            // conclusion was against that stripped stub: the on-disk wsm3d-shaders
            // bundle was only 80 bytes, so every Shader deserialized short and the
            // accumulated native errors crashed the player. OpaqueVertexColor only
            // survived because it is the simplest shader.
            //
            // ----------------------------------------------------------------
            // #204 CRASH-SAFETY: postFX shaders are VARIANT-STRIPPED STUBS in the
            // current bundle bake (80 bytes vs expected 12700/3980 bytes). Unity's
            // native deserializer aborts with:
            //   "Mismatched serialization in builtin class 'Shader'. Read 80 bytes
            //    but expected 12700 bytes" → ArgumentException: ManagedStream must
            //    be readable → process crash.
            // C# try/catch CANNOT intercept this native abort.
            //
            // SafeShaders is therefore restricted to ONLY the one known-valid shader
            // in this bundle (OpaqueVertexColor). BrpBloom, BrpACES, ColorGradingLUT,
            // ScreenSpaceGI, ScreenSpaceAO, and ProceduralSky are intentionally
            // EXCLUDED until the bundle is re-baked with full variant inclusion and
            // the serialised byte count is verified. PostFX consumers will fall back
            // to Standard (no postFX effects, but NO crash). Re-enable after a
            // verified-good re-bake (#204 bake unsolved).
            // ----------------------------------------------------------------
            // MASTER GATE: postFX shaders (BrpBloom, BrpACES, ColorGradingLUT,
            // ScreenSpaceGI, ScreenSpaceAO, ProceduralSky) are variant-stripped
            // 80-byte stubs in the current wsm3d-shaders bundle bake. Unity's
            // native deserializer aborts on GetObject<Shader> for these stubs with:
            //   "Mismatched serialization in builtin class 'Shader'.
            //    Read 80 bytes but expected 12700 bytes"
            //   ArgumentException: ManagedStream must be readable → process crash.
            // C# try/catch CANNOT intercept this native abort.
            //
            // #204/#208 — VARIANT-STRIPPING root cause FIXED 2026-06-01 (bundle md5
            // 8530b5f6): the bake now registers its ShaderVariantCollection in
            // GraphicsSettings.m_PreloadedShaders (the array Unity's AssetBundle
            // strip pass actually reads) + adds INSTANCING_ON variants for the
            // multi_compile_instancing shaders. The prior false positive was an
            // EDITOR-recompile load probe; the new validator checks
            // shader.subshaderCount (reads serialized bundle bytes) → 12 valid, 0
            // stubs (all postFX subshaderCount>=2). Re-enabling; the GAME runtime
            // is the ground-truth confirm. If a native ManagedStream abort recurs,
            // flip both back to false (crash-safe) — the per-shader load loop keeps
            // its try/catch + null + isSupported guards regardless.
            // 2026-06-01 GAME-RUNTIME RESULT: re-enable STILL crashed. OVC now loads
            // cleanly from the bundle (strip fix worked for it), but the 6 postFX
            // shaders are STILL player-side 80-byte stubs → "Read 80 expected 12700"
            // native abort. So subshaderCount (12/0 in editor) is ALSO a false
            // positive — the editor recompiles; only the PLAYER is truth. postFX
            // shaders need their FULL variant set in the SVC (INSTANCING_ON + their
            // own keywords/passes) — not yet achieved. Crash-safe until then.
            // #208 candidate test (bundle e6589a46): un-bundled SVC + WSM3D_POSTFX_KEEP.
            // Auto-reverts to false on any ManagedStream crash (game-test driven).
            // PostFX remains disabled until shader re-bake is fixed; keep bundle
            // enumeration for post-FX shaders fully disabled.
            public const bool PostFxShaderBundleAvailable = false; // #208 PLAYER-TEST FLIP REVERT 2026-06-05: pragma+SVC+2 still produces 80-byte stubs in 60f1 player (4520/4924/4952 expected). Reverting until IUnityLinker XML / SVC dispatch proves root-cause.

            public const bool ShaderBundleAvailable = true;

            // Names of corrupted postFX shaders that must never be loaded via
            // GetObject<Shader> because they can crash the runtime during native
            // shader deserialization.
            public static readonly System.Collections.Generic.HashSet<string> PostFxShaderNames =
                new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
                {
                    "BrpBloom", "BrpACES", "ColorGradingLUT",
                    "ScreenSpaceGI", "ScreenSpaceAO", "ProceduralSky",
                };

            public static readonly System.Collections.Generic.HashSet<string> CorruptedShaderNames =
                new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
                {
                    "BrpBloom",
                    "BrpACES",
                };

            // ADR-0013 SafeShaders allowlist. Only OpaqueVertexColor survives
            // bundle deserialization with a valid Shader.name on this Unity
            // 2022.3 cross-patch build; the other wsm3d-shaders-bundle shaders
            // trigger a native ManagedStream crash that C# try/catch cannot
            // intercept and would take the whole mod offline. DO NOT ADD MORE
            // SHADERS to SafeShaders without re-validating the bundle on the
            // real Unity 60f1 runtime — see ADR-0013 for full rationale.
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
                // BuildTerrainTextureAtlas disabled — atlas UV mapping not ready
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

                void BuildTerrainTextureAtlas(List<Sprite> sprites)
                {
                    int count = sprites.Count;
                    if (count <= 0)
                    {
                        TerrainTextureAtlasCols = 1;
                        TerrainTextureAtlasRows = 1;
                        TerrainTextureAtlas = Texture2D.whiteTexture;
                        return;
                    }

                    TerrainTextureAtlasCols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(count)));
                    TerrainTextureAtlasRows = Mathf.CeilToInt((float)count / TerrainTextureAtlasCols);
                    TerrainTextureAtlas = new Texture2D(
                        TerrainTextureAtlasCols * TerrainTextureAtlasTileSize,
                        TerrainTextureAtlasRows * TerrainTextureAtlasTileSize,
                        TextureFormat.RGBA32,
                        false,
                        false)
                    {
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp
                    };

                    for (int i = 0; i < count; i++)
                    {
                        int tileCol = i % TerrainTextureAtlasCols;
                        int tileRow = i / TerrainTextureAtlasCols;
                        int baseX = tileCol * TerrainTextureAtlasTileSize;
                        int baseY = tileRow * TerrainTextureAtlasTileSize;
                        Color32[] pixels = GetTruePixels(sprites[i]);

                        for (int py = 0; py < TerrainTextureAtlasTileSize; py++)
                        {
                            for (int px = 0; px < TerrainTextureAtlasTileSize; px++)
                            {
                                int sourceIndex = (py * TerrainTextureAtlasTileSize) + px;
                                TerrainTextureAtlas.SetPixel(baseX + px, baseY + py,
                                    sourceIndex < pixels.Length ? pixels[sourceIndex] : Color.clear);
                            }
                        }
                    }

                    TerrainTextureAtlas.Apply();
                }
            }
        }
    }
}
