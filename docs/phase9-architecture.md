# Phase 9 — Particles, Decals, Post-Processing

Source: design pass by `feature-dev:code-architect` (agent run 2026-05-17).
Historical design-state snapshot; use `docs/HANDOFF.md` for current defaults.

---

## 1. Module Layout

Four files under `WorldSphereMod/Code/Fx/` + the unified BRP post stack in `WorldSphereMod/Code/PostFx/`.

- **`ParticleEffectLibrary.cs`** — data layer. `ParticleBurst` record, per-effect `ParticleSystem` prefab table, VFX Graph runtime capability probe. No Harmony / no Effects.cs deps.
- **`DecalPool.cs`** — lifecycle layer. Three `DecalProjector` sub-pools (Footprint, Scorch, Blood), TTL expiry, `Tick()`.
- **`WSM3DPostStack.cs`** — BRP post-FX layer at `WorldSphereMod/Code/PostFx/WSM3DPostStack.cs`. Owns SSAO, SSGI, bloom, ACES, and LUT in a single deterministic `OnRenderImage` ping-pong chain, destroys its created materials and temp RTs on teardown, and removes legacy `ScreenSpaceAO` / `ScreenSpaceGI` / `ColorGradingLUT` camera components on attach. Bloom and ACES are implemented by the shipped BRP shaders in `WorldSphereMod/Resources/Shaders/BrpBloom.shader` and `WorldSphereMod/Resources/Shaders/BrpACES.shader`.
- **`EffectPatches9.cs`** — integration shim. All Harmony patches for this phase live here.
- **`FxFrameDriver.cs`** — `MonoBehaviour`, drains pools in `LateUpdate`.

Namespace `WorldSphereMod.Fx`.

---

## 2. Public Type Signatures

```csharp
enum BurstShape { Sphere, Ring, Cone }

record ParticleBurst(string EffectId, int Count, float Speed, float Lifetime,
                    float Size, BurstShape Shape, Color TintA, Color TintB);

static class ParticleEffectLibrary
{
    static bool VfxGraphAvailable { get; }
    static void Init();
    static void Fire(string effectId, Vector3 worldPos, float scale);
    static void Clear();
}

enum DecalChannel { Footprint, Scorch, Blood }

static class DecalPool
{
    static void Init(Transform parent);
    static void Emit(DecalChannel channel, Vector3 worldPos, Quaternion rot, float ttl);
    static void Tick();
    static void Clear();
}

sealed class WSM3DPostStack : MonoBehaviour
{
    static void EnsureCreated();
    static void Destroy();
    static void ApplySetting(bool enabled);
    static void RefreshMaterials();
}
```

---

## 3. Particle Burst Pipeline

**Capability probe.** `Init()` tries `Type.GetType("UnityEngine.VFX.VisualEffect, Unity.VisualEffectGraph")`. If found + a `.vfx` asset is in the bundle, `VfxGraphAvailable = true`. Otherwise `ParticleSystem` mesh-render mode. Probe is runtime-only; assembly compiles cleanly without the VFX Graph package.

**ParticleSystem path (primary).** Pool of 16 `ParticleSystem`s. `Fire(effectId, pos, scale)`:
1. Look up `ParticleBurst` for effect ID; bail if missing.
2. Acquire a pooled system; bail if pool empty (drop-on-overflow, no frame spike).
3. Configure via `EmitParams`: position, color (lerp `TintA→TintB` per particle), `size * scale`, lifetime.
4. Renderer in `RenderMode.Mesh` with a 2×2 voxel cube mesh cached at Init via `SpriteVoxelizer`.
5. `Emit(count)`. Auto-returns to pool on `isAlive == false` in `Tick()`.

**Sprite suppression.** `EffectPatches9` sets `effect.sprite_renderer.enabled = false` when `Fire` succeeded. `BaseEffect.deactivate` Postfix re-enables when effect is reset.

