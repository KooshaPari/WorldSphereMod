using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using NCMS.Utils;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
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
        static GameObject Space;
        static GameObject Line;
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
        }
        static void AddLine()
        {
            Object.Instantiate(Line).transform.SetParent(Tab.transform);
        }

        static void CreateTab()
        {
            ModIcon = Resources.Load<Sprite>("WorldSphereMod/ModIcon");
            Tab = TabManager.CreateTab("WorldSphereMod", "world_sphere_tab", "world_sphere_tab_desc", ModIcon, "world_sphere_tab_author");
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
               new ButtonData("sprites_rotate_to_camera", "sprites_rotate_to_camera_description", "WorldSphereMod/Rotate", Core.savedSettings.RotateStuffToCamera, ToggleRotations)
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
                new ButtonData("cylindrical_shape", "cylindrical_shape_description", "WorldSphereMod/Round", Core.savedSettings.CurrentShape == 0, SetShape, false),
                new ButtonData("flat_shape", "flat_shape_description", "WorldSphereMod/Flat", Core.savedSettings.CurrentShape == 1, SetShape, false),
                new ButtonData("perlin_noise", "perlin_noise_description", "WorldSphereMod/PerlinNoise", Core.savedSettings.PerlinNoise, PerlinNoise)
            });
            GenerateSlider("tile_length_multiplier", 1, 10, Core.savedSettings.TileHeight, (float x) => { Core.savedSettings.TileHeight = x; Core.SaveSettings(); }, "World Settings");

            // v2 fork: per-phase toggles. The default values come from
            // SavedSettings; the toggle action flips + persists. Without
            // surfacing these here the user has no way to turn Phase 1's
            // voxel actors on, so sprites stay 2D and the fork looks like
            // a no-op compared to upstream.
            CreateWindowButton("3D Phases", "WorldSphereMod/ModIcon", "phases_window", new List<ButtonData>()
            {
                new ButtonData("voxel_entities",       "voxel_entities_description",       "WorldSphereMod/Round",        Core.savedSettings.VoxelEntities,       TogglePhase_VoxelEntities),
                new ButtonData("procedural_buildings", "procedural_buildings_description", "WorldSphereMod/World",         Core.savedSettings.ProceduralBuildings, TogglePhase_ProceduralBuildings),
                new ButtonData("crossed_quad_foliage", "crossed_quad_foliage_description", "WorldSphereMod/Flat",          Core.savedSettings.CrossedQuadFoliage, TogglePhase_CrossedQuadFoliage),
                new ButtonData("mesh_water",           "mesh_water_description",           "WorldSphereMod/PerlinNoise",   Core.savedSettings.MeshWater,           TogglePhase_MeshWater),
                new ButtonData("high_shadows",         "high_shadows_description",         "WorldSphereMod/SkyBox",        Core.savedSettings.HighShadows,         TogglePhase_HighShadows),
                new ButtonData("skeletal_animation",   "skeletal_animation_description",   "WorldSphereMod/Rotate",        Core.savedSettings.SkeletalAnimation,   TogglePhase_SkeletalAnimation),
                new ButtonData("worldspace_ui",        "worldspace_ui_description",        "WorldSphereMod/Camera",        Core.savedSettings.WorldspaceUI,        TogglePhase_WorldspaceUI),
                new ButtonData("day_night_cycle",      "day_night_cycle_description",      "WorldSphereMod/SkyBox",        Core.savedSettings.DayNightCycle,       TogglePhase_DayNightCycle),
                new ButtonData("post_fx",              "post_fx_description",              "WorldSphereMod/ModIcon",       Core.savedSettings.PostFX,              TogglePhase_PostFX),
                new ButtonData("particle_effects",     "particle_effects_description",     "WorldSphereMod/Logo",          Core.savedSettings.ParticleEffects,     TogglePhase_ParticleEffects),
            });

            CreateButton("Open Sprites", "WorldSphereMod/ModIcon", OpenSprites);

            // Phase 10 / R&D QoL: ProfilerDump toggle (also drives the in-game
            // RuntimeStatsOverlay since the overlay's OnGUI gates on the same
            // flag) and a destructive Reset-to-defaults action.
            CreateToggleButton("ProfileMode", "WorldSphereMod/ModIcon", "profile_mode", "profile_mode_description", ToggleProfileMode, Core.savedSettings.ProfilerDump);
            CreateButton("Reset Defaults", "WorldSphereMod/ModIcon", ResetToDefaults);
        }

        static void TogglePhase_VoxelEntities(string _)       { Core.savedSettings.VoxelEntities       = !Core.savedSettings.VoxelEntities;       Core.SaveSettings(); PhasePatchManager.ApplyPhaseToggle(nameof(SavedSettings.VoxelEntities), Core.savedSettings.VoxelEntities); }
        static void TogglePhase_ProceduralBuildings(string _) { Core.savedSettings.ProceduralBuildings = !Core.savedSettings.ProceduralBuildings; Core.SaveSettings(); PhasePatchManager.ApplyPhaseToggle(nameof(SavedSettings.ProceduralBuildings), Core.savedSettings.ProceduralBuildings); }
        static void TogglePhase_CrossedQuadFoliage(string _)  { Core.savedSettings.CrossedQuadFoliage  = !Core.savedSettings.CrossedQuadFoliage;  Core.SaveSettings(); PhasePatchManager.ApplyPhaseToggle(nameof(SavedSettings.CrossedQuadFoliage), Core.savedSettings.CrossedQuadFoliage); }
        static void TogglePhase_MeshWater(string _)           { Core.savedSettings.MeshWater           = !Core.savedSettings.MeshWater;           Core.SaveSettings(); PhasePatchManager.ApplyPhaseToggle(nameof(SavedSettings.MeshWater), Core.savedSettings.MeshWater); }
        static void TogglePhase_HighShadows(string _)         { Core.savedSettings.HighShadows         = !Core.savedSettings.HighShadows;         Core.SaveSettings(); PhasePatchManager.ApplyPhaseToggle(nameof(SavedSettings.HighShadows), Core.savedSettings.HighShadows); }
        static void TogglePhase_SkeletalAnimation(string _)   { Core.savedSettings.SkeletalAnimation   = !Core.savedSettings.SkeletalAnimation;   Core.SaveSettings(); PhasePatchManager.ApplyPhaseToggle(nameof(SavedSettings.SkeletalAnimation), Core.savedSettings.SkeletalAnimation); }
        static void TogglePhase_WorldspaceUI(string _)        { Core.savedSettings.WorldspaceUI        = !Core.savedSettings.WorldspaceUI;        Core.SaveSettings(); PhasePatchManager.ApplyPhaseToggle(nameof(SavedSettings.WorldspaceUI), Core.savedSettings.WorldspaceUI); }
        static void TogglePhase_DayNightCycle(string _)       { Core.savedSettings.DayNightCycle       = !Core.savedSettings.DayNightCycle;       Core.SaveSettings(); PhasePatchManager.ApplyPhaseToggle(nameof(SavedSettings.DayNightCycle), Core.savedSettings.DayNightCycle); }
        static void TogglePhase_PostFX(string _)              { Core.savedSettings.PostFX              = !Core.savedSettings.PostFX;              Core.SaveSettings(); PhasePatchManager.ApplyPhaseToggle(nameof(SavedSettings.PostFX), Core.savedSettings.PostFX); }
        static void TogglePhase_ParticleEffects(string _)     { Core.savedSettings.ParticleEffects     = !Core.savedSettings.ParticleEffects;     Core.SaveSettings(); PhasePatchManager.ApplyPhaseToggle(nameof(SavedSettings.ParticleEffects), Core.savedSettings.ParticleEffects); }
        static void ToggleProfileMode()
        {
            Core.savedSettings.ProfilerDump = !Core.savedSettings.ProfilerDump;
            Core.SaveSettings();
        }
        static void ResetToDefaults()
        {
            Core.savedSettings = new SavedSettings();
            Core.SaveSettings();
            foreach (FieldInfo field in typeof(SavedSettings).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.FieldType == typeof(bool))
                {
                    PhasePatchManager.ApplyPhaseToggle(field.Name, (bool)field.GetValue(Core.savedSettings));
                }
            }
            UnityEngine.Debug.Log("[WSM3D] SavedSettings reset to defaults. Restart recommended for full effect.");
        }
        static void OpenSprites()
        {
            Application.OpenURL("file://" + Mod.ModDirectory + "/GameResources/WorldSphereMod");
        }
        static Dictionary<string, int> WorldShapes = new Dictionary<string, int>()
        {
            { "cylindrical_shape", 0 },
            { "flat_shape", 1 }
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
            PowerButton button = PowerButtonCreator.CreateSimpleButton(ID, Action, Resources.Load<Sprite>(IconPath));
            PowerButtonCreator.AddButtonToTab(button, Tab);
        }
        static void CreateToggleButton(string ID, string IconPath, string name, string Description, UnityAction toggleAction, bool Enabled)
        {
            GodPower power = AssetManager.powers.add(new GodPower()
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
            });
            PlayerConfig.dict.Add(ID, new PlayerOptionData(ID));
            var Button = PowerButtonCreator.CreateToggleButton(
                ID,
                Resources.Load<Sprite>(IconPath),
                null,
                default,
                true
            );
            AssetManager.options_library.add(new OptionAsset()
            {
                id = ID
            });
            PowerButtonCreator.AddButtonToTab(Button, Tab);
            if (!Enabled)
            {
                PlayerConfig.dict[ID].boolVal = false;
            }
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
            if (content != null)
            {
                windows.Add(id, scrollView.AddComponent<PowerWindow>());
                scrollView.GetComponent<PowerWindow>().init(id, content, Buttons);
                scrollView.gameObject.SetActive(true);
            }
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
            VerticalLayoutGroup layoutGroup = Object.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childControlHeight = false;
            layoutGroup.childControlWidth = false;
            layoutGroup.childForceExpandHeight = false;
            layoutGroup.childForceExpandWidth = false;
            layoutGroup.childScaleHeight = true;
            layoutGroup.childScaleWidth = true;
            layoutGroup.childAlignment = TextAnchor.UpperCenter;
            layoutGroup.spacing = 50;
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
                GodPower power = AssetManager.powers.add(new GodPower()
                {
                    id = data.Name,
                    name = data.Name,
                    toggle_name = data.Name,
                    toggle_action = data.Action
                });
                if (!data.CanBeFalse)
                {
                    power.toggle_action = (PowerToggleAction)System.Delegate.Combine(power.toggle_action, new PowerToggleAction(toggleOption));
                }
                PlayerConfig.dict.Add(data.Name, new PlayerOptionData(data.Name));
                AssetManager.options_library.add(new OptionAsset()
                {
                    id = data.Name
                });
                PowerButton activeButton = PowerButtonCreator.CreateToggleButton(
                    $"{data.Name}",
                    Resources.Load<Sprite>(data.IconPath),
                    Object.transform,
                    default,
                    !data.CanBeFalse
                );
                PlayerConfig.dict[data.Name].boolVal = data.IsActive;
                activeButton.gameObject.GetComponent<RectTransform>().sizeDelta = new Vector2(64, 64);
                if (ID == "phases_window")
                {
                    WorldSphereTab.addText(ID, LM.Get(data.Name), activeButton.gameObject, 10, new Vector3(0, -40, 0), new Vector2(28, 24));
                }
            }
            PowerButtonSelector.instance.checkToggleIcons();
        }
    }
}
