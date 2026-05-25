using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using NCMS.Utils;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.IO;
namespace WorldSphereMod.UI
{
    struct ButtonData
    {
        public PowerToggleAction Action;
        public string Name;
        public string Description;
        public string IconPath;
        public bool IsActive;
        public bool CanBeFalse;
        public ButtonData(string Name, string Description, string IconPath, bool IsActive, PowerToggleAction Action, bool CanBeFalse = true)
        {
            this.Name = Name;
            this.Description = Description;
            this.IconPath = IconPath;
            this.IsActive = IsActive;
            this.Action = Action;
            this.CanBeFalse = CanBeFalse;
        }
    }
    public static class WorldSphereTab
    {
        public static PowersTab Tab;
        public static Sprite ModIcon;
        const string FallbackIconPath = "WorldSphereMod/ModIcon";
        const string PhasesWindowId = "3D Phases";
        const string PhasesWindowTitle = "phases_window";
        static readonly Dictionary<string, Sprite?> IconCache = new Dictionary<string, Sprite?>();
        static readonly Dictionary<string, Sprite?> PhaseIconCache = new Dictionary<string, Sprite?>();
        static GameObject Space;
        static GameObject Line;
        static bool _isPhasesWindowSuppressionHooked;
        static void CreateTabTools()
        {
            Space = ResourcesFinder.FindResource<GameObject>("_space");
            Line = Object.Instantiate(ResourcesFinder.FindResource<GameObject>("_line"));
            Line.transform.localScale = new Vector3(Line.transform.localScale.x, Line.transform.localScale.y * 6, Line.transform.localScale.z);
        }

        public static void Begin()
        {
            CreateTabTools();
            CreateTab();
            CreateButtons();
            SuppressPhasesWindow();
            EnsurePhasesWindowAutoCloseHook();
        }
        static void AddLine()
        {
            Object.Instantiate(Line).transform.SetParent(Tab.transform);
        }

        static void CreateTab()
        {
            ModIcon = SafeLoadSprite("WorldSphereMod/ModIcon");
            Tab = TabManager.CreateTab("WorldSphereMod", "world_sphere_tab", "world_sphere_tab_desc", ModIcon, "world_sphere_tab_author");
        }
        public static Sprite SafeLoadSprite(string path)
        {
            if (IconCache.TryGetValue(path, out var cachedSprite))
            {
                return cachedSprite;
            }

            Sprite? sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
            {
                global::UnityEngine.Debug.LogWarning($"[WSM3D] Sprite resource not found: {path} - falling back to ModIcon");
                if (!IconCache.TryGetValue(FallbackIconPath, out sprite))
                {
                    sprite = Resources.Load<Sprite>(FallbackIconPath);
                    IconCache[FallbackIconPath] = sprite;
                }
            }
            IconCache[path] = sprite;
            return sprite;
        }

