# 2026-05-22 Nameplate Offset Diagnostic

Scope: `WorldSphereMod/Code/Worldspace/NameplateWorld.cs` and `WorldSphereMod/Code/Worldspace/WorldUIRenderer.cs`.

## Finding

`WorldUIRenderer.LateUpdate()` already positions the shared rig with the same world transform used by voxel actors: `Tools.To3DTileHeight(a.current_position, kRigLift)` (`WorldUIRenderer.cs:107`).

`NameplateWorld.Attach()` was diverging from that transform by parenting the label under `head_object` when available instead of the shared rig root (`NameplateWorld.cs:28-39`). That meant the nameplate inherited a second, actor-specific offset path instead of the single rig transform, which can push health/name widgets outside the intended map position when the actor sits near world center.

## Change

Aligned `NameplateWorld.Attach()` to always parent the label under `rigRoot`, leaving `head_object` only as an upstream suppression lookup. The label now inherits the same lifted world-space transform as the actor rig.

## Verification

- Confirmed the shared rig path already uses `Tools.To3DTileHeight(a.current_position, kRigLift)`.
- Confirmed the nameplate no longer chooses `head_object` as its parent.
- `dotnet build -c Release` will be used as the post-fix compile gate.