**Initial burst table:**

| Effect ID | Count | Shape | Lifetime |
|---|---|---|---|
| `fx_meteorite` | 24 | Cone | 0.6 s |
| `fx_explosion_wave` | 40 | Ring | 0.4 s |
| `fx_fire_smoke` | 20 | Sphere | 1.2 s |
| `fx_antimatter_effect` | 32 | Sphere | 0.8 s |
| `fx_napalm_flash` | 28 | Ring | 0.3 s |

---

## 4. Decal Pool

**Three sub-pools.** `Footprint` (32, square, infinite TTL while unit alive). `Scorch` (16, round, TTL 30s). `Blood` (32, round, TTL 20s). All `DecalProjector`s parented to one Pool GameObject under `Sphere.Manager.transform`.

**Acquire/return.** `Emit(channel, pos, rot, ttl)` dequeues from the sub-pool's `Queue<DecalProjector>`. Empty → drop. Sets `enabled = true`, position, `rot = Tools.GetRotation(...)` for slope alignment, TTL timestamp.

**TTL expiry.** `Tick()` iterates the per-channel active list `List<(DecalProjector, float expiry)>`. Expired → disable + re-enqueue. Called from `FxFrameDriver.LateUpdate`.

**Footprint lifecycle.** Actor-pinned (not TTL). `EffectPatches9` Postfix on `ActorManager.precalculateRenderDataParallel` per visible actor: `DecalPool.UpdateFootprint(actorId, pos, rot)` via a `Dictionary<int, DecalProjector>`. Released in `Actor.kill` / `Actor.remove` Postfix.

---

## 5. Post-FX Stack

**Create.** Postfix on `Sphere.Begin`: attach `WSM3DPostStack` to `CameraManager.MainCamera`. The stack removes legacy `ScreenSpaceAO`, `ScreenSpaceGI`, and `ColorGradingLUT` components so there is only one `OnRenderImage` owner.

**Camera.** `OnRenderImage` runs a deterministic SSAO -> SSGI -> Bloom -> ACES -> LUT chain with a ping-pong pair for intermediate blits. `ApplySetting(bool)` toggles live without reload; sub-pass flags call `RefreshMaterials()` so shader/material availability follows settings. `BloomEnabled` defaults false and `ACESTonemapping` defaults true, so the shipped baseline still has ACES filmic tonemapping even when bloom is opt-in. The BRP shaders live in `WorldSphereMod/Resources/Shaders/` as `BrpBloom.shader` and `BrpACES.shader`.

**Gate.** `EnsureCreated()` early-returns if `!PostFX`. The component tears down owned materials and temp render textures when disabled or destroyed.

---

## 6. Wire-Up

- **Driver.** `Mod.Init` adds `FxFrameDriver` via the same `AddComponent` guard pattern as `VoxelFrameDriver` (`Mod.cs:37-39`). `LateUpdate` calls `DecalPool.Tick()` + pool-reclaim for `ParticleEffectLibrary`.
- **World lifecycle.** `EffectPatches9` Postfixes on `Sphere.Begin` (→ `DecalPool.Init`, `ParticleEffectLibrary.Init`, `WSM3DPostStack.EnsureCreated`) and `Sphere.Finish` (→ `Clear`/`Destroy`). All gated `Core.IsWorld3D`.
- **Effect spawn.** Postfix on `BaseEffectController.GetObject` (`Effects.cs:182-204`): when `ParticleEffects && effectId in table`, `Fire` + suppress sprite.
- **Decal hooks.** Postfix on `ExplosionFlash.start` → `Emit(Scorch, …, 30f)`. Postfix on `Actor.takeDamage` → `Emit(Blood, …, 20f)`. Footprint emit in the `ActorManager` Postfix.

---

## 7. Risks

