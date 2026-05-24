# Phase 7 — Worldspace UI

Source: design pass by `feature-dev:code-architect` (agent run 2026-05-17).
Historical design-state snapshot; use `docs/HANDOFF.md` for current defaults.

---

## 1. Module Layout

Four files under `WorldSphereMod/Code/Worldspace/` + three shaders.

- **`WorldUIRenderer.cs`** — `MonoBehaviour` on `Mod.Object`. Owns the actor-follow-rig root, per-frame `LateUpdate` repositioning, and world-lifecycle hooks. Integration shim: replaces the screen-projection Prefix at `General.cs:421` and extends `drawSelectedUnits` at `QuantumSprites.cs:188`.
- **`NameplateWorld.cs`** — per-actor `MonoBehaviour`. Pooled `TextMeshPro` world-canvas + health-bar quad. Distance fade, depth-soft fade via `NameplateSoft.shader`.
- **`SelectionRing.cs`** — static manager. Procedural torus `MeshRenderer` per selected actor; shared `Mesh`; per-instance `MaterialPropertyBlock`. Animated dotted scroll via `SelectionRing.shader`.
- **`DamagePopup.cs`** — static pool of 64 world-space TMPs. `Spawn(worldPos, value, tint)` dequeues, plays rise-and-fade, re-enqueues. Ephemeral only.

Namespace `WorldSphereMod.Worldspace`.

---

## 2. Public Type Signatures

```csharp
sealed class WorldUIRenderer : MonoBehaviour
{
    static WorldUIRenderer? Instance;
    static void EnsureCreated();
    static void OnWorldUnload();
    void LateUpdate();
}

sealed class NameplateWorld : MonoBehaviour
{
    Actor Actor;
    TextMeshPro Label;
    Canvas LabelCanvas;     // RenderMode.WorldSpace, pixelsPerUnit = 100
    MeshRenderer HpBar;
    MaterialPropertyBlock HpBlock;
    static float kFadeNear = 10f, kFadeFar = 30f;
    static NameplateWorld Attach(Actor a, Transform rigRoot);
    static void Detach(Actor a);
    void Refresh(Vector3 worldPos, float camDistance, float hpFraction);
}

static class SelectionRing
{
    static Mesh GetTorusMesh(float outerR);   // cached, radius quantised 0.05
    static void Show(Actor a);
    static void Hide(Actor a);
    static void UpdateAll(Dictionary<Actor, Transform> rigs);
}

static class DamagePopup
{
    static int PoolSize = 64;
    static void Init(Transform poolRoot);
    static void Spawn(Vector3 worldPos, int value, Color tint);
    static void Clear();
}
```

---

## 3. Actor Follow-Rig

Each visible actor gets one `Transform` child of `WorldUIRenderer`'s root. Per `LateUpdate`:

```csharp
rig.position = Tools.To3DTileHeight(actor.current_position,
    Tools.GetTileHeightSmooth(actor.current_tile) + kRigLift);
```

`kRigLift = 0.5f` (mid-body height). Nameplate canvas + HP quad are children of this transform — free position update.

`SelectionRing` placed separately at `GetTileHeightSmooth + 0.005f`, rotated to `Core.Sphere.GetRotation(ringPos)` so it lies tangent on cylindrical worlds.

`WorldUIRenderer` maintains `Dictionary<Actor, Transform> _rigs` (O(1) lookup) and `HashSet<Actor> _selected`.

Creation trigger: Postfix on `ActorManager.precalculateRenderDataParallel` — for each actor in `visible_units` not in `_rigs`, register. Destruction: Postfix on `Actor.kill` + `OnWorldUnload`.

---

## 4. Nameplate + Health Bar

**Nameplate.** WorldSpace canvas, `pixelsPerUnit = 100`, scale `(0.01, 0.01, 0.01)`. TMP 12pt. Face camera each `LateUpdate` via `LookAt` + 180° Y flip.

Distance alpha: `Label.alpha = 1f - Mathf.InverseLerp(kFadeNear, kFadeFar, camDistance)`.

Depth-soft fade (`NameplateSoft.shader`, URP transparent): per-fragment `alpha *= saturate((sceneDepth - textDepth) / _SoftRange)` via `_CameraDepthTexture` sample. Prevents hard nameplate clipping into terrain.

**HP bar.** Procedural `0.8 × 0.08` world-unit quad. `HealthBar.shader` (URP unlit, transparent): `float alive = step(uv.x, _HpFraction); color = lerp(_Dead, _Alive, alive)`. `_HpFraction` via `MaterialPropertyBlock.SetFloat` each `Refresh`. Yaw matches `RotateCamera.Rotation.y`; Y locked world-vertical.

