# WorldSphereMod UI Audit

Audit date: 2025-05-25
Scope: all `.cs` files under `WorldSphereMod/Code/`

---

## 1. UI Systems Used

### Unity UI (uGUI / Canvas-based)
Primary UI system. Used for both screen-space overlays and world-space elements.

| File | Usage |
|------|-------|
| `WorldSphereTab.cs` | Settings panels, slider controls, text labels via `Canvas`, `RectTransform`, `Text`, `Image`, `Slider`, `VerticalLayoutGroup`, `GraphicRaycaster` |
| `Worldspace/RuntimeStatsOverlay.cs` | Screen-space overlay (`RenderMode.ScreenSpaceOverlay`) with `Canvas`, `CanvasScaler`, `Text`, `RectTransform` |
| `Worldspace/DamagePopup.cs` | World-space `Canvas` (`RenderMode.WorldSpace`) with pooled `Text` objects |
| `Worldspace/NameplateWorld.cs` | World-space `Canvas` fallback path with `Text`; also attempts reflection-based `TextMesh3D` |
| `CompoundSphereScripts.cs` | Imports `UnityEngine.UI.CanvasScaler` (used as a static import) |
| `Tools.cs` | Imports `UnityEngine.UI.CanvasScaler` (used as a static import) |
| `3DCamera.cs` | Imports `UnityEngine.UI` |

### WorldBox Power-Button API (game's built-in toolbar system)
Used exclusively by `WorldSphereTab.cs` and `Core.cs` to create mod toolbar buttons and settings windows.

| API | Files |
|-----|-------|
| `PowerButtonCreator.CreateSimpleButton` | `WorldSphereTab.cs:660` |
| `PowerButtonCreator.CreateToggleButton` | `WorldSphereTab.cs:683, 826` |
| `PowerButtonCreator.AddButtonToTab` | `WorldSphereTab.cs:661, 694` |
| `PowerButtonSelector.instance.checkToggleIcons()` | `WorldSphereTab.cs:617, 674, 712, 840`, `Core.cs:262` |
| `WindowCreator.CreateEmptyWindow` | `WorldSphereTab.cs:723` |
| `TabManager.CreateTab` | `WorldSphereTab.cs:66` |
| `Windows.ShowWindow` | `WorldSphereTab.cs:777` |

### Direct 3D Mesh Rendering (non-Canvas UI elements)
Used for in-world indicators that are rendered as 3D meshes, not Canvas elements.

| File | Element |
|------|---------|
| `Worldspace/HealthBar.cs` | Health bars via `MeshFilter`+`MeshRenderer` (legacy mode) or `MeshInstanceBatcher.Submit` (3D mode) |
| `Worldspace/SelectionRing.cs` | Selection rings via `MeshFilter`+`MeshRenderer` with procedural torus mesh |

### IMGUI
**Not used.** The codebase explicitly avoids IMGUI to prevent adding `UnityEngine.IMGUIModule` as a dependency (noted in `RuntimeStatsOverlay.cs:18-19`). One mention of `OnGUI` exists as a comment (`WorldSphereTab.cs:246`).

---

## 2. Settings Panels (`WorldSphereTab.cs`)

Entry point: `Core.Init()` calls `WorldSphereTab.Begin()` at `Core.cs:140`.

### Tab Creation
- **`WorldSphereTab.CreateTab()`** (`WorldSphereTab.cs:63-67`): Creates a `PowersTab` named "WorldSphereMod" via `TabManager.CreateTab`.

### Settings Windows (sub-panels opened from toolbar)

| Window ID | Title locale key | Buttons | Sliders | Lines |
|-----------|-----------------|---------|---------|-------|
| `"Sprite Settings"` | `sprite_settings_window` | `sprites_rotate_to_camera`, `building_style_procgen` | `building_size` (0.1-5) | 187-194 |
| `"Camera Settings"` | `camera_settings_window` | `inverted_camera`, `first_person`, `camera_rotates_with_world`, `upside_down_movement` | `render_distance` (1-20) | 195-202 |
| `"World Settings"` | `world_settings_window` | `cylindrical_shape`, `flat_shape`, `perlin_noise` | `tile_length_multiplier` (1-10) | 203-209 |
| `"3D Phases"` | `phases_window` | 22 toggles (all phase flags + `sanity_cube`) | none | 216-241 |

### Standalone Toolbar Buttons

| Button | Type | Line |
|--------|------|------|
| `Is3D` | Toggle | 186 |
| `Open Sprites` | Simple (opens file browser) | 243 |
| `ProfileMode` | Toggle | 248 |
| `Reset Defaults` | Simple | 249 |

### Slider Construction (`GenerateSlider`, lines 141-183)
Sliders are built entirely from scratch with programmatic GameObjects:
- Root `GameObject` with `Slider` + `Image` components
- Child `Track` with `Image` + `RectTransform`
- Child `Handle Slide Area` with `RectTransform`
- Child `Handle` with `Image` + `RectTransform`
- Text label via `addText()` that updates on value change

---

