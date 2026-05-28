# UI Extension & Skinning API Specification

**Status:** Draft
**Author:** WorldSphereMod3D contributors
**Target:** NeoModLoader mods targeting WorldBox (Unity 2022.3, Built-in RP)

---

## 1. Problem statement

Every NeoModLoader mod that ships UI today must:

1. Manually instantiate Unity GameObjects, add `VerticalLayoutGroup`,
   wire `Slider`/`Toggle`/`Button` components, and manage RectTransform
   math by hand (see `WorldSphereTab.cs` lines 141-183).
2. Hard-code calls to `WindowCreator.CreateEmptyWindow`, `WindowManager`,
   `PowerButtonCreator`, `TabManager` -- NML internals that change between
   versions and have no validation.
3. Re-implement sprite caching, reflection-based `LoadImage`, and
   `PlayerConfig.dict` bridging for every toggle.

The result is 900+ lines in `WorldSphereTab.cs` of procedural UI code
that is fragile, untestable, and impossible for downstream mods to extend.

This spec defines a declarative UI extension API that:

- Lets mods register settings panels, toolbar buttons, overlay HUDs,
  and modal dialogs through attributes and a builder pattern.
- Manages the Unity UGUI backend internally so mods never touch
  `GameObject`, `RectTransform`, or `VerticalLayoutGroup`.
- Integrates with NeoModLoader's `IMod` / `IStagedLoad` lifecycle.
- Supports themeable colors, fonts, and spacing.

---

## 2. Prior art

### 2.1 Factorio -- mod settings API + data-driven GUI

Factorio mods declare settings in `data.lua` using typed primitives:

```lua
data:extend({
  { type = "bool-setting",   name = "my-mod-enable-x", default_value = true },
  { type = "double-setting", name = "my-mod-scale",     default_value = 1.0,
    minimum_value = 0.1, maximum_value = 10.0 },
  { type = "string-setting", name = "my-mod-mode",
    allowed_values = {"fast","balanced","quality"}, default_value = "balanced" }
})
```

The game engine renders the settings UI automatically. Mods never create
widgets; they declare intent and the engine owns layout. Rich GUIs use a
separate `LuaGuiElement` tree with `flow`, `frame`, `table` containers
and typed children (`checkbox`, `slider`, `drop-down`, `sprite-button`).

**Takeaway:** Declarative field definitions with validation metadata work
well for settings. Richer panels need a small layout DSL.

### 2.2 RimWorld -- Def-based UI windows

RimWorld's modding layer uses XML `Def` objects to declare UI:

```xml
<ThingDef>
  <defName>MyWidget</defName>
  <label>Widget</label>
  <statBases>
    <MaxHitPoints>100</MaxHitPoints>
  </statBases>
</ThingDef>
```

Custom settings windows inherit `Mod` and override `DoSettingsWindowContents(Rect)`,
using an immediate-mode API (`Widgets.CheckboxLabeled`, `Widgets.HorizontalSlider`).
The `Listing_Standard` helper provides auto-layout (vertical list of labeled
controls with consistent spacing).

**Takeaway:** A `Listing`-style builder that auto-stacks controls vertically
is the minimum viable abstraction for mod settings.

### 2.3 Minecraft (Fabric/Forge) -- Screen/Widget registration

Forge mods register config screens via `ModLoadingContext.registerExtensionPoint`
with a factory `Function<Minecraft, Screen>`. Fabric uses `ModMenuApiImpl` to
return a `ConfigScreenFactory`. Inside, mods build widget trees:

```java
ButtonWidget.builder(Text.literal("Toggle"), btn -> { ... })
    .dimensions(x, y, 200, 20)
    .build();
```

Cloth Config (community library) adds declarative config builders:

```java
ConfigBuilder.create().setTitle(...)
    .getOrCreateCategory(Text.literal("General"))
    .addEntry(entryBuilder.startBooleanToggle(Text.literal("Enable X"), true)
        .setDefaultValue(true)
        .setSaveConsumer(val -> config.enableX = val)
        .build());
```

**Takeaway:** A builder pattern with `.Category()` / `.AddToggle()` /
`.AddSlider()` chains is the community-proven approach for typed settings
without raw widget construction.

---

## 3. API surface

### 3.1 Namespace

```
WorldSphereMod.UI.Extension
```

