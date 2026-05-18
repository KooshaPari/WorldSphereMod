# Handoff — pick up locally

State of the world for a fresh Claude Code instance (or human) resuming on a
local machine with a real Unity / WorldBox install.

## Where things are

| Thing | Location |
|---|---|
| Active branch | `claude/research-ultraplan-fork-DdgI5` |
| Open draft PR | https://github.com/KooshaPari/WorldSphereMod/pull/1 |
| HEAD commit | `4b3bd36 Phase 1: wire voxel pipeline into actor render pass` |
| Upstream remote | `origin` → `KooshaPari/WorldSphereMod` (a fork of `MelvinShwuaner/WorldSphereMod`) |
| Full plan | `docs/PLAN.md` |
| Voxel module | `WorldSphereMod/Code/Voxel/` (Phase 1) |
| Build portability | `Directory.Build.props` (uses `WORLDBOX_PATH` env var) |
| CI | `.github/workflows/build.yml` (API build only — mod build is best-effort) |

## CI status as of pause

All 5 checks green on `4b3bd36`:
- ✅ `dotnet-build` (×2 workflows, both green)
- ✅ Socket Security: PR Alerts
- ✅ Socket Security: Project Report
- ✅ semgrep-cloud-platform/scan

PR is still **draft**. CodeRabbit auto-skipped (draft); flip to "ready for review"
when Phase 1 has been validated locally if you want CodeRabbit's pass.

## What's blocked here, unblocked locally

Everything that needs a real Unity 2022.3 install or a running WorldBox copy.
Specifically:

1. **Compile the full mod.** The cloud CI builds only `WorldSphereAPI.csproj`
   because it can't get at WorldBox's reference DLLs. Locally:
   ```bash
   export WORLDBOX_PATH="/path/to/worldbox"   # folder containing worldbox_Data/
   dotnet build WorldSphereMod.csproj -c Release
   ```
   If the build fails, the most likely cause is a field name on
   `ActorManager.render_data` that differs from what I assumed in
   `Voxel/VoxelRender.cs` (`positions`, `rotations`, `scales`, `colors`,
   `flip_x_states`, `has_normal_render`, `main_sprites`). Inspect the upstream
   assembly with dnSpy/ILSpy if names changed; the names I used match the
   v1.1.0 source per `QuantumSprites.cs:491-499`.

2. **In-game smoke test Phase 1.** With voxel rendering still gated OFF by
   default (`SavedSettings.VoxelEntities = false`):
   - Install the built mod into `…/NeoModLoader/Mods/WorldSphereMod3D/` next
     to upstream WorldSphereMod (different GUID, they can coexist).
   - Launch, generate a small map, ensure terrain still renders correctly
     (regression check on Phase 0 / fork plumbing).
   - Open settings → toggle "Voxel Entities" on.
   - Verify actors render as 3D voxel meshes from any camera angle.
   - Expect: unlit appearance (the Phase 1 placeholder material is unlit).
     Lighting + shadows arrive in Phase 5.

3. **Capture before/after screenshots** for the PR. Drop them in
   `docs/screenshots/phase-1-{before,after}.png` and link from the PR body.

## Recommended next steps in priority order

1. **Validate the Phase 1 build compiles locally** (1 hour).
   Resolve any field-name mismatches in `Voxel/VoxelRender.cs`. Commit the
   fix. Push.

2. **Smoke test voxel actors** (1–2 hours).
   Flip the toggle, run, screenshot. If the placeholder shader looks ugly
   but functional, that's expected — call it done for Phase 1.

3. **Ship the real `VoxelLit.shader`** (half a day).
   Cut a tiny Unity 2022.3 builder project under `External/AssetBundleBuilder/`,
   author a lit, instanced, vertex-color shader, bake it into the existing
   `worldsphere` AssetBundle. Replace `VoxelRender.EnsureMaterial`'s
   `Shader.Find` fallback with `AssetBundleUtils.GetAssetBundle("worldsphere")
   .GetObject<Material>(...)`. Default `VoxelEntities = true` once it looks
   right. This is gentle preparation for Phase 5.

4. **Voxel buildings** (a few hours).
   `BuildingManager.render_data` field names need verification in Unity
   before re-enabling the building voxel emit (I deleted it; see
   `Voxel/VoxelRender.cs` for the previous shape). Once verified, paste it
   back and gate on `SavedSettings.ProceduralBuildings == false &&
   SavedSettings.VoxelEntities == true` so Phase 2 procgen will cleanly
   override it later.

5. **Start Phase 2 (procedural buildings)** per `docs/PLAN.md`. Each phase
   is intended to ship as its own PR.

## Open design questions to revisit on next session

- Greedy meshing in `SpriteVoxelizer.Build`: current implementation emits one
  quad per opaque texel, no merging of coplanar faces. Vertex count for a
  16×16 sprite is ~3072. Adding a greedy merge would cut that ~5×. Probably
  worth doing once Phase 5's shader is in.
- Per-instance color via `_InstanceColor`: declared in `MeshInstanceBatcher`
  but the placeholder shaders won't read it. The real Phase 5 shader needs
  to honor it.
- Skeletal animation (Phase 6) will replace the static voxel mesh per actor
  with bone-skinned variants. Keep the cache key as `(Sprite.GetInstanceID,
  depth)` — when rigging is introduced, switch to `(SpriteId, rigId)`.

## To re-subscribe to the PR

If a local instance wants the same auto-investigate-CI-events behavior:
the cloud session unsubscribed before pausing, so resubscribe explicitly
via the GitHub MCP tool `subscribe_pr_activity` for
`kooshapari/worldspheremod#1`.

## Don't forget

- `claude/research-ultraplan-fork-DdgI5` is the dev branch. Push to it,
  not to `main`.
- Keep commits one-phase-at-a-time so the PR stays reviewable.
- Don't bump the GUID in `mod.json` casually — it's how this fork stays
  co-installable with upstream.
