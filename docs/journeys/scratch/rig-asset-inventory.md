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
| eagle        | Bird |
| elf          | Humanoid |
| human        | Humanoid |
| mage         | Humanoid |
| orc          | Humanoid |
| sand_spider  | Insect |
| snake        | Snake |
| spider       | Insect |
| swordsman    | Humanoid |
| villager     | Humanoid |
| wolf         | Quadruped |

## Resolver default

`ResolveActorRig` returns `RigType.Humanoid` when `assetId` is null/empty or has no mapping.

## Rig types present in mapping

- `Humanoid` (6)
- `Quadruped` (2)
- `Bird` (1)
- `Snake` (1)
- `Insect` (2)
- `None` (2)