All types live here. Mods reference `WorldSphereAPI.dll` (netstandard2.0)
which ships the interfaces; the implementation lives in the main mod DLL.

### 3.2 Core interfaces

```csharp
namespace WorldSphereMod.UI.Extension
{
    /// <summary>
    /// Entry point. Obtained via <c>ModUI.ForMod(IMod)</c>.
    /// One instance per mod; created during IStagedLoad.Init or PostInit.
    /// </summary>
    public interface IModUIRegistrar
    {
        /// <summary>Register a settings panel (appears in the mod's tab window).</summary>
        ISettingsPanelBuilder Settings(string panelId, string localeKey);

        /// <summary>Register a toolbar button on the mod's PowersTab.</summary>
        IToolbarButtonBuilder ToolbarButton(string buttonId);

        /// <summary>Register a screen-space overlay HUD.</summary>
        IOverlayBuilder Overlay(string overlayId);

        /// <summary>Show a modal dialog.</summary>
        IModalBuilder Modal(string modalId, string titleLocaleKey);

        /// <summary>Apply a UI theme to all controls registered by this mod.</summary>
        void ApplyTheme(UITheme theme);
    }
}
```

### 3.3 Settings panel builder

```csharp
public interface ISettingsPanelBuilder
{
    /// <summary>Start a named category (rendered as a separator + header).</summary>
    ISettingsPanelBuilder Category(string localeKey);

    /// <summary>Bool toggle bound to a field or lambda.</summary>
    ISettingsPanelBuilder Toggle(string id, string localeKey,
        Func<bool> getter, Action<bool> setter,
        string? iconPath = null, string? tooltipLocaleKey = null);

    /// <summary>Float slider with min/max/step.</summary>
    ISettingsPanelBuilder Slider(string id, string localeKey,
        Func<float> getter, Action<float> setter,
        float min, float max, float step = 0f,
        string? format = "F1", string? tooltipLocaleKey = null);

    /// <summary>Int slider.</summary>
    ISettingsPanelBuilder SliderInt(string id, string localeKey,
        Func<int> getter, Action<int> setter,
        int min, int max,
        string? tooltipLocaleKey = null);

    /// <summary>
    /// Enum dropdown. T must be an enum type. Locale keys are resolved
    /// as "{localeKey}_{enumValueName}" for each option.
    /// </summary>
    ISettingsPanelBuilder Dropdown<T>(string id, string localeKey,
        Func<T> getter, Action<T> setter,
        string? tooltipLocaleKey = null) where T : struct, Enum;

    /// <summary>Text input field.</summary>
    ISettingsPanelBuilder TextInput(string id, string localeKey,
        Func<string> getter, Action<string> setter,
        int maxLength = 256,
        string? tooltipLocaleKey = null);

    /// <summary>Action button (not a setting, just a clickable row).</summary>
    ISettingsPanelBuilder ActionButton(string id, string localeKey,
        Action onClick, string? iconPath = null);

    /// <summary>Horizontal separator line.</summary>
    ISettingsPanelBuilder Separator();

    /// <summary>Finalize and register the panel with the UI system.</summary>
    void Build();
}
```

### 3.4 Toolbar button builder

```csharp
public interface IToolbarButtonBuilder
{
    IToolbarButtonBuilder Label(string localeKey);
    IToolbarButtonBuilder Tooltip(string localeKey);
    IToolbarButtonBuilder Icon(string resourcePath);

    /// <summary>Simple click action.</summary>
    IToolbarButtonBuilder OnClick(Action onClick);

    /// <summary>Toggle button bound to a bool.</summary>
    IToolbarButtonBuilder AsToggle(Func<bool> getter, Action<bool> setter);

    /// <summary>Button opens a registered settings panel.</summary>
    IToolbarButtonBuilder OpensPanel(string panelId);

    void Build();
}
```

### 3.5 Overlay HUD builder