## 3. Toolbar Buttons

All toolbar buttons are added in `WorldSphereTab.CreateButtons()` (line 184) via two helper methods:

- **`CreateButton()`** (line 658-662): Wraps `PowerButtonCreator.CreateSimpleButton` + `AddButtonToTab`
- **`CreateToggleButton()`** (line 663-713): Wraps `PowerButtonCreator.CreateToggleButton` + `AddButtonToTab` + `GodPower` registration + `PlayerConfig.dict` sync + reflection-based `SavedSettings` mirror

### Phase toggle wiring
`PowerWindow.LoadInputOptions()` (line 800-841) creates per-window toggle buttons within scroll windows using the same `PowerButtonCreator.CreateToggleButton` API.

---

## 4. Overlays/HUDs

### RuntimeStatsOverlay (`Worldspace/RuntimeStatsOverlay.cs`)
- **Type**: Screen-space overlay Canvas (`RenderMode.ScreenSpaceOverlay`, `sortingOrder=32760`)
- **Creation**: `EnsureCreated()` called from `ToggleProfileMode()` in `WorldSphereTab.cs:531`; also checked in `Mod.Init`
- **Content**: Single `Text` label showing FPS, draw calls, instances, cache hit rates, frame time
- **Gate**: `Core.savedSettings.ProfilerDump`
- **Lifecycle**: `AddComponent<RuntimeStatsOverlay>()` on `Mod.Object`; destroyed via `OnDestroy`

### DamagePopup (`Worldspace/DamagePopup.cs`)
- **Type**: Pool of 64 world-space `Canvas` GameObjects (`RenderMode.WorldSpace`)
- **Creation**: `DamagePopup.Init()` called from `WorldUIRenderer.Awake()` (line 72)
- **Content**: Per-popup `Text` component showing integer damage value
- **Gate**: `Core.savedSettings.WorldspaceUI`
- **Lifecycle**: Pool pre-built at init, entries recycled; `Clear()` on world unload

### NameplateWorld (`Worldspace/NameplateWorld.cs`)
- **Type**: Per-actor world-space name label
- **Creation**: `NameplateWorld.Attach()` called from `WorldUIRenderer.RegisterActor()` (line 133)
- **Content**: Either `TextMesh3D` (reflection-resolved) or fallback `Canvas` + `Text`
- **Gate**: `Core.savedSettings.WorldspaceUI` AND `Core.savedSettings.WorldspaceLabel3D`
- **Lifecycle**: Attached to rig transforms; destroyed via `Detach()` or `OnDestroy`

### HealthBar (`Worldspace/HealthBar.cs`)
- **Type**: Per-actor health indicator (two modes)
  - Legacy: `MeshFilter`+`MeshRenderer` with shared quad mesh + `Sprites/Default` material
  - 3D: Direct `MeshInstanceBatcher.Submit` calls (no GameObjects)
- **Creation**: `HealthBar.Attach()` called from `WorldUIRenderer.RegisterActor()` (line 134)
- **Gate**: `Core.savedSettings.WorldspaceUI` (3D mode additionally gated on `WorldspaceHealth3D`)
- **Lifecycle**: Attached to rig transforms; `Reset()` on world unload

### SelectionRing (`Worldspace/SelectionRing.cs`)
- **Type**: Per-actor 3D torus mesh (procedural annulus geometry)
- **Creation**: `SelectionRing.Show()` called from `SelectionHooks` Harmony postfixes
- **Gate**: `Core.savedSettings.WorldspaceUI`
- **Lifecycle**: `Clear()` on world unload

### Phase Icon Overlay (inside `WorldSphereTab.cs`)
- `AddPhaseIconAndLabel()` (line 843-865) adds a 16x16 `Image` + `Text` label to each phase toggle button in the "3D Phases" window
- Uses `RectTransform` positioning with hardcoded anchor/pivot values

---

## 5. Fragile Patterns

### `GameObject.Find` with hardcoded Canvas paths (HIGH RISK)

All in `WorldSphereTab.cs`. These will break if WorldBox changes its UI hierarchy:

| Line | Path |
|------|------|
| 125 | `"/Canvas Container Main/Canvas - Windows/windows/" + window + "/Background/Title"` |
| 447 | `"/Canvas Container Main/Canvas - Windows/windows/{windowId}"` |
| 725 | `"/Canvas Container Main/Canvas - Windows/windows/{window.name}/Background/Scroll View"` |
| 726 | `"/Canvas Container Main/Canvas - Windows/windows/{window.name}/Background/Scroll View/Viewport/Content"` |

**Impact**: If any path segment changes (e.g., "Canvas Container Main" renamed), all settings windows fail silently (null checks exist at 727-730 but only log a warning and skip window creation entirely).

### `FindObjectOfType` (MODERATE RISK)

| File | Line | Target |
|------|------|--------|
| `AutoTest.cs` | 196 | `FindObjectOfType<WorldTilemap>()` -- fallback only, used when `World.world?.tilemap` is null |

### `Resources.FindObjectsOfTypeAll` (MODERATE RISK)

