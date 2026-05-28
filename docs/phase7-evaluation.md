# Phase 7 — Worldspace UI Evaluation

Evaluation of `WorldSphereMod/Code/Worldspace/` against the Phase 7 plan and
in-game behavior. Covers what landed, where the seams are, and the
label-sizing regression that motivated the scale fix in `NameplateWorld.cs`.

## Components in scope

| File | Role |
|---|---|
| `WorldUIRenderer.cs` | Per-actor follow-rig manager, parented under `Mod.Object`. Owns the `Actor → Transform` rig graph; children attach to these rigs. |
| `NameplateWorld.cs` | Per-actor world-space name label (TextMesh3D when available, fallback `Canvas`+`Text`). Suppresses vanilla `NameplateText`. |
| `HealthBar.cs` | Per-actor health bar. Two modes: legacy quad-mesh + camera billboard, or full 3D box-mesh submitted via `MeshInstanceBatcher`. |
| `SelectionRing.cs` | Static manager. Shared torus mesh per quantised radius, drawn flat under selected actors. |
| `DamagePopup.cs` | Floating damage numbers. |
| `PhaseToast.cs`, `RuntimeStatsOverlay.cs` | Diagnostic HUD; orthogonal to per-actor UI. |
| `SelectionHooks.cs` | Harmony hooks that drive `SelectionRing.Show/Hide`. |

## Nameplate3D vs vanilla `NameplateText`

Vanilla `NameplateText` is a `SpriteRenderer`/`TextMeshPro` parented to
the actor's `head_object`. Upstream sizes it in screen space via the 2D
camera and rotates it to face the orthographic top-down rig.

`NameplateWorld` (Phase 7 Step 2) parents to the shared **worldspace rig**
(`WorldUIRenderer.Rigs[actor]`), not the actor head, so it inherits the
lifted 3D position the voxel actors use. It tries `TextMesh3D` via
reflection (`Assembly-CSharp`, `Assembly-CSharp-Publicized`, unqualified)
and falls back to a world-space `Canvas` + `Text` if the TextMesh3D type
isn't found at runtime. The upstream nameplate is **suppressed**
(`enabled = false`) while ours is active and restored on teardown — see
`SuppressUpstreamNameplate` and the `_suppressedUpstream` dictionary.
Suppression is keyed off the actor's `head_object` via reflection
(`ResolveHeadTransform`).

Key divergences from vanilla:

- **Parent**: worldspace rig root, not head bone.
- **Sizing**: vanilla relies on the 2D ortho camera; ours has to fight
  the 3D camera distance and the `VoxelScaleMultiplier` (~8x) blow-up.
- **Faces camera**: `transform.LookAt(cam.position, Vector3.up)` every
  `LateUpdate` — same intent as vanilla's billboard, different rig.
- **Fade**: configurable `NameplateFadeNear`/`NameplateFadeFar`
  (defaults 10..30) world-unit distance fade; vanilla has no distance
  fade in the 3D camera mode.
- **Font**: prefers `Helvetica Bold`, falls back to built-in `Arial.ttf`.

## Health bar mesh path

Two paths gated by `Core.savedSettings.WorldspaceHealth3D`:

**Legacy (flag off):**
- `MeshFilter` + `MeshRenderer` attached to the rig child.
- Shared `WSM3D.HpBarQuad` mesh (`BuildQuadMesh`).
- Shared material from `Shader.Find("Sprites/Default")`.
- `LateUpdate` Y-billboards via `Quaternion.LookRotation(fwd with y=0)`,
  scales X by current HP ratio, lerps red→green.

**3D (flag on):**
- Upstream `SpriteRenderer` health bar is suppressed via reflection
  (`ResolveUpstreamHealthBarRenderer` walks a known-name list, then
  falls back to scanning fields/properties whose names contain both
  `health` and `bar`). Resolution result is cached per `Actor` type.
- `Submit3DBar` builds two `Matrix4x4.TRS` calls each frame: a full-length
  red background bar and a length-scaled green foreground bar. Both
  submitted via `MeshInstanceBatcher.Submit` with the shared
  `WSM3D.HpBarBox` mesh and `VoxelRender.GetResolvedMaterial()`.
- Bar is anchored at `rig.position + Vector3.up * kHeadOffset` (0.2) and
  Y-axis-only billboards via `look.y = 0` to avoid pitch wobble.

Two known smells in this path:
1. The bar uses `VoxelRender.GetResolvedMaterial()` — the same material
   as actor voxel meshes. Tints come from the per-instance `_Color`
   property block, which is fine for batching but means the bar inherits
   any AlphaTest/`renderQueue` regressions the voxel material picks up
   (cf. WSM3D alpha.8 victory notes).
2. The bar's world-space length is `kFullLength = 1f` — at
   `VoxelScaleMultiplier = 8`, a 1-world-unit bar sits *under* an 8-unit
   actor mesh and looks tiny. Not addressed in this pass; tracked as
   follow-up.

## Selection ring

`SelectionRing` is a **static manager**, not a per-actor MonoBehaviour
(intentional — avoids one component per selected actor when batch-selecting
a faction). One `GameObject` per selected actor (`SelectionRing:{hash}`)
with shared `Mesh` keyed by quantised outer radius.

- Mesh is a flat 2-ring strip (`segments * 2` verts, `segments * 6` tris),
  built once per unique radius (`_torusByRadius`).
- Radius is currently a placeholder constant `0.4 + 0.2 = 0.6` — the Phase 7
  plan specifies `stats.size * 0.6 + 0.2` once stats wiring is finalised.