```csharp
public interface IOverlayBuilder
{
    /// <summary>Screen anchor (TopLeft, TopRight, BottomLeft, etc.).</summary>
    IOverlayBuilder Anchor(UIAnchor anchor);

    /// <summary>Pixel offset from anchor.</summary>
    IOverlayBuilder Offset(float x, float y);

    /// <summary>Fixed size; omit for auto-size.</summary>
    IOverlayBuilder Size(float width, float height);

    /// <summary>
    /// Content callback invoked every frame (or on dirty).
    /// Receives an IOverlayCanvas for immediate-mode-style drawing.
    /// </summary>
    IOverlayBuilder OnRender(Action<IOverlayCanvas> render);

    /// <summary>
    /// Predicate controlling visibility. Checked every frame.
    /// </summary>
    IOverlayBuilder VisibleWhen(Func<bool> predicate);

    void Build();
}

public interface IOverlayCanvas
{
    void Label(string text, int fontSize = 14);
    void Label(string text, Color color, int fontSize = 14);
    void ProgressBar(float value01, Color fill, Color bg, float height = 8f);
    void Spacer(float height = 4f);
    void Icon(string resourcePath, float size = 16f);
    void BeginRow();
    void EndRow();
}

public enum UIAnchor
{
    TopLeft, TopCenter, TopRight,
    MiddleLeft, MiddleCenter, MiddleRight,
    BottomLeft, BottomCenter, BottomRight
}
```

### 3.6 Modal dialog builder

```csharp
public interface IModalBuilder
{
    IModalBuilder Message(string localeKey);

    /// <summary>Add a button to the modal footer.</summary>
    IModalBuilder Button(string localeKey, Action onClick,
        ModalButtonStyle style = ModalButtonStyle.Default);

    /// <summary>Add the same typed controls as a settings panel.</summary>
    IModalBuilder Content(Action<ISettingsPanelBuilder> configure);

    /// <summary>Show the modal. Non-blocking; returns immediately.</summary>
    void Show();
}

public enum ModalButtonStyle { Default, Primary, Danger }
```

### 3.7 UI theme

```csharp
public class UITheme
{
    /// <summary>
    /// Panel/window background. Null = use game default.
    /// </summary>
    public Color? BackgroundColor { get; set; }
    public Color? AccentColor { get; set; }
    public Color? TextColor { get; set; }
    public Color? SecondaryTextColor { get; set; }
    public Color? SeparatorColor { get; set; }

    /// <summary>
    /// Font size scale relative to game default (1.0 = unchanged).
    /// </summary>
    public float FontScale { get; set; } = 1.0f;

    /// <summary>Vertical spacing between controls in pixels.</summary>
    public float ItemSpacing { get; set; } = 8f;

    /// <summary>Inner padding for panels/modals.</summary>
    public float Padding { get; set; } = 12f;

    /// <summary>
    /// Optional TMP_FontAsset name from the game's resources.
    /// Null = default WorldBox font.
    /// </summary>
    public string? FontName { get; set; }

    public static UITheme Default => new UITheme();
}
```

### 3.8 Static entry point

```csharp
public static class ModUI
{
    /// <summary>
    /// Obtain the UI registrar for a mod. Call during Init() or PostInit().
    /// Thread-safe; idempotent -- returns the same instance for the same mod.
    /// </summary>
    public static IModUIRegistrar ForMod(IMod mod);

    /// <summary>
    /// Obtain by mod GUID (for cross-mod UI extension).
    /// Returns null if the target mod has not registered yet.
    /// </summary>
    public static IModUIRegistrar? ForMod(string modGuid);
}
```

### 3.9 Attribute-based shorthand (optional)

For mods that prefer zero-code settings, an attribute scan path:

```csharp
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ModSettingAttribute : Attribute
{
    public string Id { get; }
    public string LocaleKey { get; }
    public string? Category { get; set; }
    public string? Icon { get; set; }
    public string? Tooltip { get; set; }
    public float Min { get; set; } = float.MinValue;
    public float Max { get; set; } = float.MaxValue;
    public float Step { get; set; } = 0f;
    public int Order { get; set; } = 0;

    public ModSettingAttribute(string id, string localeKey) { Id = id; LocaleKey = localeKey; }
}

[AttributeUsage(AttributeTargets.Class)]
public class ModSettingsPanelAttribute : Attribute
{
    public string PanelId { get; }
    public string LocaleKey { get; }
    public ModSettingsPanelAttribute(string panelId, string localeKey) { PanelId = panelId; LocaleKey = localeKey; }
}
```

`ModUI.ForMod(mod)` auto-scans the mod assembly for these attributes
during `Init()` and generates the equivalent builder calls.

---

## 4. Example usage

### 4.1 Settings panel via builder (recommended)

