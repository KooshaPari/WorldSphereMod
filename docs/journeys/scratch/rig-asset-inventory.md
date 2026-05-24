# Rig asset inventory

Source: [`Constants.ResolveActorRig`](../../../WorldSphereMod/Code/Constants.cs) reads from
[`ActorRigTypes`](../../../WorldSphereMod/Code/Constants.cs) and falls back to
`RigType.Humanoid` when `assetId` is not present.

## Static assetId -> RigType mapping

| assetId      | RigType |
|--------------|---------|
| archer       | Humanoid |
| bear         | Quadruped |
| crabzilla    | None |
| dragon       | None |
| elf          | Humanoid |
| human        | Humanoid |
| mage         | Humanoid |
| orc          | Humanoid |
| sand_spider  | None |
| snake        | Snake |
| swordsman    | Humanoid |
| villager     | Humanoid |
| wolf         | Quadruped |

## Resolver default

`ResolveActorRig` returns `RigType.Humanoid` when `assetId` has no registry entry and is not a vehicle prefix match. Vehicle asset IDs (boat, ship, car, etc.) return `RigType.None` via `VehicleShapeHints.IsVehicleAssetId`.

## Rig types present in mapping

- `Humanoid` (6)
- `Quadruped` (2)
- `Snake` (1)
- `None` (3)
