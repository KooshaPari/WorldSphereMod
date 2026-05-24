# Phase 9b - Voxel Mesh Particle Bursts

Scope: replace the current 3D-sprite routing in `Effects.cs` with actual voxel-mesh bursts for the effect events that already act like transient VFX. This is a design note only; no implementation here.

## 1) Effect events that should become voxel bursts

- `Meteorite.spawnOn` and `ExplosionFlash.start` are already patched through `BasePatch`, which currently just calls `SetEffect3D` and keeps them as positioned sprites. Those are the right burst entry points for fire/explosion-style one-shots. `Effects.cs:217` `Effects.cs:228` `Effects.cs:234`
- `StatusParticle.spawnParticle` is currently a pure sprite setup path: it prepares the effect and colors the sprite, then exits early. That makes it the matching hook for smoke/cloud/status bursts. `Effects.cs:337`
- The existing Phase 9 runtime already treats `ParticleEffects` as a dispatch table keyed by effect ID, with sprite suppression after a successful fire. That is the behavior to replace, not extend. `Effects.cs:182` `Effects.cs:202` `docs/phase9-architecture.md:112`

## 2) Reuse `VoxelMeshCache.Get` on the effect sprite

- The reuse point is the effect's source `Sprite`, exactly as the voxel renderer already does for actors: `Mesh m = VoxelMeshCache.Get(sp);`. `VoxelRender.cs:376` `VoxelRender.cs:506`
- The cache API itself is sprite-keyed and returns `null` for missing/empty meshes, so the burst path should take the effect's `sprite_renderer.sprite` and ask the cache for a mesh once per burst sprite. `VoxelMeshCache.cs:52` `VoxelMeshCache.cs:74`
- The doc example already uses the same pattern from a sprite-producing object: `VoxelMeshCache.Get(a.calculateMainSprite())`. `Voxel\README.md:20`

## 3) Burst lifecycle

- Treat each burst as a short-lived instance with three phases: spawn, growth, fade.
- Spawn: create the burst from the effect event, position it from the effect transform, and fetch the voxel mesh from the sprite cache.
- Growth: scale up quickly from near-zero to the target size so the burst reads as an emission, not a static mesh replacement.
- Fade: reduce alpha and/or scale near the end of lifetime, then disable and return the burst to the pool or destroy queue.
- The current Phase 9 architecture already expects burst-style pool behavior and TTL-driven reclaim for transient VFX, so the lifecycle should follow that model instead of the always-on `BaseEffect` sprite update path. `docs/phase9-architecture.md:62` `docs/phase9-architecture.md:83`

## 4) Interaction with Phase 5 shadows

- Phase 5 shadow support is tied to `SpriteShadow.LateUpdate` and reads `sprRndCaster.sprite` / `sprRndCaster.color` from the caster sprite. `Effects.cs:291` `Effects.cs:121`
- If a burst replaces the visible sprite with a voxel mesh, the shadow path should still have a valid caster to read from, or it should be explicitly disabled for that burst so `UpdateShadow` does not chase a hidden/pooled sprite. `Effects.cs:123` `Effects.cs:133`
- The safe rule is: voxel bursts may change the render primitive, but they should not break the existing `SpriteShadow` contract. If a burst has no meaningful caster sprite, opt out of shadow rendering for that burst instead of letting Phase 5 infer one from a pooled object. `Effects.cs:291` `Effects.cs:298`

## Summary

Phase 9b should move the three effect hooks above from "3D sprite positioned in world space" to "voxel mesh burst emitted from the effect sprite, cached through `VoxelMeshCache.Get`, animated by spawn/grow/fade, and shadow-safe." `Effects.cs:228` `Effects.cs:234` `Effects.cs:337`