```csharp
using NeoModLoader.api;
using WorldSphereMod.UI.Extension;

public class MyMod : MonoBehaviour, IMod, IStagedLoad
{
    static MySettings _settings = new MySettings();

    public void Init()
    {
        var ui = ModUI.ForMod(this);

        ui.Settings("my-mod-settings", "my_mod_settings_title")
          .Category("general_category")
            .Toggle("enable_feature",  "enable_feature_label",
                () => _settings.EnableFeature,
                v  => { _settings.EnableFeature = v; Save(); },
                iconPath: "MyMod/Icons/feature",
                tooltipLocaleKey: "enable_feature_tip")
            .Slider("intensity", "intensity_label",
                () => _settings.Intensity,
                v  => { _settings.Intensity = v; Save(); },
                min: 0f, max: 10f, step: 0.1f)
          .Category("advanced_category")
            .Dropdown<RenderQuality>("quality", "quality_label",
                () => _settings.Quality,
                v  => { _settings.Quality = v; Save(); })
            .SliderInt("max_entities", "max_entities_label",
                () => _settings.MaxEntities,
                v  => { _settings.MaxEntities = v; Save(); },
                min: 10, max: 1000)
          .Separator()
            .ActionButton("reset", "reset_defaults_label",
                () => { _settings = new MySettings(); Save(); },
                iconPath: "MyMod/Icons/reset")
          .Build();

        ui.ToolbarButton("my-mod-btn")
          .Label("my_mod_tab_btn")
          .Icon("MyMod/Icons/logo")
          .OpensPanel("my-mod-settings")
          .Build();

        ui.ApplyTheme(new UITheme
        {
            AccentColor   = new Color(0.2f, 0.6f, 1f),
            FontScale     = 0.95f,
            ItemSpacing   = 10f,
        });
    }

    // ... IStagedLoad, save/load, etc.
}
```

### 4.2 Settings panel via attributes (zero-boilerplate)

```csharp
[ModSettingsPanel("my-mod-settings", "my_mod_settings_title")]
public class MySettings
{
    [ModSetting("enable_feature", "enable_feature_label",
        Category = "general_category", Icon = "MyMod/Icons/feature",
        Tooltip = "enable_feature_tip")]
    public bool EnableFeature = true;

    [ModSetting("intensity", "intensity_label",
        Category = "general_category", Min = 0f, Max = 10f, Step = 0.1f)]
    public float Intensity = 5f;

    [ModSetting("quality", "quality_label", Category = "advanced_category")]
    public RenderQuality Quality = RenderQuality.Medium;
}
```

During `ModUI.ForMod(this)`, the API reflects over `MySettings`, detects
the panel attribute, and auto-generates the builder equivalent.

### 4.3 Overlay HUD

```csharp
ui.Overlay("my-fps-overlay")
  .Anchor(UIAnchor.TopRight)
  .Offset(-10, -10)
  .VisibleWhen(() => _settings.ShowOverlay)
  .OnRender(canvas =>
  {
      canvas.BeginRow();
      canvas.Icon("MyMod/Icons/fps", 12f);
      canvas.Label($"FPS: {(1f / Time.deltaTime):F0}", Color.green, 12);
      canvas.EndRow();
      canvas.ProgressBar(_gpuLoad, Color.red, Color.gray, 6f);
  })
  .Build();
```

### 4.4 Modal dialog

```csharp
ui.Modal("confirm-reset", "confirm_reset_title")
  .Message("confirm_reset_body")
  .Button("cancel_btn", () => { }, ModalButtonStyle.Default)
  .Button("reset_btn", () => { ResetAll(); }, ModalButtonStyle.Danger)
  .Show();
```

---

## 5. Implementation approach

### 5.1 Unity UGUI backend

The API is a thin builder layer over Unity's built-in UGUI. Each builder
method records a descriptor struct into an ordered list. `Build()` walks
the list and emits GameObjects:

| API control       | Unity UGUI mapping                                            |
|--------------------|--------------------------------------------------------------|
| Toggle             | `Toggle` + `Image` (checkmark) + `Text` (label)             |
| Slider / SliderInt | `Slider` + track/handle `Image` + value `Text`              |
| Dropdown           | `Dropdown` (TMP or legacy `Text`) + `ScrollRect` popup      |
| TextInput          | `InputField` + `Text` placeholder/content                    |
| ActionButton       | `Button` + `Image` + `Text`                                 |
| Separator          | `Image` (1px height, stretch horizontal)                     |
| Category header    | `Text` (bold, larger font) + separator below                 |
| Overlay            | Root `Canvas` (ScreenSpaceOverlay, sortOrder=100)            |
| Modal              | Full-screen `Image` (dim) + centered panel `VerticalLayout`  |