---

## 5. Selection Ring

Torus: `outerR = actor.stats.size * 0.6f + 0.2f`, strip width `0.04f`, 64 segments. Single quad-strip mesh; radius quantised to `0.05f` steps prevents mesh proliferation.

`SelectionRing.shader` (URP unlit, transparent): UV scrolls at `_ScrollSpeed * Time.time` along circumference. Dotted pattern: `clip(frac(uv.x * _DotCount) - _DotDuty)`. Render state: `ZWrite Off; ZTest LEqual; Blend SrcAlpha OneMinusSrcAlpha; Offset -1, -1`. Polygon offset eliminates coplanar z-fighting without disabling depth test.

---

## 6. Wire-Up

- **Init.** `Mod.Init` after `AddComponent<VoxelFrameDriver>`: `WorldUIRenderer.EnsureCreated()`. Skips if `!WorldspaceUI || !IsWorld3D`.
- **World end.** Postfix on `MapBox.addClearWorld` → `WorldUIRenderer.OnWorldUnload()`.
- **Nameplate.** `General.cs:421` `Text3D.Prefix`: prepend `if (Core.savedSettings.WorldspaceUI) return false;`.
- **Selection.** Postfix on `SelectionManager.selectUnit` → `SelectionRing.Show`. Postfix on `deselectUnit`/`deselectAll` → `Hide`.
- **Damage popup.** Postfix on `Actor.takeDamage` (decompile for exact name): `DamagePopup.Spawn(...)`.
- **Per-frame.** `VoxelFrameDriver.LateUpdate` (`VoxelRender.cs:185`): add `WorldUIRenderer.Instance?.LateUpdate()`. No second driver.

---

## 7. Risks

1. **Nameplate legibility at small zoom.** 12pt world-canvas maps to 8-12 screen pixels at close zoom on 1080p. Tune `pixelsPerUnit` up to 200 if blurry.
2. **Selection ring z-fighting with terrain.** `Offset -1, -1` is primary fix. High `TileHeight` may exceed offset budget — scale the `0.005f` lift constant by `TileHeight`. Don't use `ZTest Always` (rings would draw through buildings).
3. **100+ damage popups.** Pool capped at 64; overflow stops oldest active and recycles. All 64 share one `RenderMode.WorldSpace` canvas → one dirty event per frame regardless of active count.

---

## 8. Build Sequence (one PR)

1. `worldspace: WorldUIRenderer skeleton + rig tracking` — no visuals; verify no crash at 500 actors.
2. `worldspace: NameplateWorld TMP label + distance fade` — overrides `Text3D` Prefix.
3. `worldspace: HealthBar shader + quad + hp refresh`.
4. `worldspace: SelectionRing torus + scroll shader` — Show/Hide hooks.
5. `worldspace: DamagePopup pool + Actor.takeDamage Postfix`.
6. `worldspace: NameplateSoft depth fade shader`.
7. `worldspace: flip WorldspaceUI=true; update phase table + HANDOFF`.

---

## 9. Files

**New:**
- `WorldSphereMod/Code/Worldspace/{WorldUIRenderer,NameplateWorld,SelectionRing,DamagePopup}.cs`
- `WorldSphereMod/Resources/Shaders/{NameplateSoft,HealthBar,SelectionRing}.shader`

**Modify:**
- `WorldSphereMod/Code/Mod.cs` — `EnsureCreated` after `VoxelFrameDriver` registration.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs:185` — `VoxelFrameDriver.LateUpdate` adds the call.
- `WorldSphereMod/Code/General.cs:421` — `Text3D.Prefix` early-return on flag.
- `WorldSphereMod/Code/QuantumSprites.cs:188` — `drawSelectedUnits` Prefix delegates to `SelectionRing.UpdateAll`.
- `WorldSphereMod/Code/WorldSphereTab.cs` — `WorldspaceUI` toggle button.

No changes to `SavedSettings.cs` (`WorldspaceUI = false` already), `MeshInstanceBatcher.cs`, or Phase 1-6 files.

---

## Key references

- `WorldSphereMod/Code/General.cs:421-432` — `Text3D` Prefix.
- `WorldSphereMod/Code/QuantumSprites.cs:188-192` — `drawSelectedUnits` Prefix.
- `WorldSphereMod/Code/Voxel/VoxelRender.cs:183-191` — `VoxelFrameDriver` pattern.
- `WorldSphereMod/Code/3DCamera.cs:267` — `RotateCamera.Rotation` (HP bar yaw).
- `WorldSphereMod/Code/SavedSettings.cs:38-39` — `WorldspaceUI` flag.
