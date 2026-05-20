# Phase 6 Rig Variety Spec

Scope: `WorldSphereMod/Code/Voxel/VoxelRender.cs`, `WorldSphereMod/Code/Rig/BoneDefinition.cs`, `WorldSphereMod/Code/Constants.cs`, and the current WorldBox actor catalog decompile.

## 1) Confirmed asset IDs and coverage

The current publicized actor library confirms these relevant IDs:

- `dragon` and `crabzilla` are special assets with explicit prefab IDs (`C:\Users\koosh\AppData\Local\Temp\wsm_decomp_actor_assets\ActorAssetLibrary.decompiled.cs:235-236`, `:405-406`).
- `snake`, `wolf`, and `bear` are normal actor clones in the catalog (`...ActorAssetLibrary.decompiled.cs:1654`, `:1747`, `:1788`).
- `sand_spider` is present and should be treated as rigless/static unless a dedicated insect rig lands later (`...ActorAssetLibrary.decompiled.cs:834-836`).
- `fire_elemental_horse` exists, but it is not a normal horse asset and should not be used as proof of a general horse rig (`...ActorAssetLibrary.decompiled.cs:5420-5424`).

What I did not find in the current publicized catalog scan: a literal `eagle`, `lion`, `tiger`, `boar`, or `deer` asset ID. That means the spec should not hardcode those IDs yet; they need a separate catalog pass if those assets exist in another branch/build.

Current quantification from the scan: 2 confirmed quadrupeds (`wolf`, `bear`), 1 snake, 1 spider, 2 special boss rigs (`dragon`, `crabzilla`), and 0 confirmed bird IDs.

## 2) Rig families beyond Humanoid

`WorldSphereMod/Code/Rig/BoneDefinition.cs:14` currently only defines `None, Humanoid, Quadruped`. Phase 6 variety should expand the family set to:

- `Quadruped` for wolf/bear and any confirmed hoofed/panther-like animals once their IDs are known.
- `Bird` for airborne birds such as eagles, if/when a literal asset ID is confirmed.
- `Snake` for snake-shaped bodies with no legs.
- `Insect` for spiders/insects, with `sand_spider` as the first obvious target.

Keep `dragon` and `crabzilla` out of the generic enum path. They stay hand-rigged specials with dedicated rigs, not registry-driven generic skeletons.

## 3) Registry pattern

`WorldSphereMod/Code/Voxel/VoxelRender.cs:404-410` is the current hardcoded Humanoid fallback. Replace that with a registry-driven resolver:

- Add `Constants.ActorRigTypes` as a `Dictionary<string, RigType>` registry in `Constants.cs`.
- Seed it with explicit overrides for non-humanoids and specials.
- Make `ResolveRigType(assetId)` do a `TryGetValue`; if the asset is absent, fall back to `RigType.Humanoid`.
- Reserve explicit `RigType.None` entries for assets that must remain static/rag-doll-free.
- Keep `RegisterActorRig(string assetId, RigType rig)` as the extension point for future content mods.

This gives a safe default-Humanoid posture while still allowing explicit `None`/`Snake`/`Bird`/`Insect` entries to opt out of humanoid skinning.

## 4) Cull-lift + skeletal animation interaction

`WorldSphereMod/Code/Voxel/VoxelRender.cs:308-336` already performs cull-lift before visibility and LOD selection, then chooses the skeletal path only for visible, non-impostor actors. The rig-variety change should preserve that ordering:

- Keep the frustum test on the lifted position.
- Resolve rig type after visibility/LOD passes, not before.
- Pass the lifted world position unchanged into `RigDriver.SubmitSkinnedActor()`.
- Let `RigDriver` handle only skinning and mesh submission; it should not mutate world transform or perform a second lift.
- If `tier == Impostor`, skip skeletal work entirely and keep the billboard path unchanged.

Net effect: rig variety changes which skeleton is chosen, not how culling or tile-height lifting behaves.