All controls are parented under a `ScrollRect` > `VerticalLayoutGroup`
content pane, matching the existing NML `ScrollWindow` pattern
(`WindowCreator.CreateEmptyWindow`).

### 5.2 Layout engine

- **Panels:** `VerticalLayoutGroup` with configurable `spacing` from
  `UITheme.ItemSpacing` and `padding` from `UITheme.Padding`.
- **Categories:** Insert a `LayoutElement` with `preferredHeight=28` for
  the header, followed by a thin separator.
- **Rows (overlay):** `HorizontalLayoutGroup` children inside the
  vertical root, auto-created by `BeginRow()`/`EndRow()`.
- **Modals:** Centered via anchors `(0.5, 0.5)` with pivot `(0.5, 0.5)`,
  fixed width (400px default), height from `ContentSizeFitter`.

### 5.3 Persistence bridge

The settings panel builder auto-bridges to `PlayerConfig.dict` for toggle
state (needed by `PowerButtonSelector.checkToggleIcons`). The getter/setter
lambdas are the mod's source of truth; the bridge is write-through only:

```
setter invoked -> mod saves to its own store -> bridge writes PlayerConfig
```

On load, the bridge reads `PlayerConfig.dict` to restore toggle visuals,
then calls the getter to verify consistency.

### 5.4 Lifecycle integration

```
NML lifecycle        UI Extension hook
--------------       ----------------------------
IMod.OnLoad()        (too early -- no Canvas yet)
IStagedLoad.Init()   ModUI.ForMod(this) available; Settings/Button/Overlay Build() queued
IStagedLoad.PostInit()  Queued builds execute (Canvas guaranteed to exist)
                        Toolbar buttons added to PowersTab
                        Overlays instantiated
```

`ModUI.ForMod()` returns immediately during `Init()`. Builders queue
descriptors. Actual GameObject creation is deferred to a coroutine
scheduled after `PostInit()`, matching the existing `DeferredInitRunner`
pattern in `Mod.cs`.

### 5.5 Cross-mod extension

`ModUI.ForMod(string modGuid)` lets Mod B add controls to Mod A's panel:

```csharp
var wsmUI = ModUI.ForMod("worldsphere3d.fork");
if (wsmUI != null)
{
    wsmUI.Settings("my-addon-panel", "my_addon_title")
        .Toggle("addon_flag", "addon_flag_label",
            () => _addonEnabled, v => _addonEnabled = v)
        .Build();
}
```

The target mod's tab gets an additional sub-window button automatically.

### 5.6 Theme application

`ApplyTheme()` walks all GameObjects registered by the calling mod and:

1. Sets `Image.color` on backgrounds to `BackgroundColor`.
2. Sets `Text.color` / `Text.fontSize` (scaled) on labels.
3. Sets `Slider` track/handle colors from `AccentColor`.
4. Sets `LayoutGroup.spacing` and `LayoutGroup.padding` from theme values.

Themes are per-mod scoped. One mod's theme does not bleed into another's.

---

## 6. Migration path: WorldSphereTab.cs to the new API

### 6.1 Current state

`WorldSphereTab.cs` (935 lines) manually creates:

- 1 `PowersTab` via `TabManager.CreateTab`
- 3 window buttons (Sprite Settings, Camera Settings, World Settings)
  each with 2-4 toggle buttons + 1 slider
- 1 large "3D Phases" window with 23 toggle buttons
- 2 standalone toggle buttons (Is3D, ProfileMode)
- 2 action buttons (Open Sprites, Reset Defaults)
- Manual `PlayerConfig.dict` bridging, reflection-based field lookup,
  sprite caching, and slider generation

### 6.2 Migration steps

**Phase A -- Parallel operation (non-breaking)**

1. Add the `WorldSphereMod.UI.Extension` interfaces to `WorldSphereAPI.dll`.
2. Implement `ModUIRegistrar` in the main mod DLL, backed by UGUI.
3. In `Mod.Init()`, call `ModUI.ForMod(this)` and register all existing
   controls via the builder API. Keep `WorldSphereTab.Begin()` as the
   fallback.