        public static void SetGodPowerSprite(ref GodPower power, string iconPath)
        {
            var sprite = SafeLoadSprite(iconPath);
            if (sprite == null)
            {
                return;
            }

            var powerType = typeof(GodPower);
            const BindingFlags Binding = BindingFlags.Public | BindingFlags.Instance;
            var iconField = powerType.GetField("icon", Binding);
            if (iconField != null && iconField.FieldType == typeof(Sprite))
            {
                iconField.SetValue(power, sprite);
                return;
            }
            var spriteField = powerType.GetField("sprite", Binding);
            if (spriteField != null && spriteField.FieldType == typeof(Sprite))
            {
                spriteField.SetValue(power, sprite);
                return;
            }
            var iconProperty = powerType.GetProperty("icon", Binding);
            if (iconProperty != null && iconProperty.CanWrite && iconProperty.PropertyType == typeof(Sprite))
            {
                iconProperty.SetValue(power, sprite, null);
                return;
            }
            var spriteProperty = powerType.GetProperty("sprite", Binding);
            if (spriteProperty != null && spriteProperty.CanWrite && spriteProperty.PropertyType == typeof(Sprite))
            {
                spriteProperty.SetValue(power, sprite, null);
            }
        }
        public static Text addText(string window, string textString, GameObject parent, int sizeFont, Vector3 pos, Vector2 addSize = default(Vector2))
        {
            GameObject textRef = GameObject.Find($"/Canvas Container Main/Canvas - Windows/windows/" + window + "/Background/Title");
            GameObject textGo = Object.Instantiate(textRef, parent.transform);
            textGo.SetActive(true);

            var textComp = textGo.GetComponent<Text>();
            textComp.fontSize = sizeFont;
            textComp.resizeTextMaxSize = sizeFont;
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.position = new Vector3(0, 0, 0);
            textRect.localPosition = pos + new Vector3(0, -50, 0);
            textRect.sizeDelta = new Vector2(100, 100) + addSize;
            textGo.AddComponent<GraphicRaycaster>();
            textComp.text = textString;

            return textComp;
        }
        static Slider GenerateSlider(string Name,float Min, float Max, float Current, UnityAction<float> Func, string Window)
        {
            GameObject sliderGO = new GameObject(Name, typeof(Slider), typeof(Image));
            Transform Parent = WindowManager.windows[Window].Object.transform;
            sliderGO.transform.SetParent(Parent, false);
            RectTransform rt = sliderGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(25, 5);
            rt.anchoredPosition = new Vector2(0, 0);
            Slider slider = sliderGO.GetComponent<Slider>();
            slider.minValue = Min;
            slider.maxValue = Max;
            slider.value = Current;
            slider.onValueChanged.AddListener(Func);

            GameObject trackGO = new GameObject("Track");
            trackGO.transform.SetParent(sliderGO.transform, false);
            Image trackImage = trackGO.AddComponent<Image>();
            RectTransform trackRect = trackGO.GetComponent<RectTransform>();
            trackRect.sizeDelta = new Vector2(100, 2);
            trackRect.anchoredPosition = Vector2.zero;
            trackImage.color = Color.gray;

            GameObject handleAreaGO = new GameObject("Handle Slide Area");
            handleAreaGO.transform.SetParent(sliderGO.transform, false);
            RectTransform handleAreaRect = handleAreaGO.AddComponent<RectTransform>();
            handleAreaRect.sizeDelta = new Vector2(100, 0);

            GameObject handleGO = new GameObject("Handle");
            handleGO.transform.SetParent(handleAreaGO.transform, false);
            Image handleImage = handleGO.AddComponent<Image>();
            RectTransform handleRect = handleGO.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(10, 10);
            handleImage.color = Color.white;

            slider.targetGraphic = handleImage;
            slider.handleRect = handleGO.GetComponent<RectTransform>();

            Text textGO = addText(Window, $"{Name} : {Current}", sliderGO, 10, new Vector3(0, -2));
            textGO.text = $"{LM.Get(Name)} : {Current}";
            slider.onValueChanged.AddListener((float x) => textGO.text = $"{LM.Get(Name)} : {x}");

            return slider;
        }
        static void CreateButtons()
        {
            CreateToggleButton("Is3D", "WorldSphereMod/ModIcon", "is_3d", "is_3d_description", Toggle3D, Core.savedSettings.Is3D);
            CreateWindowButton("Sprite Settings", "WorldSphereMod/Rotate", "sprite_settings_window", new List<ButtonData>()
            {
               ///new ButtonData("sprites_rotate_to_camera", "sprites_rotate_to_camera_description", "WorldSphereMod/Rotate", Core.savedSettings.RotateStuffToCamera, ToggleRotations),
               new ButtonData("sprites_rotate_to_camera", "sprites_rotate_to_camera_description", "WorldSphereMod/Rotate", Core.savedSettings.RotateStuffToCamera, ToggleRotations),
               new ButtonData("building_style_procgen", "building_style_procgen_description", "WorldSphereMod/World", Core.savedSettings.BuildingStyleProcgen, ToggleBuildingStyleProcgen)
            }
            );
            GenerateSlider("building_size", 0.1f, 5f, Core.savedSettings.BuildingSize, (float val) => { Core.savedSettings.BuildingSize = val; Core.SaveSettings(); }, "Sprite Settings");
            CreateWindowButton("Camera Settings", "WorldSphereMod/Camera", "camera_settings_window", new List<ButtonData>()
            {
                new ButtonData("inverted_camera", "inverted_camera_description", "WorldSphereMod/Camera", Core.savedSettings.InvertedCameraMovement, ToggleCamera),
                new ButtonData("first_person", "first_person_description", "WorldSphereMod/Camera", Core.savedSettings.FirstPerson, ToggleFirtPerson),
                new ButtonData("camera_rotates_with_world", "camera_rotates_with_world_description", "WorldSphereMod/Camera", Core.savedSettings.CameraRotatesWithWorld, ToggleRotateToWorld),
                new ButtonData("upside_down_movement", "upside_down_movement_description", "WorldSphereMod/Camera", Core.savedSettings.UpsideDownMovement, UpsideDown)
            });
            GenerateSlider("render_distance", 1, 20, Core.savedSettings.RenderRange, (float val) => { Core.savedSettings.RenderRange = val; Core.SaveSettings(); }, "Camera Settings");
            CreateWindowButton("World Settings", "WorldSphereMod/World", "world_settings_window", new List<ButtonData>()
            {
                new ButtonData("cylindrical_shape", "cylindrical_shape_description", "WorldSphereMod/Round", Core.savedSettings.CurrentShape == 1, SetShape, false),
                new ButtonData("flat_shape", "flat_shape_description", "WorldSphereMod/Flat", Core.savedSettings.CurrentShape == 0, SetShape, false),
                new ButtonData("perlin_noise", "perlin_noise_description", "WorldSphereMod/PerlinNoise", Core.savedSettings.PerlinNoise, PerlinNoise)
            });
            GenerateSlider("tile_length_multiplier", 1, 10, Core.savedSettings.TileHeight, (float x) => { Core.savedSettings.TileHeight = x; Core.SaveSettings(); }, "World Settings");

            // v2 fork: per-phase toggles. The default values come from
            // SavedSettings; the toggle action flips + persists. Without
            // surfacing these here the user has no way to turn Phase 1's
            // voxel actors on, so sprites stay 2D and the fork looks like
            // a no-op compared to upstream.
            CreateWindowButton(PhasesWindowId, "WorldSphereMod/ModIcon", PhasesWindowTitle, new List<ButtonData>()
            {
                new ButtonData("voxel_entities",       "voxel_entities_description",       "WorldSphereMod/Round",        Core.savedSettings.VoxelEntities,       TogglePhase),
                new ButtonData("procedural_buildings", "procedural_buildings_description", "WorldSphereMod/World",         Core.savedSettings.ProceduralBuildings, TogglePhase),
                new ButtonData("crossed_quad_foliage", "crossed_quad_foliage_description", "WorldSphereMod/Flat",          Core.savedSettings.CrossedQuadFoliage, TogglePhase),
                new ButtonData("biome_blending",       "biome_blending_description",       "WorldSphereMod/World",         Core.savedSettings.BiomeBlending,       TogglePhase),
                new ButtonData("mesh_water",           "mesh_water_description",           "WorldSphereMod/PerlinNoise",   Core.savedSettings.MeshWater,           TogglePhase),
                new ButtonData("mountain_slope_smoothing", "mountain_slope_smoothing_description", "WorldSphereMod/World", Core.savedSettings.MountainSlopeSmoothing, TogglePhase),
                new ButtonData("high_shadows",         "high_shadows_description",         "WorldSphereMod/SkyBox",        Core.savedSettings.HighShadows,         TogglePhase),
                new ButtonData("hdr_skybox",           "hdr_skybox_description",           "WorldSphereMod/SkyBox",        Core.savedSettings.HdrSkybox,           TogglePhase),
                new ButtonData("color_grading_lut",    "color_grading_lut_description",    "WorldSphereMod/ModIcon",       Core.savedSettings.ColorGradingLut,      TogglePhase),
                new ButtonData("ssao_enabled",         "ssao_enabled_description",         "WorldSphereMod/ModIcon",       Core.savedSettings.SSAOEnabled,          TogglePhase),
                new ButtonData("ssgi_enabled",         "ssgi_enabled_description",         "WorldSphereMod/ModIcon",       Core.savedSettings.SSGIEnabled,          TogglePhase),
                new ButtonData("bloom_enabled",        "bloom_enabled_description",        "WorldSphereMod/ModIcon",       Core.savedSettings.BloomEnabled,         TogglePhase),
                new ButtonData("aces_tonemapping",     "aces_tonemapping_description",     "WorldSphereMod/ModIcon",       Core.savedSettings.ACESTonemapping,      TogglePhase),
                new ButtonData("skeletal_animation",   "skeletal_animation_description",   "WorldSphereMod/Rotate",        Core.savedSettings.SkeletalAnimation,   TogglePhase),
                new ButtonData("worldspace_ui",        "worldspace_ui_description",        "WorldSphereMod/Camera",        Core.savedSettings.WorldspaceUI,        TogglePhase),
                new ButtonData("worldspace_health_3d", "worldspace_health_3d_description", "WorldSphereMod/ModIcon",      Core.savedSettings.WorldspaceHealth3D,  TogglePhase),
                new ButtonData("day_night_cycle",      "day_night_cycle_description",      "WorldSphereMod/SkyBox",        Core.savedSettings.DayNightCycle,       TogglePhase),
                new ButtonData("weather_rain",          "weather_rain_description",         "WorldSphereMod/ModIcon",       Core.savedSettings.WeatherRain,           TogglePhase),
                new ButtonData("weather_snow",          "weather_snow_description",         "WorldSphereMod/ModIcon",       Core.savedSettings.WeatherSnow,           TogglePhase),
                new ButtonData("weather_lightning",     "weather_lightning_description",    "WorldSphereMod/ModIcon",       Core.savedSettings.WeatherLightning,      TogglePhase),
                new ButtonData("post_fx",              "post_fx_description",              "WorldSphereMod/ModIcon",       Core.savedSettings.PostFX,              TogglePhase),
                new ButtonData("particle_effects",     "particle_effects_description",     "WorldSphereMod/Logo",          Core.savedSettings.ParticleEffects,     TogglePhase),
                new ButtonData("sanity_cube",           "sanity_cube_description",           "WorldSphereMod/ModIcon",       Core.savedSettings.DebugSanityCube,     ToggleDebugSanityCube),
            });

            CreateButton("Open Sprites", "WorldSphereMod/ModIcon", OpenSprites);

            // Phase 10 / R&D QoL: ProfilerDump toggle (also drives the in-game
            // RuntimeStatsOverlay since the overlay's OnGUI gates on the same
            // flag) and a destructive Reset-to-defaults action.
            CreateToggleButton("ProfileMode", "WorldSphereMod/ModIcon", "profile_mode", "profile_mode_description", ToggleProfileMode, Core.savedSettings.ProfilerDump);
            CreateButton("Reset Defaults", "WorldSphereMod/ModIcon", ResetToDefaults);
        }