| File | Line | Target |
|------|------|--------|
| `NameplateWorld.cs` | 85 | `Resources.FindObjectsOfTypeAll<NameplateWorld>()` -- used as fallback in `Detach()` when rig lookup fails |
| `NameplateWorld.cs` | 328 | `Resources.FindObjectsOfTypeAll<Font>()` -- font resolution fallback |

### Reflection-based member access (MODERATE RISK)

Heavy use of reflection to access WorldBox internals that may change between versions:

| File | Line(s) | Target |
|------|---------|--------|
| `WorldSphereTab.cs` | 89-121 | `GodPower.icon`/`sprite` field/property resolution |
| `WorldSphereTab.cs` | 456-481 | `Windows.HideWindow`/`CloseWindow` method resolution |
| `HealthBar.cs` | 86-93 | `Actor.getHealthRatio()` method resolution |
| `HealthBar.cs` | 203-266 | `Actor.health_bar` field resolution (12 candidate names) |
| `NameplateWorld.cs` | 168-186 | `TextMesh3D` component property access via reflection |
| `NameplateWorld.cs` | 340-344 | `TextMesh3D` type resolution across multiple assemblies |
| `NameplateWorld.cs` | 363-387 | `Actor.head_object` field/property resolution |

### Hardcoded layout values (LOW RISK)

| File | Line | Value |
|------|------|-------|
| `WorldSphereTab.cs` | 802 | `Buttons.Count * 125` pixel height per button |
| `WorldSphereTab.cs` | 834 | `new Vector2(64, 64)` button size |
| `WorldSphereTab.cs` | 856-857 | Phase icon anchored at `(-18, -40)`, size `(16, 16)` |
| `WorldSphereTab.cs` | 134 | Text localPosition offset `+ new Vector3(0, -50, 0)` |
| `RuntimeStatsOverlay.cs` | 70-71 | Label anchored at `(8, -8)`, size `(560, 80)` |

---

## 6. Summary by File

| File | UI System | Role | Fragile Patterns |
|------|-----------|------|-----------------|
| `WorldSphereTab.cs` | WorldBox PowerButton API + uGUI | Settings panels, toolbar, sliders | 4x `GameObject.Find` on hardcoded paths, reflection for GodPower/Windows |
| `Core.cs` | WorldBox PowerButton API | Hotkey-driven toggle icon refresh | `PowerButtonSelector.instance` direct access |
| `Worldspace/RuntimeStatsOverlay.cs` | uGUI ScreenSpace | Debug stats HUD | None significant |
| `Worldspace/DamagePopup.cs` | uGUI WorldSpace | Floating damage numbers | None significant |
| `Worldspace/NameplateWorld.cs` | uGUI WorldSpace + reflection TextMesh3D | Actor name labels | `FindObjectsOfTypeAll`, 5 reflection chains |
| `Worldspace/HealthBar.cs` | MeshFilter/MeshRenderer + MeshInstanceBatcher | HP bars | Reflection for `getHealthRatio` + health_bar field |
| `Worldspace/SelectionRing.cs` | MeshFilter/MeshRenderer | Selection indicator | None significant |
| `Worldspace/SelectionHooks.cs` | (Harmony hooks, no direct UI) | Wires selection events to SelectionRing | None |
| `Worldspace/WorldUIRenderer.cs` | (Orchestrator, no direct UI) | Per-actor rig graph, LateUpdate driver | None |
| `CompoundSphereScripts.cs` | uGUI import only | Sphere tile rendering | None |
| `Tools.cs` | uGUI import only | Utility helpers | None |
| `3DCamera.cs` | uGUI import only | Camera management | None |
| `AutoTest.cs` | None | Automated testing | `FindObjectOfType<WorldTilemap>` |

---

## 7. Recommendations

1. **Extract `GameObject.Find` paths to constants** -- the 4 hardcoded Canvas paths in `WorldSphereTab.cs` are the single highest-risk pattern. If WorldBox updates its UI hierarchy, every settings window breaks. Consider caching the root `Transform` once and using `transform.Find` relative paths, or falling back gracefully with user-facing errors.

2. **Pool the reflection lookups in HealthBar/NameplateWorld** -- already partially done via `_healthBarMemberCache` and `_ratioMethodResolved`, but the initial resolution still iterates 12+ candidate field names. Consider documenting which WorldBox version these names target.

3. **RuntimeStatsOverlay is safe** -- cleanest UI code in the mod. Uses `EnsureCreated()` pattern, proper `OnDestroy` cleanup, single responsibility.

4. **DamagePopup pool is well-designed** -- fixed-size pool with oldest-entry recycling. No fragile patterns.

5. **Consider abstracting the PowerButton creation** -- `WorldSphereTab.CreateToggleButton()` (lines 663-713) does 7 distinct operations (GodPower creation, sprite reflection, AssetManager registration, PlayerConfig sync, button creation, option library registration, SavedSettings reflection mirror). A builder pattern or data-driven approach would reduce the per-toggle boilerplate.