4. Gate new path behind `SavedSettings.UseNewUI = false` (default off).
5. Validate that both paths produce identical UI in-game.

**Phase B -- Feature parity**

6. Port the 23-toggle "3D Phases" panel to a single builder chain:

```csharp
var phases = ui.Settings("wsm3d-phases", "phases_window");
phases.Category("phase_rendering");
// For each phase bool in SavedSettings, one Toggle() call:
foreach (var field in typeof(SavedSettings).GetFields()
    .Where(f => f.FieldType == typeof(bool) && IsPhaseField(f)))
{
    string id = ToSnakeCase(field.Name);
    phases.Toggle(id, $"{id}_label",
        () => (bool)field.GetValue(Core.savedSettings),
        v  => { field.SetValue(Core.savedSettings, v);
                Core.ApplyPhaseToggle(field.Name, v);
                Core.SaveSettings(); },
        iconPath: ResolvePhaseIcon(field.Name));
}
phases.Separator();
phases.ActionButton("reset_defaults", "reset_defaults_label",
    ResetToDefaults, iconPath: "WorldSphereMod/ModIcon");
phases.Build();
```

7. Port sliders (`building_size`, `render_distance`, `tile_length_multiplier`).
8. Port action buttons and window-open buttons.
9. Remove manual `PlayerConfig.dict` bridging (handled by the framework).

**Phase C -- Deprecate old path**

10. Flip `UseNewUI` default to `true`.
11. Mark `WorldSphereTab.Begin()` and `WindowManager` as `[Obsolete]`.
12. After one release cycle, delete the old code path entirely.

### 6.3 Estimated line count reduction

| Component                  | Before (lines) | After (lines) | Reduction |
|---------------------------|----------------|---------------|-----------|
| WorldSphereTab.cs         | 935            | ~120          | -87%      |
| WindowManager class       | 120            | 0 (deleted)   | -100%     |
| PowerWindow class         | 155            | 0 (deleted)   | -100%     |
| New: ModUIRegistrar impl  | 0              | ~400          | (new)     |
| New: UGUI control factory | 0              | ~300          | (new)     |
| **Net**                   | **1210**       | **~820**      | **-32%**  |

The net reduction is modest, but the 820 remaining lines are reusable
infrastructure vs. 1210 lines of one-off procedural UI. Every future
settings panel -- WSM3D or third-party -- is a 10-line builder chain
instead of 100+ lines of manual UGUI.

---

## 7. Non-goals (explicitly out of scope)

- **Custom layout DSL.** Complex multi-column, tabbed, or drag-and-drop
  layouts are not supported. Mods that need arbitrary UI should use raw
  Unity UGUI directly.
- **Runtime hot-reload of UI definitions.** Panels are built once during
  init. Changing structure requires a game restart.
- **UI Toolkit / UITK backend.** WorldBox ships Unity 2022.3 with the
  built-in render pipeline. UI Toolkit is available but not battle-tested
  in the modding context. UGUI is the safer backend for now.
- **Asset import (custom fonts, sprites from disk).** The API uses
  `Resources.Load<Sprite>` paths. Loading arbitrary PNGs from disk is
  the mod's responsibility (as WSM3D already does via `TryLoadPngViaReflection`).
- **Localization framework.** The API takes locale keys and calls
  `LM.Get()` internally. Mods are responsible for shipping their own
  locale files via `ILocalizable.GetLocaleFilesDirectory`.

---

## 8. Open questions

1. **Should the API live in `WorldSphereAPI.dll` or a new
   `WorldSphereMod.UI.Extension.dll`?** Keeping it in the existing API
   assembly is simpler for consumers but increases its size. A separate
   DLL adds a reference but keeps the API assembly focused.

2. **Should `IOverlayCanvas` support immediate-mode every frame, or
   should it be retained-mode (rebuild only on dirty)?** Immediate-mode
   is simpler to implement and use; retained-mode is more performant for
   static overlays.

3. **Should the attribute scan be opt-in (mod calls `ScanAttributes()`)
   or automatic?** Automatic is more magical; opt-in is more predictable.

---

## 9. Dependencies

- Unity 2022.3 UGUI (`UnityEngine.UI`)
- NeoModLoader (`NeoModLoader.api.IMod`, `NeoModLoader.General.UI.Tab.TabManager`)
- Newtonsoft.Json (already shipped, for settings persistence)
- No new NuGet packages or external frameworks
