# `render_data` field map (WorldBox publicized Assembly-CSharp)

Source: ilspycmd decompile of `worldbox_Data/StreamingAssets/mods/NML/Assembly-CSharp-Publicized.dll`.
Captured 2026-05-17 for the WorldSphereMod3D fork (task #4, Phase 2 Risk 2).

---

## `ActorManager.render_data` → `ActorRenderData`

Declaration: `public readonly ActorRenderData render_data = new ActorRenderData(4096);` (global namespace, class).

| Field | Type |
|---|---|
| `positions` | `Vector3[]` |
| `scales` | `Vector3[]` |
| `rotations` | `Vector3[]` |
| `colors` | `Color[]` |
| `has_normal_render` | `bool[]` |
| `main_sprites` | `Sprite[]` |
| `main_sprite_colored` | `Sprite[]` |
| `materials` | `Material[]` |
| `flip_x_states` | `bool[]` |
| `shadows` | `bool[]` |
| `shadow_position` | `Vector3[]` *(singular)* |
| `shadow_scales` | `Vector3[]` |
| `shadow_sprites` | `Sprite[]` |
| `has_item` | `bool[]` |
| `item_scale` | `Vector3[]` *(singular)* |
| `item_pos` | `Vector3[]` *(singular)* |
| `item_sprites` | `Sprite[]` |

Visible-count + array iteration: `__instance._visible_actors_count` + `__instance._array_visible_actors` (pattern; confirm exact name on next visit).

---

## `BuildingManager.render_data` → `BuildingRenderData`

Declaration: `public BuildingRenderData render_data = new BuildingRenderData(4096);` (not readonly).

| Field | Type |
|---|---|
| `positions` | `Vector3[]` |
| `scales` | `Vector3[]` |
| `rotations` | `Vector3[]` |
| `colored_sprites` | `Sprite[]` |
| `main_sprites` | `Sprite[]` |
| `materials` | `Material[]` |
| `flip_x_states` | `bool[]` |
| `colors` | `Color[]` |
| `shadows` | `bool[]` |
| `shadow_sprites` | `Sprite[]` |

Visible-count + array iteration: `__instance._visible_buildings_count` + `__instance._array_visible_buildings`.

---

## Diff & implications for the voxel/procgen building Postfix

**Actor-only fields (not on Building):**
`has_normal_render`, `main_sprite_colored`, `shadow_position`, `shadow_scales`, `has_item`, `item_scale`, `item_pos`, `item_sprites`.

**Building-only field:**
`colored_sprites` (Actor's analogue is named `main_sprite_colored`).

**Identical name + type on both:** `positions`, `scales`, `rotations`, `colors`, `main_sprites`, `materials`, `flip_x_states`, `shadows`, `shadow_sprites`.

### Consequence 1 — sprite suppression mechanism (Phase 2 Risk 2)
The actor voxel Postfix sets `render_data.has_normal_render[i] = false` to suppress the upstream sprite quad after submitting a mesh. **`BuildingRenderData` has no `has_normal_render` field.** Three options for buildings:

1. `render_data.main_sprites[i] = null` — relies on downstream null-check, needs verification in `BuildingManager.update`/render code path.
2. `render_data.scales[i] = Vector3.zero` — sprite still draws but at zero size (likely culled).
3. Maintain a mod-side `bool[]` parallel to `render_data` and gate sprite paint via a transpiler — heavier.

Recommendation when implementing: try option 1 first (read the building update path to confirm it null-checks `main_sprites`), fall back to option 2 if it doesn't.

### Consequence 2 — no items, no per-instance shadow positioning on buildings
The Phase 1 actor Postfix branches that read `has_item`/`item_*` and `shadow_position`/`shadow_scales` must be dropped from the building Postfix entirely. Buildings just have a `shadows: bool[]` flag and a `shadow_sprites: Sprite[]` — same shape, much simpler.

---

## Decompile artifacts (kept for next visit)
- `C:/Users/koosh/AppData/Local/Temp/wb_decomp/ActorRenderData.decompiled.cs`
- `C:/Users/koosh/AppData/Local/Temp/wb_decomp/BuildingRenderData.decompiled.cs`
- `C:/Users/koosh/AppData/Local/Temp/wb_decomp/ActorManager.decompiled.cs`
- `C:/Users/koosh/AppData/Local/Temp/wb_decomp/BuildingManager.decompiled.cs`

Tool: `ilspycmd` 10.0.1.8346.
