# Constants Mutability Audit

Scope: [`WorldSphereMod/Code/Constants.cs`](../../../WorldSphereMod/Code/Constants.cs). I checked all `Constants.X` reads/writes in `WorldSphereMod/Code` and the Harmony Postfix paths that consume them.

## Mutable vs immutable

- Immutable already:
  - `ZDisplacement`, `HalfRoot`, `TileHeightDiffSpeed`, and `SpecialHeight` are `const` and have no runtime mutation path ([`Constants.cs:9`](../../../WorldSphereMod/Code/Constants.cs#L9), [`Constants.cs:13`](../../../WorldSphereMod/Code/Constants.cs#L13), [`Constants.cs:15`](../../../WorldSphereMod/Code/Constants.cs#L15), [`Constants.cs:34`](../../../WorldSphereMod/Code/Constants.cs#L34)).
  - `ConstRot`, `ToUpright`, and `FromUpright` are already `readonly` and only read at call sites ([`Constants.cs:17`](../../../WorldSphereMod/Code/Constants.cs#L17), [`Constants.cs:18`](../../../WorldSphereMod/Code/Constants.cs#L18), [`Constants.cs:19`](../../../WorldSphereMod/Code/Constants.cs#L19), [`3DCamera.cs:132`](../../../WorldSphereMod/Code/3DCamera.cs#L132), [`QuantumSprites.cs:26`](../../../WorldSphereMod/Code/QuantumSprites.cs#L26), [`QuantumSprites.cs:216`](../../../WorldSphereMod/Code/QuantumSprites.cs#L216)).

- Mutable by design:
  - `EffectDatas`, `PerpActors`, `PerpBuildings`, and `PerpProjectiles` are `readonly` references to mutable `ConcurrentDictionary` instances. They are populated at startup in `Core.DoSomeOtherStuff()` and extended through the public API (`Make*Perp`, `EditEffect`) at runtime ([`Constants.cs:20`](../../../WorldSphereMod/Code/Constants.cs#L20), [`Constants.cs:31`](../../../WorldSphereMod/Code/Constants.cs#L31), [`Constants.cs:32`](../../../WorldSphereMod/Code/Constants.cs#L32), [`Constants.cs:33`](../../../WorldSphereMod/Code/Constants.cs#L33), [`Core.cs:88`](../../../WorldSphereMod/Code/Core.cs#L88), [`Core.cs:89`](../../../WorldSphereMod/Code/Core.cs#L89), [`Core.cs:90`](../../../WorldSphereMod/Code/Core.cs#L90), [`Core.cs:91`](../../../WorldSphereMod/Code/Core.cs#L91), [`WorldSphereAPI.cs:20`](../../../WorldSphereMod/Code/WorldSphereAPI.cs#L20), [`WorldSphereAPI.cs:24`](../../../WorldSphereMod/Code/WorldSphereAPI.cs#L24), [`WorldSphereAPI.cs:30`](../../../WorldSphereMod/Code/WorldSphereAPI.cs#L30), [`WorldSphereAPI.cs:36`](../../../WorldSphereMod/Code/WorldSphereAPI.cs#L36), [`WorldSphereAPI.cs:46`](../../../WorldSphereMod/Code/WorldSphereAPI.cs#L46)).
  - `Zero` is the only non-`readonly` field that appears effectively immutable in practice, but it is passed by `ref` to `ForceRotation` (`sprite.ForceRotation(ref Constants.Zero)`), so making it `readonly` requires changing that API first ([`Constants.cs:37`](../../../WorldSphereMod/Code/Constants.cs#L37), [`QuantumSprites.cs:26`](../../../WorldSphereMod/Code/QuantumSprites.cs#L26), [`QuantumSprites.cs:216`](../../../WorldSphereMod/Code/QuantumSprites.cs#L216)).

## Thread-safety risk

- The render-data Postfixes read the shared registries while the public API can mutate them. The hot paths are `ActorManager.precalculateRenderDataParallel` and `BuildingManager.precalculateRenderDataParallel`, which read `PerpActors` / `PerpBuildings` on parallel render passes ([`VoxelRender.cs:285`](../../../WorldSphereMod/Code/Voxel/VoxelRender.cs#L285), [`VoxelRender.cs:441`](../../../WorldSphereMod/Code/Voxel/VoxelRender.cs#L441), [`BuildingProcRender.cs:15`](../../../WorldSphereMod/Code/ProcGen/BuildingProcRender.cs#L15)).
- `ConcurrentDictionary` makes the individual `ContainsKey`/`Add` operations safe, so there is no immediate data-race crash. The remaining risk is semantic, not structural: different Postfixes can observe different membership within the same frame if another thread adds an entry mid-flight.

## Recommendation

- Keep the dictionaries mutable, but treat them as runtime registries.
- Make `Zero` `readonly` only after replacing the `ref`-based `ForceRotation` call pattern with by-value or `in` parameters.
- No other `Constants.X` fields need a `readonly` change; the rest are already `const`/`readonly` or are mutable registries by design.
