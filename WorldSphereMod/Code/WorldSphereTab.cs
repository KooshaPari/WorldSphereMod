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
        const string SettingsWindowId = "wsm_settings_window";
        const string SettingsWindowTitle = "wsm_settings";
        static readonly Dictionary<string, Sprite?> IconCache = new Dictionary<string, Sprite?>();
        static readonly Dictionary<string, Sprite?> PhaseIconCache = new Dictionary<string, Sprite?>();
        static GameObject Space;
        static GameObject Line;
        static readonly string[] VoxelInflationStyleOptions =
        {
            "pertexel",
            "greedy",
            "extruded",
            "balloon",
            "organicblob",
            "lathe",
            "auto"
        };
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
            textComp.raycastTarget = false;
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
            WindowManager.CreateWindow(SettingsWindowId, SettingsWindowTitle, new List<ButtonData>());
            CreateButton("WSM Settings", "WorldSphereMod/ModIcon", OpenSettingsWindow);

            void SetVoxelScale(float value)
            {
                Core.savedSettings.VoxelScaleMultiplier = value;
                Core.SaveSettings();
                WorldSphereMod.Voxel.VoxelMeshCache.Clear();
                WorldSphereMod.Voxel.VoxelRender.Reset();
            }

            void SetActorScale(float value)
            {
                Core.savedSettings.ActorVoxelScaleFactor = value;
                Core.SaveSettings();
                WorldSphereMod.Voxel.VoxelMeshCache.Clear();
                WorldSphereMod.Voxel.VoxelRender.Reset();
            }

            void SetBuildingSize(float value)
            {
                Core.savedSettings.BuildingSize = value;
                Core.SaveSettings();
                WorldSphereMod.Voxel.VoxelMeshCache.Clear();
                WorldSphereMod.Voxel.VoxelRender.Reset();
            }

            void SetLODScale(float value)
            {
                Core.savedSettings.LODScale = value;
                Core.SaveSettings();
                WorldSphereMod.LOD.LodSelector.ResetHysteresis();
            }

            void SetTileHeight(float value)
            {
                Core.savedSettings.TileHeight = value;
                Core.SaveSettings();
                Core.Sphere.HeightMult = Mathf.Max(value, 1f);
            }

            void SetLuminance(float value)
            {
                Core.savedSettings.VoxelNeutralLuminance = value;
                Core.SaveSettings();
            }

            void SetShadowRecession(float value)
            {
                Core.savedSettings.VoxelShadowRecession = value;
                Core.SaveSettings();
            }

            void SetSpriteDepth(float value)
            {
                int rounded = Mathf.RoundToInt(value);
                Core.savedSettings.VoxelSpriteDepth = rounded;
                Core.SaveSettings();
                WorldSphereMod.Voxel.VoxelMeshCache.Clear();
                WorldSphereMod.Voxel.VoxelRender.Reset();
            }

            void SetVoxelThreshold(float value)
            {
                WorldSphereMod.LOD.LodSelector.VoxelThreshold = value;
                WorldSphereMod.LOD.LodSelector.ResetHysteresis();
                Core.SaveSettings();
            }

            void SetSmoothingIterations(float value)
            {
                Core.savedSettings.SmoothingIterations = Mathf.Max(0, Mathf.RoundToInt(value));
                Core.SaveSettings();
            }

            void SetPhaseFlag(string flag, bool value)
            {
                var field = typeof(SavedSettings).GetField(flag);
                if (field != null && field.FieldType == typeof(bool))
                {
                    field.SetValue(Core.savedSettings, value);
                }
                Core.SaveSettings();
                Core.ApplyPhaseToggle(flag, value);
            }

            void SetDebugFlag(string flag, bool value)
            {
                var field = typeof(SavedSettings).GetField(flag);
                if (field != null && field.FieldType == typeof(bool))
                {
                    field.SetValue(Core.savedSettings, value);
                }
                Core.SaveSettings();
            }

            void SetInflationStyle(string value)
            {
                Core.savedSettings.VoxelInflationStyle = value;
                Core.SaveSettings();
                WorldSphereMod.Voxel.VoxelMeshCache.Clear();
                WorldSphereMod.Voxel.VoxelRender.Reset();
            }

            WindowManager.windows[SettingsWindowId].Object.GetComponent<RectTransform>().sizeDelta =
                new Vector2(520, 360);

            CreateSettingsPanel(
                WindowManager.windows[SettingsWindowId].Object,
                SetVoxelScale,
                SetActorScale,
                SetBuildingSize,
                SetLODScale,
                SetVoxelThreshold,
                SetSpriteDepth,
                SetTileHeight,
                SetPhaseFlag,
                SetSmoothingIterations,
                SetLuminance,
                SetShadowRecession,
                SetInflationStyle,
                SetDebugFlag
            );
        }

        static Font GetDefaultFont()
        {
            Text sample = Object.FindObjectOfType<Text>();
            return sample != null && sample.font != null
                ? sample.font
                : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        static void OpenSettingsWindow()
        {
            WindowManager.windows[SettingsWindowId].openWindow();
        }

        static float FormatNumeric(float v, float step)
        {
            if (step >= 1f) return Mathf.Round(v);
            if (step >= 0.1f) return Mathf.Round(v * 10f) / 10f;
            return Mathf.Round(v * 100f) / 100f;
        }

        static void AddSectionHeader(string title, GameObject parent, int width)
        {
            GameObject textGo = new GameObject("SectionHeader", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(parent.transform, false);
            Text header = textGo.GetComponent<Text>();
            header.text = title;
            header.font = GetDefaultFont();
            header.fontSize = 14;
            header.resizeTextMaxSize = 14;
            header.color = Color.white;
            header.alignment = TextAnchor.MiddleLeft;
            header.fontStyle = FontStyle.Bold;
            header.raycastTarget = false;
            AddLayoutElement(textGo, width, width, 0f);
        }

        static GameObject AddRow(GameObject parent)
        {
            var row = new GameObject("SettingsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent.transform, false);
            var rt = row.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 30);

            HorizontalLayoutGroup group = row.GetComponent<HorizontalLayoutGroup>();
            group.childAlignment = TextAnchor.MiddleLeft;
            group.spacing = 10f;
            group.childControlWidth = false;
            group.childControlHeight = false;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            group.padding = new RectOffset(2, 2, 2, 2);
            return row;
        }

        static void AddLayoutElement(GameObject go, float minWidth, float preferredWidth = 0f, float flexibleWidth = 1f)
        {
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minWidth = minWidth;
            le.preferredWidth = preferredWidth > 0f ? preferredWidth : minWidth;
            le.flexibleWidth = flexibleWidth;
            le.minHeight = 24;
            le.preferredHeight = 24;
        }

        static Text MakeText(string text, int fontSize, GameObject parent)
        {
            GameObject textGo = new GameObject("Text", typeof(Text));
            textGo.transform.SetParent(parent.transform, false);
            Text textComp = textGo.GetComponent<Text>();
            textComp.text = text;
            textComp.font = GetDefaultFont();
            textComp.fontSize = fontSize;
            textComp.resizeTextMaxSize = fontSize;
            textComp.alignment = TextAnchor.MiddleLeft;
            textComp.color = Color.white;
            textComp.raycastTarget = false;
            return textComp;
        }

        static void AddLabelledToggle(string label, bool value, UnityAction<bool> onValueChanged, GameObject row, float width)
        {
            GameObject holder = new GameObject("ToggleCell", typeof(RectTransform));
            holder.transform.SetParent(row.transform, false);
            AddLayoutElement(holder, width, width, 0.75f);

            var layout = holder.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;

            Text labelText = MakeText(label, 10, holder);
            AddLayoutElement(labelText.gameObject, width * 0.58f);

            GameObject toggleGo = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle), typeof(Image));
            toggleGo.transform.SetParent(holder.transform, false);
            Image toggleBg = toggleGo.GetComponent<Image>();
            toggleBg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            Toggle toggle = toggleGo.GetComponent<Toggle>();

            GameObject checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkGo.transform.SetParent(toggleGo.transform, false);
            Image checkImage = checkGo.GetComponent<Image>();
            checkImage.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            toggle.graphic = checkImage;
            toggle.targetGraphic = toggleBg;
            toggle.isOn = value;
            toggle.onValueChanged.AddListener(onValueChanged);
        }

        static void AddLabelledSlider(string label, float min, float max, float step, float value, GameObject row, UnityAction<float> onValueChanged, float width, bool integer = false)
        {
            GameObject holder = new GameObject("SliderCell", typeof(RectTransform));
            holder.transform.SetParent(row.transform, false);
            AddLayoutElement(holder, width, width, 1f);

            var layout = holder.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 5f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            Text labelText = MakeText($"{label}: {FormatNumeric(value, step)}", 10, holder);
            AddLayoutElement(labelText.gameObject, 120, 120, 0.2f);

            GameObject sliderGO = new GameObject("Slider", typeof(RectTransform), typeof(Slider), typeof(Image));
            sliderGO.transform.SetParent(holder.transform, false);
            AddLayoutElement(sliderGO, width - 132, width - 132, 1f);
            Slider slider = sliderGO.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.onValueChanged.AddListener((float v) =>
            {
                if (integer) v = Mathf.Round(v);
                labelText.text = $"{label}: {FormatNumeric(v, step)}";
                onValueChanged(v);
            });

            GameObject track = new GameObject("Track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(sliderGO.transform, false);
            Image trackImage = track.GetComponent<Image>();
            trackImage.color = Color.gray;

            GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(sliderGO.transform, false);
            RectTransform handleAreaRt = handleArea.GetComponent<RectTransform>();
            handleAreaRt.sizeDelta = new Vector2(100, 16);

            GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(handleArea.transform, false);
            RectTransform handleRt = handle.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(14, 14);
            Image handleImage = handle.GetComponent<Image>();
            handleImage.color = Color.white;

            slider.targetGraphic = handleImage;
            slider.handleRect = handleRt;
        }

        static void AddStyleSwitcher(string label, string value, GameObject row, string[] options, UnityAction<string> onValueChanged, float width)
        {
            GameObject holder = new GameObject("StyleCell", typeof(RectTransform));
            holder.transform.SetParent(row.transform, false);
            AddLayoutElement(holder, width, width, 1f);

            var layout = holder.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            Text labelText = MakeText(label, 10, holder);
            AddLayoutElement(labelText.gameObject, 120, 120, 0.3f);

            GameObject valueGo = new GameObject("Value", typeof(RectTransform), typeof(Text));
            valueGo.transform.SetParent(holder.transform, false);
            Text valueText = valueGo.GetComponent<Text>();
            valueText.text = value;
            valueText.font = GetDefaultFont();
            valueText.fontSize = 10;
            valueText.resizeTextMaxSize = 10;
            valueText.alignment = TextAnchor.MiddleCenter;
            valueText.color = Color.white;
            AddLayoutElement(valueGo, 84, 84, 0.5f);

            void Step(int delta)
            {
                int currentIdx = System.Array.IndexOf(options, valueText.text);
                if (currentIdx < 0) currentIdx = 0;
                int next = (currentIdx + delta) % options.Length;
                if (next < 0) next = options.Length - 1;
                valueText.text = options[next];
                onValueChanged(valueText.text);
            }

            GameObject prevButton = CreateMiniButton("<", holder, () => Step(-1));
            AddLayoutElement(prevButton, 22, 22, 0.2f);
            GameObject nextButton = CreateMiniButton(">", holder, () => Step(1));
            AddLayoutElement(nextButton, 22, 22, 0.2f);
        }

        static GameObject CreateMiniButton(string text, GameObject parent, UnityAction action)
        {
            GameObject buttonGo = new GameObject("MiniButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(parent.transform, false);
            Image bg = buttonGo.GetComponent<Image>();
            bg.color = new Color(0.25f, 0.25f, 0.25f, 1f);
            Button button = buttonGo.GetComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(action);

            Text textComponent = MakeText(text, 10, buttonGo);
            textComponent.alignment = TextAnchor.MiddleCenter;
            AddLayoutElement(textComponent.gameObject, 0, 0, 1f);
            return buttonGo;
        }

        static void CreateSettingsPanel(
            GameObject windowContent,
            UnityAction<float> setVoxelScale,
            UnityAction<float> setActorScale,
            UnityAction<float> setBuildingSize,
            UnityAction<float> setLodScale,
            UnityAction<float> setVoxelThreshold,
            UnityAction<float> setSpriteDepth,
            UnityAction<float> setTileHeight,
            UnityAction<string, bool> setPhaseFlag,
            UnityAction<float> setSmoothingIterations,
            UnityAction<float> setNeutralLuminance,
            UnityAction<float> setShadowRecession,
            UnityAction<string> setInflationStyle,
            UnityAction<string, bool> setDebugFlag
        )
        {
            GameObject panel = new GameObject("SettingsPanel", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(windowContent.transform, false);
            VerticalLayoutGroup panelLayout = panel.GetComponent<VerticalLayoutGroup>();
            panelLayout.spacing = 10f;
            panelLayout.childForceExpandHeight = false;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childAlignment = TextAnchor.UpperLeft;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = false;

            ContentSizeFitter fitter = panel.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            AddSectionHeader("RENDER", panel, 500);

            GameObject row = AddRow(panel);
            AddLabelledSlider("Voxel Scale", 0.5f, 16f, 0.1f, Core.savedSettings.VoxelScaleMultiplier, row, setVoxelScale, 240);
            AddLabelledSlider("Actor Scale", 0.01f, 1f, 0.01f, Core.savedSettings.ActorVoxelScaleFactor, row, setActorScale, 240);
            AddLabelledSlider("Building Size", 0.1f, 5f, 0.05f, Core.savedSettings.BuildingSize, row, setBuildingSize, 240);

            row = AddRow(panel);
            AddLabelledSlider("LOD Scale", 0.1f, 2.5f, 0.05f, Core.savedSettings.LODScale, row, setLodScale, 240);
            AddLabelledSlider("Voxel Threshold", 0.002f, 0.2f, 0.001f, WorldSphereMod.LOD.LodSelector.VoxelThreshold, row, setVoxelThreshold, 240);
            AddLabelledSlider("Voxel Depth", 1f, 32f, 1f, Core.savedSettings.VoxelSpriteDepth, row, setSpriteDepth, 240, true);

            AddSectionHeader("TERRAIN", panel, 500);
            row = AddRow(panel);
            AddLabelledSlider("Tile Height", 1f, 10f, 0.1f, Core.savedSettings.TileHeight, row, setTileHeight, 240);
            AddLabelledToggle("Heightfield", Core.savedSettings.UseHeightFieldTerrain, (v) =>
            {
                Core.savedSettings.UseHeightFieldTerrain = v;
                Core.SaveSettings();
            }, row, 240);
            AddLabelledToggle("Voxel Smoothing", Core.savedSettings.VoxelMeshSmoothing, (v) =>
            {
                Core.savedSettings.VoxelMeshSmoothing = v;
                Core.SaveSettings();
            }, row, 240);

            row = AddRow(panel);
            AddLabelledSlider("Smoothing Iter", 0f, 12f, 1f, Core.savedSettings.SmoothingIterations, row, setSmoothingIterations, 240);

            AddSectionHeader("EFFECTS", panel, 500);
            row = AddRow(panel);
            AddLabelledToggle("Voxel Tonemap", Core.savedSettings.VoxelColorTonemap, (v) =>
            {
                Core.savedSettings.VoxelColorTonemap = v;
                Core.SaveSettings();
            }, row, 240);
            AddLabelledToggle("Luminance Depth", Core.savedSettings.VoxelLuminanceDepth, (v) =>
            {
                Core.savedSettings.VoxelLuminanceDepth = v;
                Core.SaveSettings();
            }, row, 240);
            AddLabelledSlider("Neutral Luma", 0f, 1f, 0.01f, Core.savedSettings.VoxelNeutralLuminance, row, setNeutralLuminance, 240);

            row = AddRow(panel);
            AddLabelledSlider("Shadow Recession", 0f, 4f, 0.05f, Core.savedSettings.VoxelShadowRecession, row, setShadowRecession, 240);
            AddStyleSwitcher("Voxel Inflation", Core.savedSettings.VoxelInflationStyle, row, VoxelInflationStyleOptions, setInflationStyle, 240);

            AddSectionHeader("FEATURES", panel, 500);
            row = AddRow(panel);
            AddLabelledToggle("Voxel Entities", Core.savedSettings.VoxelEntities, (v) => setPhaseFlag(nameof(SavedSettings.VoxelEntities), v), row, 240);
            AddLabelledToggle("Procedural Buildings", Core.savedSettings.ProceduralBuildings, (v) => setPhaseFlag(nameof(SavedSettings.ProceduralBuildings), v), row, 240);
            AddLabelledToggle("Procgen Style", Core.savedSettings.BuildingStyleProcgen, (v) =>
            {
                Core.savedSettings.BuildingStyleProcgen = v;
                Core.SaveSettings();
            }, row, 240);

            row = AddRow(panel);
            AddLabelledToggle("Fallback Draw Path", Core.savedSettings.ForceFallbackDrawPath, (v) =>
            {
                Core.savedSettings.ForceFallbackDrawPath = v;
                Core.SaveSettings();
                WorldSphereMod.Voxel.MeshInstanceBatcher.SetFallbackPath(v);
            }, row, 240);

            AddSectionHeader("DEBUG", panel, 500);
            row = AddRow(panel);
            AddLabelledToggle("Profiler Dump", Core.savedSettings.ProfilerDump, (v) => setDebugFlag(nameof(SavedSettings.ProfilerDump), v), row, 240);
            AddLabelledToggle("Debug HUD", Core.savedSettings.DebugHUDVisible, (v) => setDebugFlag(nameof(SavedSettings.DebugHUDVisible), v), row, 240);
            AddLabelledToggle("Sanity Cube", Core.savedSettings.DebugSanityCube, (v) => setDebugFlag(nameof(SavedSettings.DebugSanityCube), v), row, 240);

            row = AddRow(panel);
            AddLabelledToggle("Spawn Debug", Core.savedSettings.DebugSpawnBuildings, (v) => setDebugFlag(nameof(SavedSettings.DebugSpawnBuildings), v), row, 240);
            AddLabelledToggle("Voxel Outline", Core.savedSettings.DebugVoxelOutline, (v) => setDebugFlag(nameof(SavedSettings.DebugVoxelOutline), v), row, 240);
            AddLabelledToggle("Diag Overlay", Core.savedSettings.RenderDiagOverlay, (v) => setDebugFlag(nameof(SavedSettings.RenderDiagOverlay), v), row, 240);

            row = AddRow(panel);
            AddLabelledToggle("Render Error Props", Core.savedSettings.RenderErrorProps, (v) => setDebugFlag(nameof(SavedSettings.RenderErrorProps), v), row, 240);
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
            { "cylindrical_shape", 0 },
            { "flat_shape", 1 },
            { "cube_shape", 2 }
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
            }
            PowerButtonSelector.instance.checkToggleIcons();
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
