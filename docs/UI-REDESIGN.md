# WorldSphereMod UI Redesign Proposal

## Problem Statement

The current settings experience works, but it does not scale well:

- The mod already has a dedicated WorldSphere tab, plus a separate `3D Phases` window that is explicitly suppressed on world load in code.
- The phase list is growing, and the current flat toggle presentation makes it harder to find the right control quickly.
- The mod needs a UI that is discoverable, keeps phase flags understandable, and does not fight the existing NeoModLoader / WorldBox tab model.

Current state worth preserving:

- `WorldSphereTab.cs` already owns the mod tab and all settings windows.
- Phase toggles persist through `SavedSettings` and `PlayerConfig`.
- `SavedSettings.cs` already contains a fairly large flag surface, so the UX should assume more toggles will arrive later.

## Decision Criteria

I evaluated each option against the same criteria:

1. Discoverability: can a player find the setting they want without memorizing phase names?
2. Scale: will it still work when the list grows?
3. Integration risk: how much code and UI plumbing does it require?
4. Compatibility: how well does it fit the existing WorldBox / NeoModLoader UI model?
5. Maintenance: how likely is it to become a one-off UI system that drifts from the rest of the mod?

## Options

### A. Keep the popup modal, but rebuild it with sections + search

Implementation effort: **S**

This keeps the current `3D Phases` modal as the entry point, but makes it much easier to navigate:

- Add a search box at the top.
- Group toggles into sections such as `Core rendering`, `Lighting`, `Worldspace`, `Weather`, `Diagnostics`.
- Show short descriptions and current state inline.
- Add a `Show only recommended` or `Pinned` row for the toggles people touch most often.

Pros:

- Lowest-risk path.
- Preserves the existing mental model: "open the phases window, flip flags."
- Search makes the growing toggle list manageable without redesigning the whole tab system.
- Easy to keep consistent with the rest of the mod's settings windows.
- Can be implemented incrementally without changing the save model.

Cons:

- Still a modal, so it is one extra click away from the main tab.
- If the modal grows too large, it can still feel like a settings dump.
- Search only helps after the player opens the window.

ASCII mockup:

```text
+--------------------------------------------------------------+
| 3D Phases                                            [x]     |
| Search: [ worldspace____________________________ ]          |
| Filter: [All] [Core] [Lighting] [UI] [Weather] [Diag]        |
+--------------------------------------------------------------+
| Core Rendering                                                |
|  [x] Voxel Entities         Render actors/items as 3D meshes  |
|  [x] Procedural Buildings   Replace building sprites         |
|  [x] Crossed Quad Foliage   3D foliage cards and overlays    |
|                                                              |
| Worldspace UI                                                 |
|  [x] Worldspace UI          Nameplates, HP bars, selection   |
|  [x] Worldspace Health 3D   3D bar style                     |
|                                                              |
| Lighting / Atmosphere                                         |
|  [x] High Shadows           Cascaded shadows                 |
|  [x] Day Night Cycle        Time-of-day driver               |
|  [ ] Post FX                Bloom / vignette / grading        |
|                                                              |
| [Reset Defaults]                               [Close]        |
+--------------------------------------------------------------+
```

### B. Move the toggles into the WorldSphere top tab as collapsible groups

Implementation effort: **M**

This removes the separate phases modal and folds the phase toggles into the existing WorldSphere tab. The tab becomes the one place where the user manages the mod.

Pros:

- Best discoverability inside the current mod UI.
- Fewer context switches: the user is already in the WorldSphere tab.
- Collapsible groups keep the long list from feeling like one giant wall of switches.
- Can surface the most important settings immediately, while keeping advanced groups collapsed.

Cons:

- More layout work than option A.
- The WorldSphere tab already contains multiple windows and sliders, so adding many collapsible groups risks crowding.
- Requires careful handling of tab width, scroll behavior, and spacing across WorldBox resolutions.
- More work to keep the visual style readable if the toggle count keeps increasing.

ASCII mockup:

```text
+--------------------------------------+
| WorldSphereMod                       |
|--------------------------------------|
| [Sprite Settings]                    |
| [Camera Settings]                    |
| [World Settings]                     |
|                                      |
| Phase Toggles                        |
|  v Core Rendering                    |
|   [x] Voxel Entities                 |
|   [x] Procedural Buildings           |
|   [x] Crossed Quad Foliage           |
|                                      |
|  > Lighting                          |
|  > Worldspace UI                     |
|  > Weather                           |
|  > Diagnostics                       |
|                                      |
| [Reset Defaults]                     |
+--------------------------------------+
```

### C. F-key panel overlay with a custom Unity UGUI canvas

Implementation effort: **L**

This creates a dedicated overlay panel, likely opened by an `F` key, with a custom Unity UGUI canvas that behaves more like an in-game settings drawer than a normal WorldBox window.

Pros:

- Most flexible layout for search, filtering, grouping, and future controls.
- Can support richer interactions later: presets, tooltips, keyboard navigation, favorites, and inline status chips.
- Can be made visually distinct from the default WorldBox windows.

Cons:

- Highest implementation and maintenance cost.
- Risks input/focus conflicts with the game UI.
- More likely to become a custom UI island that needs ongoing upkeep.
- Hardest option to keep aligned with the rest of the mod and NeoModLoader conventions.
- More likely to create bugs on different resolutions or UI scaling setups.

ASCII mockup:

```text
             +--------------------------------------+
             | WorldSphere Settings          [F]   |
             | Search: [ voxel________________ ]   |
             | [Core] [Lighting] [UI] [Weather]    |
             +--------------------------------------+
             |  Core Rendering                      |
             |  [x] Voxel Entities                  |
             |  [x] Procedural Buildings            |
             |  [x] Crossed Quad Foliage            |
             |                                      |
             |  Lighting / Atmosphere                |
             |  [x] High Shadows                     |
             |  [x] Day Night Cycle                  |
             |                                      |
             |  [Apply] [Reset] [Close]              |
             +--------------------------------------+
```

## Recommendation

**Recommend option A for the next pass.**

Why:

- It gives the biggest UX improvement for the smallest technical risk.
- It fits the current architecture: the mod already uses a dedicated phases window and already persists the relevant flags.
- It avoids introducing a new UI framework or a more invasive tab refactor before the settings model itself is settled.
- Search and sections solve the real problem the current UI has: too many toggles in one flat list.

If the goal is a long-term UI polish roadmap, option B is the best follow-up once the team wants a more integrated tab experience. Option C is only justified if the mod later needs a full custom in-game control surface, not just a better settings panel.

## Suggested Shape For Option A

If this proposal moves forward, the modal should be organized by intent rather than by phase number:

- `Core Rendering`
- `Lighting / Atmosphere`
- `Worldspace UI`
- `Weather / Effects`
- `Diagnostics`

Useful extras:

- Search should match both the toggle id and the human-readable label.
- Each section should be collapsible.
- The current state should be visible without opening the tooltip.
- Keep `Reset Defaults` visible, but visually separated from the normal toggle list.

## Bottom Line

The best immediate redesign is to keep the popup modal and make it searchable, grouped, and easier to scan. That gives the mod a better UI now without turning the settings surface into a separate subsystem.