1. **VFX Graph availability.** URP decal output for VFX Graph is roadmap; Unity 2022.3 LTS ships without. Probe + `ParticleSystem` fallback covers this. VFX Graph assets can be added later without code change.
2. **Decal pool perf on low-end GPUs.** 80 active projectors in dense combat may surface overdraw. Mitigations: hard pool cap with drop policy; `renderingLayerMask` restricting decals to terrain; profile before default-on.
3. **Post-FX cost on integrated GPUs.** Bloom downsample/upsample pyramid plus ACES/LUT compositing ~2-3 ms on UHD 620 at 1080p. `PostFX` defaults true in current Phase 9 settings, with `SSGIEnabled` still default-off, `BloomEnabled` default-off, and `ACESTonemapping` default-on (`SavedSettings.cs`). Phase 10 can still gate behind GPU tier check.
4. **`UniversalAdditionalCameraData` null.** Use `GetUniversalAdditionalCameraData()` extension (auto-creates) not `GetComponent<>` (may be null).

---

## 8. Build Sequence (one PR, 8 atomic commits)

1. `fx: ParticleEffectLibrary — pool + burst table` — data layer only.
2. `fx: DecalPool — three sub-pools, Tick() expiry` — smoke-test via debug key.
3. `fx: WSM3DPostStack — unified BRP post stack + camera enable` — editor-visible, lives in `WorldSphereMod/Code/PostFx/`.
4. `fx: FxFrameDriver MonoBehaviour; Sphere.Begin/Finish lifecycle wire`.
5. `fx: EffectPatches9 — 5 effect IDs route to ParticleEffectLibrary.Fire; sprite suppression` — visual test of fx_explosion_wave.
6. `fx: scorch + blood decal hooks (ExplosionFlash + Actor.takeDamage)`.
7. `fx: footprint decal hook in ActorManager Postfix`.
8. `fx: flip ParticleEffects=true (PostFX stays default-false); phase table + HANDOFF`.

---

## 9. Files

**New:**
- `WorldSphereMod/Code/Fx/{ParticleEffectLibrary,DecalPool,PostFxController,EffectPatches9,FxFrameDriver}.cs`
- `WorldSphereMod/Code/PostFx/WSM3DPostStack.cs`

**Modify:**
- `WorldSphereMod/Resources/Shaders/BrpBloom.shader` — BRP bloom chain used by `WSM3DPostStack`.
- `WorldSphereMod/Resources/Shaders/BrpACES.shader` — BRP ACES tonemapper used by `WSM3DPostStack`.
- `WorldSphereMod/Code/Mod.cs:37-39` — add `AddComponent<FxFrameDriver>` guard.
- `WorldSphereMod/Code/Effects.cs:258-271` — `BaseEffect.deactivate` Postfix: re-enable sprite when ParticleEffects and effect-id in table. One line.
- `WorldSphereMod/Code/SavedSettings.cs` — add `public bool ParticleEffects = false;`. (`PostFX` at line 44 already present.)

No changes to `Constants.cs`, `MeshInstanceBatcher.cs`, or Phase 1-8 files.

---

## Key references

- `WorldSphereMod/Code/Constants.cs:20-30` — `EffectDatas` table; the five target effect IDs.
- `WorldSphereMod/Code/Effects.cs:182-204` — `BaseEffectController.GetObject` Postfix (burst dispatch).
- `WorldSphereMod/Code/Effects.cs:258-271` — `BaseEffect.deactivate` Postfix (sprite re-enable).
- `WorldSphereMod/Code/Effects.cs:219-224` — `ExplosionFlash.start` (scorch decal).
- `WorldSphereMod/Code/3DCamera.cs:107` — `CameraController.MainCamera`.
- `WorldSphereMod/Code/Mod.cs:37-39` — `AddComponent` guard pattern.
- `WorldSphereMod/Code/SavedSettings.cs:44` — `PostFX` flag.
- `WorldSphereMod/Code/Voxel/SpriteVoxelizer.cs` — reused for particle cube mesh.
