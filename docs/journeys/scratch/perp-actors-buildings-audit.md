# Perp Actors / Buildings Audit

Scope: `Constants.PerpActors` and `Constants.PerpBuildings` in [`WorldSphereMod/Code/Constants.cs`](../../../WorldSphereMod/Code/Constants.cs). I checked the code that consumes them, plus the local installed mod copy and bundle manifest.

## Findings

1. `PerpActors` is currently empty in source, so there are no repo-defined actor opt-outs to audit for staleness. Its only consumers are the voxel actor gate (`VoxelRender.ActorVoxelEmit`) and the generic `IsUpright(Actor)` helper (`Tools.cs`), while the public API only exposes `MakeActorPerp` for external mods to add entries at runtime ([`Constants.cs:31`](../../../WorldSphereMod/Code/Constants.cs#L31), [`VoxelRender.cs:301-305`](../../../WorldSphereMod/Code/Voxel/VoxelRender.cs#L301-L305), [`Tools.cs:161-163`](../../../WorldSphereMod/Code/Tools.cs#L161-L163), [`WorldSphereAPI.cs:20-25`](../../../WorldSphereMod/Code/WorldSphereAPI.cs#L20-L25)).

2. `PerpBuildings` has exactly three hardcoded entries, all stockpile variants: `stockpile_acidproof`, `stockpile_fireproof`, and `stockpile` ([`Core.cs:88-90`](../../../WorldSphereMod/Code/Core.cs#L88-L90)). Those are still consistent with the stockpile-specific render path in `QuantumSprites.cs` (`draw_building_stockpiles` / `is_stockpile`) and the Phase 1 smoke test note that stockpiles should remain flat billboards ([`QuantumSprites.cs:121-136`](../../../WorldSphereMod/Code/QuantumSprites.cs#L121-L136), [`QuantumSprites.cs:661-667`](../../../WorldSphereMod/Code/QuantumSprites.cs#L661-L667), [`docs/smoke-test-phase1.md:46-50`](../../smoke-test-phase1.md#L46-L50)). I did not find an obvious stale stockpile ID in source.

3. No obvious Phase 9 omission belongs in `PerpActors` / `PerpBuildings`. Phase 9 effect IDs are handled separately through `EffectData` and `ParticleEffectLibrary` (`fx_meteorite`, `fx_fire_smoke`, `fx_antimatter_effect`, `fx_napalm_flash`, `fx_boulder`, `fx_explosion_wave`, `fx_tile_effect`, `fx_cloud`), and the `BaseEffectController.GetObject` patch suppresses the sprite after a particle fire succeeds ([`Constants.cs:20-29`](../../../WorldSphereMod/Code/Constants.cs#L20-L29), [`Effects.cs:202-212`](../../../WorldSphereMod/Code/Effects.cs#L202-L212), [`docs/phase9-architecture.md:71-80`](../../phase9-architecture.md#L71-L80), [`docs/phase9-architecture.md:110-113`](../../phase9-architecture.md#L110-L113)). Those should not be added to the perp building/actor lists.

4. Adjacent context: the projectile billboard exception already exists for `arrow`, but it lives in `PerpProjectiles`, not the actor/building lists requested here ([`Core.cs:91`](../../../WorldSphereMod/Code/Core.cs#L91), [`QuantumSprites.cs:161-169`](../../../WorldSphereMod/Code/QuantumSprites.cs#L161-L169)).

## Bundle Check

I could inspect the installed mod copy, but the local `worldsphere.manifest` only enumerates shader/mesh/material assets and does not expose any actor/building asset ID list to diff against ([`worldsphere.manifest:22-27`](../../../WorldSphereMod/AssetBundles/win/worldsphere.manifest#L22-L27)). So I could not prove bundle-level staleness or completeness beyond the source/runtime checks above.

## Bottom Line

No stale actor/building perp IDs were evident in the repo. The only hardcoded building opt-outs still look intentional, and Phase 9 effect IDs are already routed through the effect table rather than these lists. The main gap is that `PerpActors` is empty, so any ground-aligned actor decals still need to be added through `WorldSphereModAPI.MakeActorPerp` if/when they exist.