        public static void PreloadPhaseIcons()
        {
            string[] phaseIconNames =
            {
                "CrossedQuadFoliage",
                "DayNightCycle",
                "HdrSkybox",
                "HighShadows",
                "MeshWater",
                "ProceduralBuildings",
                "SkeletalAnimation",
                "SSGIEnabled",
                "VoxelEntities",
                "WorldspaceUI"
            };

            foreach (string iconName in phaseIconNames)
            {
                GetPhaseIcon(iconName);
            }
        }

        public static Sprite? GetPhaseIcon(string iconName)
        {
            if (PhaseIconCache.TryGetValue(iconName, out var cachedSprite))
            {
                return cachedSprite;
            }

            string iconPath = Path.Combine(Mod.ModDirectory, "GameResources", "PhaseIcons", $"{iconName}.png");
            if (!File.Exists(iconPath))
            {
                PhaseIconCache[iconName] = null;
                return null;
            }

            try
            {
                byte[] data = File.ReadAllBytes(iconPath);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!TryLoadPngViaReflection(texture, data))
                {
                    PhaseIconCache[iconName] = null;
                    return null;
                }

                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f
                );
                PhaseIconCache[iconName] = sprite;
                return sprite;
            }
            catch (System.Exception ex)
            {
                global::UnityEngine.Debug.LogWarning($"[WSM3D] Failed to load phase icon '{iconName}': {ex.Message}");
                PhaseIconCache[iconName] = null;
                return null;
            }
        }

        static bool TryLoadPngViaReflection(Texture2D tex, byte[] bytes)
        {
            try
            {
                var miInstance = typeof(Texture2D).GetMethod(
                    "LoadImage",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new System.Type[] { typeof(byte[]) },
                    null);
                if (miInstance != null)
                {
                    object result = miInstance.Invoke(tex, new object[] { bytes });
                    if (result is bool b1)
                    {
                        return b1;
                    }
                    return true;
                }

                var icType = typeof(Texture2D).Assembly.GetType("UnityEngine.ImageConversion");
                if (icType != null)
                {
                    var miStatic = icType.GetMethod(
                        "LoadImage",
                        BindingFlags.Static | BindingFlags.Public,
                        null,
                        new System.Type[] { typeof(Texture2D), typeof(byte[]) },
                        null);
                    if (miStatic != null)
                    {
                        object result = miStatic.Invoke(null, new object[] { tex, bytes });
                        if (result is bool b2)
                        {
                            return b2;
                        }
                        return true;
                    }
                }
            }
            catch (System.Exception ex)
            {
                global::UnityEngine.Debug.LogWarning($"[WSM3D] TryLoadPngViaReflection threw: {ex.GetType().Name}: {ex.Message}");
            }

            return false;
        }

        static void TogglePhase(string phaseToggleId)
        {
            if (!TryResolvePhaseToggleField(phaseToggleId, out FieldInfo? settingField))
            {
                global::UnityEngine.Debug.LogWarning($"[WSM3D] Missing SavedSettings field for phase toggle '{phaseToggleId}'.");
                WorldSphereMod.Worldspace.PhaseToast.ShowError($"{phaseToggleId} could not be toggled: unknown setting");
                return;
            }

            bool nextValue = !(settingField.GetValue(Core.savedSettings) as bool? ?? false);
            settingField.SetValue(Core.savedSettings, nextValue);

            try
            {
                Core.ApplyPhaseToggle(settingField.Name, nextValue);
            }
            catch (System.Exception ex)
            {
                string reason = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                global::UnityEngine.Debug.LogError($"[WSM3D] {settingField.Name} toggle failed: {ex}");
                WorldSphereMod.Worldspace.PhaseToast.ShowError($"{settingField.Name} could not be {(nextValue ? "enabled" : "disabled")}: {reason}");
                // Revert the setting since the toggle failed
                settingField.SetValue(Core.savedSettings, !nextValue);
                Core.SaveSettings();
                return;
            }

            Core.SaveSettings();

            if (!PlayerConfig.dict.ContainsKey(phaseToggleId))
            {
                PlayerConfig.dict.Add(phaseToggleId, new PlayerOptionData(phaseToggleId));
            }
            PlayerConfig.dict[phaseToggleId].boolVal = nextValue;

            if (settingField.Name == nameof(SavedSettings.BiomeBlending) && Core.IsWorld3D)
            {
                Core.Sphere.RefreshColors();
            }
        }

        static void EnsurePhasesWindowAutoCloseHook()
        {
            if (_isPhasesWindowSuppressionHooked)
            {
                return;
            }

            _isPhasesWindowSuppressionHooked = true;
            MapBox.on_world_loaded += SuppressPhasesWindowOnWorldLoad;
        }

        static void SuppressPhasesWindowOnWorldLoad()
        {
            // Skip suppression on first install so FirstRunWelcome can open the window.
            if (Core.IsFirstInstall && !Core.savedSettings.HasSeenWelcome)
            {
                MapBox.on_world_loaded -= SuppressPhasesWindowOnWorldLoad;
                return;
            }
            try
            {
                SuppressPhasesWindow();
            }
            catch (System.Exception ex)
            {
                global::UnityEngine.Debug.LogWarning($"[WSM3D] Failed to suppress 3D Phases modal on world load: {ex.Message}");
            }
            finally
            {
                MapBox.on_world_loaded -= SuppressPhasesWindowOnWorldLoad;
            }
        }

        static void SuppressPhasesWindow()
        {
            bool configChanged = false;
            if (!PlayerConfig.dict.TryGetValue(PhasesWindowId, out var optionData))
            {
                optionData = new PlayerOptionData(PhasesWindowId);
                PlayerConfig.dict.Add(PhasesWindowId, optionData);
                configChanged = true;
            }

            if (optionData.boolVal)
            {
                optionData.boolVal = false;
                configChanged = true;
            }

            if (configChanged)
            {
                PlayerConfig.saveData();
            }

            ClosePhasesWindow();
        }

        static void ClosePhasesWindow()
        {
            TryHideWindowByName(PhasesWindowId);
            TryCloseWindowViaReflection(PhasesWindowId);
        }

        static void TryHideWindowByName(string windowId)
        {
            GameObject windowRoot = GameObject.Find($"/Canvas Container Main/Canvas - Windows/windows/{windowId}");
            if (windowRoot != null)
            {
                windowRoot.SetActive(false);
            }
        }

        static void TryCloseWindowViaReflection(string windowId)
        {
            MethodInfo? hideMethod = typeof(Windows).GetMethod(
                "HideWindow",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            );

            if (hideMethod == null)
            {
                hideMethod = typeof(Windows).GetMethod(
                    "CloseWindow",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                );
            }

            if (hideMethod == null)
            {
                return;
            }

            ParameterInfo[] parameters = hideMethod.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
            {
                return;
            }

            hideMethod.Invoke(null, new object[] { windowId });
        }

        static bool TryResolvePhaseToggleField(string toggleId, out FieldInfo? settingField)
        {
            settingField = typeof(SavedSettings).GetField(toggleId);
            if (settingField != null)
            {
                return true;
            }

            string normalizedToggle = NormalizeSettingId(toggleId);
            foreach (FieldInfo field in typeof(SavedSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(bool))
                {
                    continue;
                }
                if (string.Equals(NormalizeSettingId(field.Name), normalizedToggle, System.StringComparison.OrdinalIgnoreCase))
                {
                    settingField = field;
                    return true;
                }
            }

            return false;
        }
        static string NormalizeSettingId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }

            System.Text.StringBuilder normalized = new System.Text.StringBuilder(id.Length);
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (char.IsLetterOrDigit(c))
                {
                    normalized.Append(char.ToLowerInvariant(c));
                }
            }
            return normalized.ToString();
        }
        static void ToggleDebugSanityCube(string _)           { Core.savedSettings.DebugSanityCube     = !Core.savedSettings.DebugSanityCube;     Core.SaveSettings(); }
        static void ToggleProfileMode()
        {
            Core.savedSettings.ProfilerDump = !Core.savedSettings.ProfilerDump;
            if (Core.savedSettings.ProfilerDump)
            {
                WorldSphereMod.Worldspace.RuntimeStatsOverlay.EnsureCreated();
            }
            Core.SaveSettings();
        }
        static void ResetToDefaults()
        {
            bool previousVoxelEntities = Core.savedSettings.VoxelEntities;
            bool previousProceduralBuildings = Core.savedSettings.ProceduralBuildings;
            bool previousCrossedQuadFoliage = Core.savedSettings.CrossedQuadFoliage;
            bool previousBiomeBlending = Core.savedSettings.BiomeBlending;
            bool previousMeshWater = Core.savedSettings.MeshWater;
            bool previousMountainSlopeSmoothing = Core.savedSettings.MountainSlopeSmoothing;
            bool previousHighShadows = Core.savedSettings.HighShadows;
            bool previousHdrSkybox = Core.savedSettings.HdrSkybox;
            bool previousColorGradingLut = Core.savedSettings.ColorGradingLut;
            bool previousSSAOEnabled = Core.savedSettings.SSAOEnabled;
            bool previousSSGIEnabled = Core.savedSettings.SSGIEnabled;
            bool previousBloomEnabled = Core.savedSettings.BloomEnabled;
            bool previousACESTonemapping = Core.savedSettings.ACESTonemapping;
            bool previousSkeletalAnimation = Core.savedSettings.SkeletalAnimation;
            bool previousWorldspaceUI = Core.savedSettings.WorldspaceUI;
            bool previousWorldspaceHealth3D = Core.savedSettings.WorldspaceHealth3D;
            bool previousDayNightCycle = Core.savedSettings.DayNightCycle;
            bool previousWeatherRain = Core.savedSettings.WeatherRain;
            bool previousWeatherSnow = Core.savedSettings.WeatherSnow;
            bool previousWeatherLightning = Core.savedSettings.WeatherLightning;
            bool previousPostFX = Core.savedSettings.PostFX;
            bool previousParticleEffects = Core.savedSettings.ParticleEffects;

            Core.savedSettings = new SavedSettings();
            Core.SaveSettings();

            if (previousVoxelEntities != Core.savedSettings.VoxelEntities)               Core.ApplyPhaseToggle(nameof(SavedSettings.VoxelEntities),       Core.savedSettings.VoxelEntities);
            if (previousProceduralBuildings != Core.savedSettings.ProceduralBuildings)   Core.ApplyPhaseToggle(nameof(SavedSettings.ProceduralBuildings), Core.savedSettings.ProceduralBuildings);
            if (previousCrossedQuadFoliage != Core.savedSettings.CrossedQuadFoliage)     Core.ApplyPhaseToggle(nameof(SavedSettings.CrossedQuadFoliage),  Core.savedSettings.CrossedQuadFoliage);
            if (previousBiomeBlending != Core.savedSettings.BiomeBlending && Core.IsWorld3D) Core.Sphere.RefreshColors();
            if (previousMeshWater != Core.savedSettings.MeshWater)                       Core.ApplyPhaseToggle(nameof(SavedSettings.MeshWater),           Core.savedSettings.MeshWater);
            if (previousMountainSlopeSmoothing != Core.savedSettings.MountainSlopeSmoothing)         Core.ApplyPhaseToggle(nameof(SavedSettings.MountainSlopeSmoothing),    Core.savedSettings.MountainSlopeSmoothing);
            if (previousHighShadows != Core.savedSettings.HighShadows)                   Core.ApplyPhaseToggle(nameof(SavedSettings.HighShadows),         Core.savedSettings.HighShadows);
            if (previousHdrSkybox != Core.savedSettings.HdrSkybox)                       Core.ApplyPhaseToggle(nameof(SavedSettings.HdrSkybox),           Core.savedSettings.HdrSkybox);
            if (previousColorGradingLut != Core.savedSettings.ColorGradingLut)         Core.ApplyPhaseToggle(nameof(SavedSettings.ColorGradingLut),      Core.savedSettings.ColorGradingLut);
            if (previousSSAOEnabled != Core.savedSettings.SSAOEnabled)                 Core.ApplyPhaseToggle(nameof(SavedSettings.SSAOEnabled),          Core.savedSettings.SSAOEnabled);
            if (previousSSGIEnabled != Core.savedSettings.SSGIEnabled)                 Core.ApplyPhaseToggle(nameof(SavedSettings.SSGIEnabled),          Core.savedSettings.SSGIEnabled);
            if (previousBloomEnabled != Core.savedSettings.BloomEnabled)               Core.ApplyPhaseToggle(nameof(SavedSettings.BloomEnabled),        Core.savedSettings.BloomEnabled);
            if (previousACESTonemapping != Core.savedSettings.ACESTonemapping)         Core.ApplyPhaseToggle(nameof(SavedSettings.ACESTonemapping),      Core.savedSettings.ACESTonemapping);
            if (previousSkeletalAnimation != Core.savedSettings.SkeletalAnimation)       Core.ApplyPhaseToggle(nameof(SavedSettings.SkeletalAnimation),   Core.savedSettings.SkeletalAnimation);
            if (previousWorldspaceUI != Core.savedSettings.WorldspaceUI)                 Core.ApplyPhaseToggle(nameof(SavedSettings.WorldspaceUI),        Core.savedSettings.WorldspaceUI);
            if (previousWorldspaceHealth3D != Core.savedSettings.WorldspaceHealth3D)     Core.ApplyPhaseToggle(nameof(SavedSettings.WorldspaceHealth3D), Core.savedSettings.WorldspaceHealth3D);
            if (previousDayNightCycle != Core.savedSettings.DayNightCycle)               Core.ApplyPhaseToggle(nameof(SavedSettings.DayNightCycle),       Core.savedSettings.DayNightCycle);
            if (previousWeatherRain != Core.savedSettings.WeatherRain)                   Core.ApplyPhaseToggle(nameof(SavedSettings.WeatherRain),         Core.savedSettings.WeatherRain);
            if (previousWeatherSnow != Core.savedSettings.WeatherSnow)                   Core.ApplyPhaseToggle(nameof(SavedSettings.WeatherSnow),         Core.savedSettings.WeatherSnow);
            if (previousWeatherLightning != Core.savedSettings.WeatherLightning)         Core.ApplyPhaseToggle(nameof(SavedSettings.WeatherLightning),    Core.savedSettings.WeatherLightning);
            if (previousPostFX != Core.savedSettings.PostFX)                             Core.ApplyPhaseToggle(nameof(SavedSettings.PostFX),              Core.savedSettings.PostFX);
            if (previousParticleEffects != Core.savedSettings.ParticleEffects)           Core.ApplyPhaseToggle(nameof(SavedSettings.ParticleEffects),     Core.savedSettings.ParticleEffects);

            UnityEngine.Debug.Log("[WSM3D] SavedSettings reset to defaults. Restart recommended for full effect.");
        }
        static void OpenSprites()
        {
            Application.OpenURL("file://" + Mod.ModDirectory + "/GameResources/WorldSphereMod");
        }
        static Dictionary<string, int> WorldShapes = new Dictionary<string, int>()
        {
            { "flat_shape", 0 },
            { "cylindrical_shape", 1 }
        };
        static void PerlinNoise(string ID)
        {
            Core.savedSettings.PerlinNoise = !Core.savedSettings.PerlinNoise;
            Core.SaveSettings();
        }
        static void UpsideDown(string ID)
        {
            Core.savedSettings.UpsideDownMovement = !Core.savedSettings.UpsideDownMovement;
            Core.SaveSettings();
        }
        static void SetShape(string ID)
        {
            Core.savedSettings.CurrentShape = WorldShapes[ID];
            foreach(string shape in WorldShapes.Keys)
            {
                if(shape != ID)
                {
                    PlayerOptionData tData = PlayerConfig.dict[shape];
                    tData.boolVal = false;
                }
                PowerButtonSelector.instance.checkToggleIcons();
            }
            Core.SaveSettings();
        }
        static void Toggle3D()
        {
            Core.savedSettings.Is3D = !Core.savedSettings.Is3D;
            Core.SaveSettings();
        }
        static void ToggleRotations(string _)
        {
            Core.savedSettings.RotateStuffToCamera = !Core.savedSettings.RotateStuffToCamera;
            Core.SaveSettings();
        }
        static void ToggleBuildingStyleProcgen(string _)
        {
            Core.savedSettings.BuildingStyleProcgen = !Core.savedSettings.BuildingStyleProcgen;
            Core.SaveSettings();
        }
        static void ToggleFirtPerson(string _)
        {
            Core.savedSettings.FirstPerson = !Core.savedSettings.FirstPerson;
            Core.SaveSettings();
        }
        static void ToggleRotateToWorld(string _)
        {
            Core.savedSettings.CameraRotatesWithWorld = !Core.savedSettings.CameraRotatesWithWorld;
            Core.SaveSettings();
        }
        static void ToggleCamera(string _)
        {
            Core.savedSettings.InvertedCameraMovement = !Core.savedSettings.InvertedCameraMovement;
            Core.SaveSettings();
        }
        #region Buttons
        static PowerWindow CreateWindowButton(string ID, string IconPath, string WindowDescription, List<ButtonData> Buttons)
        {
            WindowManager.CreateWindow(ID, WindowDescription, Buttons);
            CreateButton(ID, IconPath, delegate () { WindowManager.OpenWindow(ID); });
            return WindowManager.windows[ID];
        }
        static void CreateButton(string ID, string IconPath, UnityAction Action)
        {
            PowerButton button = PowerButtonCreator.CreateSimpleButton(ID, Action, SafeLoadSprite(IconPath));
            PowerButtonCreator.AddButtonToTab(button, Tab);
        }
        static void CreateToggleButton(string ID, string IconPath, string name, string Description, UnityAction toggleAction, bool Enabled)
        {
            GodPower power = new GodPower()
            {
                id = ID,
                name = name,
                toggle_name = ID,
                toggle_action = delegate
                {
                    toggleAction();
                    PlayerConfig.dict[ID].boolVal = !PlayerConfig.dict[ID].boolVal;
                    PowerButtonSelector.instance.checkToggleIcons();
                }
            };
            SetGodPowerSprite(ref power, IconPath);
            AssetManager.powers.add(power);
            if (!PlayerConfig.dict.ContainsKey(ID))
            {
                PlayerConfig.dict.Add(ID, new PlayerOptionData(ID));
            }
            var Button = PowerButtonCreator.CreateToggleButton(
                ID,
                SafeLoadSprite(IconPath),
                null,
                default,
                true
            );
            AssetManager.options_library.add(new OptionAsset()
            {
                id = ID
            });
            PowerButtonCreator.AddButtonToTab(Button, Tab);
            // PlayerConfig.dict.Add() sets boolVal=false by default.
            // Set to match the Enabled parameter passed in — without this,
            // 'Enabled=true' phases came up disabled after every game launch
            // because PlayerConfig.dict shadowed SavedSettings (this is the
            // 'bridge POST after each launch' workaround we documented at
            // docs/journeys/scratch/all-phases-enabled-state.md).
            PlayerConfig.dict[ID].boolVal = Enabled;
            // Mirror into SavedSettings via reflection so phase code agrees.
            try
            {
                var field = typeof(SavedSettings).GetField(ID);
                if (field != null && field.FieldType == typeof(bool) && Core.savedSettings != null)
                {
                    field.SetValue(Core.savedSettings, Enabled);
                }
            }
            catch { }
            PowerButtonSelector.instance.checkToggleIcons();
        }
      }
        #endregion
    static class WindowManager
    {
        public static Dictionary<string, PowerWindow> windows = new Dictionary<string, PowerWindow>();
        public static void CreateWindow(string id, string title, List<ButtonData> Buttons)
        {
            ScrollWindow window;
            GameObject content;
            window = WindowCreator.CreateEmptyWindow(id, title);

            GameObject scrollView = GameObject.Find($"/Canvas Container Main/Canvas - Windows/windows/{window.name}/Background/Scroll View");
            content = GameObject.Find($"/Canvas Container Main/Canvas - Windows/windows/{window.name}/Background/Scroll View/Viewport/Content");
            if (scrollView == null || content == null)
            {
                global::UnityEngine.Debug.LogWarning($"[WSM3D] WindowManager: failed to create window {id}; scroll/content path missing");
                return;
            }
            var powerWindow = scrollView.AddComponent<PowerWindow>();
            windows.Add(id, powerWindow);
            powerWindow.init(id, content, Buttons);
            scrollView.gameObject.SetActive(true);
        }
        public static void OpenWindow(string ID)
        {
            windows[ID].openWindow();
        }
    }
    class PowerWindow : MonoBehaviour
    {
        public GameObject Object;
        string ID;
        public void init(string id, GameObject content, List<ButtonData> Buttons)
        {
            ID = id;
            Object = content;
            if (Object == null)
            {
                global::UnityEngine.Debug.LogWarning("[WSM3D] PowerWindow.init: content GameObject is null/destroyed for id=" + id + " — skipping layout setup");
                return;
            }
            VerticalLayoutGroup layoutGroup = Object.AddComponent<VerticalLayoutGroup>();
            if (layoutGroup == null)
            {
                global::UnityEngine.Debug.LogWarning("[WSM3D] PowerWindow.init: AddComponent<VerticalLayoutGroup> returned null for id=" + id);
                return;
            }
            layoutGroup.childControlHeight = false;
            layoutGroup.childControlWidth = false;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childForceExpandWidth = false;
            layoutGroup.childScaleHeight = true;
            layoutGroup.childScaleWidth = true;
            layoutGroup.childAlignment = TextAnchor.UpperCenter;
            layoutGroup.spacing = 50;
            if (ID == "3D Phases")
            {
                WorldSphereTab.PreloadPhaseIcons();
                AddQuickStartGuide(Object);
            }
            LoadInputOptions(Buttons);
        }
        public void openWindow()
        {
            Windows.ShowWindow(ID);
        }
        static void toggleOption(string pPower)
        {
            GodPower godPower2 = AssetManager.powers.get(pPower);
            WorldTip.instance.showToolbarText(godPower2);
            if (!PlayerConfig.dict.TryGetValue(godPower2.toggle_name, out var value2))
            {
                value2 = new PlayerOptionData(godPower2.toggle_name)
                {
                    boolVal = false
                };
                PlayerConfig.instance.data.add(value2);
            }

            value2.boolVal = true;
            if (value2.boolVal && godPower2.map_modes_switch)
            {
                PowerLibrary.disableAllOtherMapModes(pPower);
            }

            PlayerConfig.saveData();
        }
        private void LoadInputOptions(List<ButtonData> Buttons)
        {
            Object.GetComponent<RectTransform>().sizeDelta += new Vector2(0, Buttons.Count * 125);
            foreach (var data in Buttons)
            {
                GodPower power = new GodPower()
                {
                    id = data.Name,
                    name = data.Name,
                    toggle_name = data.Name,
                    toggle_action = data.Action
                };
                WorldSphereTab.SetGodPowerSprite(ref power, data.IconPath);
                AssetManager.powers.add(power);
                if (!data.CanBeFalse)
                {
                    power.toggle_action = (PowerToggleAction)System.Delegate.Combine(power.toggle_action, new PowerToggleAction(toggleOption));
                }
                if (!PlayerConfig.dict.ContainsKey(data.Name))
                {
                    PlayerConfig.dict.Add(data.Name, new PlayerOptionData(data.Name));
                }
                AssetManager.options_library.add(new OptionAsset()
                {
                    id = data.Name
                });
                PowerButton activeButton = PowerButtonCreator.CreateToggleButton(
                    $"{data.Name}",
                    WorldSphereTab.SafeLoadSprite(data.IconPath),
                    Object.transform,
                    default,
                    !data.CanBeFalse
                );
                PlayerConfig.dict[data.Name].boolVal = data.IsActive;
                activeButton.gameObject.GetComponent<RectTransform>().sizeDelta = new Vector2(64, 64);
                if (ID == "3D Phases")
                {
                    AddPhaseIconAndLabel(activeButton.gameObject, data.Name);
                }
            }
            PowerButtonSelector.instance.checkToggleIcons();
        }

        static void AddQuickStartGuide(GameObject content)
        {
            if (content == null) return;

            string guideText = LM.Get("wsm3d_quick_start_guide");
            if (string.IsNullOrEmpty(guideText) || guideText == "wsm3d_quick_start_guide")
            {
                guideText =
                    "--- Quick Start ---\n" +
                    "Voxel Actors (P1): 3D voxel units, items, projectiles\n" +
                    "Mesh Buildings (P2): Procedural 3D buildings\n" +
                    "Foliage (P3): Crossed-quad trees and bushes\n" +
                    "Mesh Water (P4): Gerstner-wave water surface\n" +
                    "Sun + Shadows (P5): Directional light + cascades\n" +
                    "Skeletal Anim (P6): Auto-rigged voxel actors\n" +
                    "Worldspace UI (P7): 3D nameplates and HP bars\n" +
                    "Day/Night (P8): Procedural sky + time cycle\n" +
                    "Post FX (P9): Bloom, SSAO, SSGI, color grading\n" +
                    "---\n" +
                    "Enable phases top-to-bottom for best results.\n" +
                    "Reload the world after toggling.";
            }

            GameObject guideGo = new GameObject("QuickStartGuide", typeof(RectTransform));
            guideGo.transform.SetParent(content.transform, false);
            guideGo.transform.SetAsFirstSibling();

            RectTransform guideRect = guideGo.GetComponent<RectTransform>();
            guideRect.sizeDelta = new Vector2(200, 220);

            GameObject textRef = GameObject.Find("/Canvas Container Main/Canvas - Windows/windows/3D Phases/Background/Title");
            if (textRef != null)
            {
                GameObject textGo = UnityEngine.Object.Instantiate(textRef, guideGo.transform);
                textGo.SetActive(true);

                var textComp = textGo.GetComponent<Text>();
                if (textComp != null)
                {
                    textComp.text = guideText;
                    textComp.fontSize = 9;
                    textComp.resizeTextMaxSize = 9;
                    textComp.alignment = TextAnchor.UpperLeft;
                }

                var textRt = textGo.GetComponent<RectTransform>();
                if (textRt != null)
                {
                    textRt.anchorMin = new Vector2(0, 0);
                    textRt.anchorMax = new Vector2(1, 1);
                    textRt.offsetMin = Vector2.zero;
                    textRt.offsetMax = Vector2.zero;
                    textRt.localPosition = Vector3.zero;
                    textRt.sizeDelta = new Vector2(200, 220);
                }
            }

            // Expand the content area to accommodate the guide.
            content.GetComponent<RectTransform>().sizeDelta += new Vector2(0, 230);
        }

        static void AddPhaseIconAndLabel(GameObject parent, string phaseId)
        {
            string iconName = GetPhaseIconName(phaseId);
            Sprite? icon = string.IsNullOrEmpty(iconName) ? null : WorldSphereTab.GetPhaseIcon(iconName);
            if (icon != null)
            {
                GameObject iconGo = new GameObject("PhaseIcon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(parent.transform, false);

                RectTransform iconRect = iconGo.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = new Vector2(-18f, -40f);
                iconRect.sizeDelta = new Vector2(16f, 16f);

                Image iconImage = iconGo.GetComponent<Image>();
                iconImage.sprite = icon;
                iconImage.raycastTarget = false;
            }

            WorldSphereTab.addText("3D Phases", LM.Get(phaseId), parent, 10, new Vector3(0, -40, 0), new Vector2(28, 24));
        }

        static string GetPhaseIconName(string phaseId)
        {
            switch (phaseId)
            {
                case "crossed_quad_foliage": return "CrossedQuadFoliage";
                case "day_night_cycle": return "DayNightCycle";
                case "hdr_skybox": return "HdrSkybox";
                case "high_shadows": return "HighShadows";
                case "mesh_water": return "MeshWater";
                case "procedural_buildings": return "ProceduralBuildings";
                case "skeletal_animation": return "SkeletalAnimation";
                case "ssgi_enabled": return "SSGIEnabled";
                case "bloom_enabled": return "BloomEnabled";
                case "aces_tonemapping": return "ACESTonemapping";
                case "voxel_entities": return "VoxelEntities";
                case "worldspace_ui": return "WorldspaceUI";
                default: return string.Empty;
            }
        }

        static bool TryLoadPngViaReflection(Texture2D tex, byte[] bytes)
        {
            try
            {
                var miInstance = typeof(Texture2D).GetMethod(
                    "LoadImage",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new System.Type[] { typeof(byte[]) },
                    null);
                if (miInstance != null)
                {
                    object result = miInstance.Invoke(tex, new object[] { bytes });
                    if (result is bool b1)
                    {
                        return b1;
                    }
                    return true;
                }

                var icType = typeof(Texture2D).Assembly.GetType("UnityEngine.ImageConversion");
                if (icType != null)
                {
                    var miStatic = icType.GetMethod(
                        "LoadImage",
                        BindingFlags.Static | BindingFlags.Public,
                        null,
                        new System.Type[] { typeof(Texture2D), typeof(byte[]) },
                        null);
                    if (miStatic != null)
                    {
                        object result = miStatic.Invoke(null, new object[] { tex, bytes });
                        if (result is bool b2)
                        {
                            return b2;
                        }
                        return true;
                    }
                }
            }
            catch (System.Exception ex)
            {
                global::UnityEngine.Debug.LogWarning($"[WSM3D] TryLoadPngViaReflection threw: {ex.GetType().Name}: {ex.Message}");
            }

            return false;
        }
    }
}



