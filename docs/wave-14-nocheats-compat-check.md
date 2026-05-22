# WSM3D vs NoCheats compatibility check (static analysis)

Scope: `Code/Core.cs` patch registration and static WSM3D patch surface overlap risk with a typical NoCheats workflow (`god`/sandbox-ish behavior).

## 1) PatchAll filter behavior in `Core.Patch()`

`Core.Patch()` applies patches in two stages:

1. **Assembly scan + phase gate + class-based `PatchAll`**
   - Iterates all types in the assembly.
   - `if type has [Phase]` then reads corresponding bool from `SavedSettings` and skips when disabled.
   - Applies `Patcher.CreateClassProcessor(type).Patch()` only when type also has `[HarmonyPatch]`.
   - This means phase-gated classes with no `[HarmonyPatch]` are not patching via this scan.

2. **Explicit `PatchAll` calls**
   - `WorldSphereMod.Bridge.BridgePerFrameTick`
   - `SphereControl`
   - `Dist3D`
   - `EffectPatches`
   - `MovementEnhancement`
   - `Drop3D`
   - `FixCrabzilla`
   - `AddLayers`
   - `QuantumSpritePatches`
   - `WorldLoop`
   - `SourcePatches`

3. **Manual individual patches/transpilers** in `Core.Patch()`
   - `GeneratorTool.getTile` ← `WorldLoop.Tiles`
   - `MapBox.GetTile` ← `WorldLoop.Tiles`
   - `PlayerControl.clickedStart` transpiler (`Lerp3D.Transpiler`)
   - `MapAction.applyTileDamage` transpiler
   - `MapBox.loopWithBrush(...)` overload transpilers
   - `BehWormDigEat.loopWithBrush` transpiler
   - `MapBox.loopWithBrushPowerForDropsRandom` transpiler
   - `BaseEffect.prepare(...)` postfix (`EffectPatches.BasePatch`)
   - Map rendering/debug transpilers on `DebugLayer*`, `MapLayer`, `ZoneCalculator`, etc.
   - `Actor.updateMovement`, `Actor.tryToAttack`, `MapBox.checkAttackFor`, `Actor.updatePossessedMovementTowards`, `CombatActionLibrary.getAttackTargetPosition`, `MusicBoxContainerTiles.calculatePan` (`Move3D.Transpiler`)
   - `PreviewHelper.convertMapToTexture`, `PreviewHelper.getCurrentWorldPreview` prefix+postfix (`PreviewPatch`)
   - `MoveCamera.zoomToBounds`, `MoveCamera.updateMobileCamera` transpilers
   - `HeatRayEffect.update` transpiler (`DisableSettingPositions.Transpiler`)
   - `MapBox`/`DebugLayer`/`ZoneCalculator`/`WorldLayer*`/`Tilemap`/`QuantumSprite`/`GroupSpriteObject` convertors and layer rewrites.

## 2) NoCheats compatibility: patch-surface overlap risk

### No obvious direct overlap with obvious NoCheats domains
No direct references to the following are present in `Core.Patch()` surface:
- `Power`/`GodPower` state toggles
- explicit sandbox mode check methods
- resource checks (`canAfford`, `spend`, `inventory`, `money`, `mana`, etc.)
- explicit god-mode command handlers

### Likely collision candidates
- **`Actor.die`**
  - WSM3D has a `HarmonyPatch(typeof(Actor), "die")` in `Fx\Environmental.cs` (via `SourcePatches`/`PatchAll` class).
  - If NoCheats patches `Actor.die` for immortality/skip-death behavior, this is the highest direct collision risk.
- **Combat and attack-related methods**
  - `Actor.tryToAttack`, `MapBox.checkAttackFor`, `CombatActionLibrary.getAttackTargetPosition` are patched in `General.cs`.
  - If NoCheats rewrites combat resolve/attack eligibility, prefix/transpiler order may conflict.
- **World interaction/editing methods**
  - `MapAction.applyTileDamage`, `MapBox.loopWithBrush*`, `BehWormDigEat.loopWithBrush`, `MapBox.loopWithBrushPowerForDropsRandom`.
  - If NoCheats patches tile-write/damage logic, behavior can be order-sensitive (though not a guaranteed conflict).

### Low-likelihood/noise-sensitive collisions
- Most other WSM3D patched areas are render/camera/math conversion (`MoveCamera`, `MapTileToSphere`, `TileBox`/`ZoneCamera`, sprite conversion, map redraw, layer queueing).
- These are generally orthogonal to NoCheats-style gameplay-gating patches.

## 3) Practical risk posture

- **Current static verdict:** _likely compatible by construction_, with **one explicit high-risk overlap zone** (`Actor.die`) and a few **medium-risk overlap zones** in combat/tile mutation methods.
- Since no NoCheats source was in-repo, this remains a **method-surface compatibility assessment**, not a definitive runtime proof.

## 4) Suggested verification

1. Run both mods and inspect harmony patch logs for duplicate patch errors.
2. Test in-game: god mode invulnerability + death prevention, sandbox actions, and damage application after enabling NoCheats.
3. If instability appears, move to explicit load-order pinning or conditional patches around `Actor.die`/combat entrypoints.