- Per-frame update in `UpdateAll` (called from
  `WorldUIRenderer.LateUpdate`) re-positions every ring via
  `Tools.To3DTileHeight(a.current_position, 0.005f)` and orients it
  tangent to the cylindrical world (`Tools.GetRotation * Euler(90,0,0)`)
  so it doesn't punch through hills.
- Material: `Resources.Load<Shader>("Shaders/SelectionRing")` →
  `Sprites/Default` fallback. Color `(0.2, 1, 0.4, 0.6)`.
- Shadow casting off, receive shadows off — flat decal-style geometry.

## Scale-with-distance logic

The label scale ladder before the fix:

```
const float kReferenceDistance = 10f;
const float kMinScale = 0.25f;
const float kMaxScale = 4f;
const float kBaseScale = 0.15f;

float distanceFactor = Mathf.Clamp(d / kReferenceDistance, kMinScale, kMaxScale);
transform.localScale = Vector3.one * (kBaseScale * distanceFactor);
```

Intent: hold a constant screen-space size by **growing** scale with
camera distance — same idea as a perspective-divide-cancelling billboard.

Why this rendered huge in practice:
- The rig sits at world units that already incorporate
  `VoxelScaleMultiplier` (~8x) — the parent's effective scale is large.
- `kBaseScale = 0.15` doesn't cancel that out; at the reference distance
  the label is `0.15 * rig_scale ≈ 1.2` world units tall.
- At strategy-view distance (`d ≈ 80..120`), `distanceFactor` clamps at
  `kMaxScale = 4`, so the label balloons to `0.15 * 4 * rig_scale ≈ 4.8`
  world units — comparable to the *entire actor*.
- Stacked with `LookAt(camera)` (full 3-axis), the billboard occludes the
  voxel actor head at any oblique view.

## Screen-space vs world-space

The codebase is mixed:

- `NameplateWorld` fallback path uses a **world-space `Canvas`**
  (`canvas.renderMode = RenderMode.WorldSpace`) with `worldCamera =
  CameraManager.MainCamera`. RectTransform sizeDelta is `(6, 1.5)` in
  *Canvas* units, which interact with `transform.localScale` — another
  reason large `localScale` makes the fallback path enormous.
- `TextMesh3D` path is **pure world-space mesh**; size is whatever the
  3D text renderer's `size` field maps to (`SetFloatValue(label, 0.5f,
  "size")`). Distance scaling is applied via `transform.localScale`.
- `HealthBar` legacy path is **world-space mesh** that Y-billboards.
- `HealthBar` 3D path is **world-space instanced mesh** through the
  voxel batcher; no transform, position is per-frame Matrix4x4.TRS.
- `SelectionRing` is **world-space mesh, never billboards** — by design
  (it's a ground decal).
- `DamagePopup` and `PhaseToast` are screen-space HUD; they live outside
  the rig.

There is no screen-space → world-space adapter. Anywhere the camera
distance enters the picture, code multiplies `transform.localScale`
directly. This works but couples every label to the
`VoxelScaleMultiplier` factor on the parent rig.

## Why labels render huge — root cause

Two compounding issues:

1. **Parent scale is ~8x** (`VoxelScaleMultiplier`). Anything attached
   to the rig with `localScale = 1` is already an 8x-world-unit object.
2. **Old distance policy grew** scale with distance up to `kMaxScale = 4`.
   At strategy-view altitude this multiplies (1) by 4×, producing a
   label that occupies as much screen area as the actor.

The fade range (`NameplateFadeNear=10`/`NameplateFadeFar=30`) masks this
at extreme zoom (label is fully transparent past 30 units), but at
intermediate distances (d ≈ 20..30) the label is huge *and* still ~50%
opaque.

## Scale fix applied

`NameplateWorld.LateUpdate` now clamps `transform.localScale` to
`Min(1, cameraDistance / NameplateScaleDistanceDivisor)`, with the
`NameplateBaseScale` floor so close-up text stays legible:

```csharp
float baseScale = Core.savedSettings.NameplateBaseScale;          // 0.15
float divisor   = Core.savedSettings.NameplateScaleDistanceDivisor; // 100
float clamped   = Mathf.Min(1f, d / divisor);
float effective = Mathf.Max(baseScale, clamped);
transform.localScale = Vector3.one * effective;
```

With the default divisor of 100 this is exactly `Min(1, d/100)` as
specified. The constants are surfaced as `SavedSettings` tunables
(`NameplateBaseScale`, `NameplateScaleDistanceDivisor`,
`NameplateFadeNear`, `NameplateFadeFar`, etc.) so the policy can be
re-tuned per-world without recompiling.

Effect:
- At `d <= 15` the floor `baseScale = 0.15` wins → label is small at
  close range (~`0.15 * 8 ≈ 1.2` world units, comparable to an actor
  head).
- At `d` from 15 → 100 the linear policy takes over; scale tops out at
  1.0 (8 world units after parent multiplier) at strategy view.
- Past `d = 100` the clamp holds at 1.0 — label no longer grows
  unbounded, which was the visible "huge label" regression.

Follow-ups (out of scope for this fix):
- Tie scaling to the parent rig's effective scale instead of hard-coding
  the base-scale floor — would eliminate the implicit
  `VoxelScaleMultiplier` coupling.
- Move `NameplateFadeFar` past the clamp horizon (`>100`) once the size
  is sensible; right now far labels are invisible whether they're sized
  right or not.
- Adopt the same `Min(1, d/divisor)` policy for `HealthBar` so the bar
  doesn't dominate the screen at mid-range; the 3D path currently has
  no distance compensation at all.

## Affected files

- `WorldSphereMod/Code/Worldspace/NameplateWorld.cs` — scale clamp.
- `WorldSphereMod/Code/SavedSettings.cs` — `Nameplate*` tunables.
- `docs/phase7-evaluation.md` — this file.
