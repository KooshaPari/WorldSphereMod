---
name: wsm3d
description: Use when working on WorldSphereMod3D fork — building, installing, running, debugging the WorldBox mod, or validating a Phase via render journeys. Centralizes dev-loop commands, log diagnostics, and known pitfalls.
---

# WSM3D dev loop

## When to invoke
- A task explicitly names "dev-loop", "build", "install", "test", or "validate Phase X" for WorldSphereMod3D
- You need to build the mod DLL, install it to WorldBox, run the game, or diagnose a render failure
- You are debugging a shader, frustum cull, or material issue in the 3D conversion

## Repo facts
- **Repo path:** `E:/Dev/WorldSphereMod`
- **Game install:** `C:/Program Files (x86)/Steam/steamapps/common/worldbox`
- **Mod destination:** `<install>/Mods/WorldSphereMod3D`
- **Player.log:** `C:/Users/koosh/AppData/LocalLow/mkarpenko/WorldBox/Player.log` (cleared on game launch)
- **SavedSettings JSON:** `%USERPROFILE%/AppData/Roaming/<NML path>/WorldSphereMod.json`
- **Target:** net48; WorldBox runs Mono 6.12.x (no C# 9+ features)
- **NML workflow:** Roslyn compile at startup; compile failure = silent skip + retry on reload (no DLL written)
- **AssetBundle reality:** worldsphere render path is driven by bundle + bridge behavior, not legacy CompoundSpheres assumptions.
- **Git branch:** `wip/208-*` lineage (`wip/208-ovc-good-bundle`, integ/live-fixes descendants)

## The CLI

| Command | Purpose | Example |
|---------|---------|---------|
| `build` | Compile Code/*.cs via csproj to bin/Release/*.dll | `cd E:/Dev/WorldSphereMod && dotnet build -c Release` |
| `install` | Run install.ps1 to copy DLL + assets to mod folder | `& "E:/Dev/WorldSphereMod/install.ps1"` |
| `launch` | Launch WorldBox directly (triggers NML compile + load) | `& "C:/Program Files (x86)/Steam/steamapps/common/WorldBox/worldbox.exe"` |
| `tail-log` | Stream Player.log for [WSM3D] tags or compile errors | `Get-Content -Path "$env:USERPROFILE/AppData/LocalLow/mkarpenko/WorldBox/Player.log" -Wait -Tail 50` |
| `journey` | Verify a render validation journey (Tools/wsm3d.ps1) | `& "E:/Dev/WorldSphereMod/Tools/wsm3d.ps1" journey verify -Id us-wsm-phase-1-voxel-actors` |
| `live-verify` | Offline CI gate: dotnet test + all journey mock verifies | `pwsh Tools/wsm-live-verify.ps1` → `Tools/.reports/live-verify-latest.json` |

## Render bundle reality (2026-06, hard-won)

- Two AssetBundles are the production render reality now:
  - `worldsphere` (~12KB): `CompoundSphereMaterial` + `CompoundSphereMesh` + `SkyBox` + `OpaqueVertexColor`
  - `wsm3d-shaders`: the shader bundle
- The variant-strip pass can generate 80-byte `STUB` shaders in standalone PLAYER. Those can abort with an uncatchable native `ManagedStream` abort even though editor validation looks clean. This is a false positive pattern: editor recompiles from source, PLAYER loads compiled variants.
- `Core.cs` enforces startup safety using two gates: `ShaderBundleAvailable` and `PostFxShaderBundleAvailable`, plus a `SafeShaders[]` allowlist.

## Bridge HTTP API (127.0.0.1:8766, only listens once a 3D world is loaded — NOT at main menu)

- `GET /health` (`bridgeAlive`)
- `GET /world/state` (`isWorld3D`)
- `GET /diag/full_dump`
- `GET /diag/render_stats`
- `POST /actions/screenshot` (`{ mode: "camera", path }`) — `camera` mode bypasses debug-console overlay
- `POST /actions/generate_world`
- `POST /actions/spawn_units` (`{ count }`)

## Launch (the catch-22)

Launch via executable path first:
`C:/Program Files (x86)/Steam/steamapps/common/WorldBox/worldbox.exe`

`steam://rungameid/1206560` is flaky. The bridge does not come up at main menu, so save-load/world-state checks require entering a world first.

## Terrain color lesson

`Sphere.PrepareWorld` kept `WorldPrepared` set across transitions, which could leave `BaseLayers` empty and make `GetBaseColor` return white. Fixed in `Sphere.ResetPrepared()` hooks on save-load + new-world flow (commit `4bf1c236`).

## Batchmode bake

Use this for shader baking when GUI ILPP runner stalls:
`E:/Unity/Hub/Editor/2022.3.60f1/Editor/Unity.exe -batchmode -nographics -quit -projectPath E:/Dev/WorldSphereMod/Tools/Unity-Bake-Project -executeMethod BakeShaders.BakeAll -logFile <log>`

Kill leftover `bee_backend`/`Unity.ILPP.Runner` first if needed.

## Common workflows

### Pre-merge validation (all phases, offline)

Matches `/wsm-validate-all`, `task live-verify`, and `live-verify-gate.yml`:

```pwsh
cd E:/Dev/WorldSphereMod
pwsh Tools/wsm-live-verify.ps1
```

Stages: (1) `dotnet test` on `tests/WorldSphereMod.Tests.{Unit,Integration,E2E}`,
(2) `Tools/verify-journeys.ps1` (`phenotype-journey verify --mock` for every manifest),
(3) live PlayCUA/SSIM **skipped** unless you pass `-Live`.

Individual stages when debugging:

```pwsh
task test-all                                    # dotnet only
pwsh Tools/verify-journeys.ps1                   # all journey mocks
pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-2-mesh-buildings
```

Phase IDs: `docs/journeys/manifests/index.json` (`us-wsm-phase-1-voxel-actors` … `us-wsm-phase-10-lod-impostor`).

### Verify a Phase toggle works in-game
1. Ensure the Phase toggle is wired in `WorldSphereTab.cs` CreateButtons pattern
2. Build: `dotnet build -c Release`
3. Install: `& install.ps1`
4. Launch: `& "C:/Program Files (x86)/Steam/steamapps/common/WorldBox/worldbox.exe"`
5. In-game, open WorldSphere tab; toggle the Phase
6. Tail log: `Select-String -Path "$env:USERPROFILE/AppData/LocalLow/mkarpenko/WorldBox/Player.log" -Pattern "\[WSM3D\]" | Select-Object -Last 20`
7. Confirm Phase render call fired and no shader errors logged

### Diagnose "mod load failed" or silent NML skip
```pwsh
# Check if NML compiled our mod this run
Select-String -Path "$env:USERPROFILE/AppData/LocalLow/mkarpenko/WorldBox/Player.log" `
  -Pattern "Compile Mod WorldSphereMod3D|Failed to compile mod WorldSphereMod3D" | `
  Select-Object LineNumber, Line

# Extract all WSM3D-tagged logs
Select-String -Path "$env:USERPROFILE/AppData/LocalLow/mkarpenko/WorldBox/Player.log" `
  -Pattern "\[WSM3D\]" | `
  Select-Object LineNumber, Line

# Look for shader resolution errors
Select-String -Path "$env:USERPROFILE/AppData/LocalLow/mkarpenko/WorldBox/Player.log" `
  -Pattern "Shader.*not found|enableInstancing.*failed" | `
  Select-Object LineNumber, Line
```

### Add a new Phase toggle
1. **Code logic:** Add enum variant to `Phases.cs`
2. **UI button:** In `WorldSphereTab.cs` CreateButtons, follow the existing pattern:
   ```csharp
   if (UI.Button($"Phase {phase.Name}"))
   {
       Log("[WSM3D] Toggling Phase " + phase.Name);
       phase.enabled = !phase.enabled;
       // Trigger render re-eval
   }
   ```
3. **Render check:** Ensure `VoxelRender.cs` Postfix respects the phase's enabled flag before setting `has_normal_render[i] = false`
4. Build, install, test in-game

### Verify a Phenotype journey for Phase validation
1. Manifests: `docs/journeys/manifests/us-wsm-phase-<N>-*/manifest.json` (indexed in `index.json`)
2. Single phase (mock, default):
   ```pwsh
   pwsh Tools/wsm3d.ps1 journey verify -Id us-wsm-phase-1-voxel-actors
   ```
   Or: `phenotype-journey verify docs/journeys/manifests/us-wsm-phase-1-voxel-actors/manifest.json --mock`
3. All phases offline: `pwsh Tools/wsm-live-verify.ps1` or `pwsh Tools/verify-journeys.ps1`
4. Add `-Live` on `journey verify` or `wsm-live-verify.ps1 -Live` for bridge/PlayCUA/SSIM (see `docs/live-verification.md`)
5. Capture without verify: `pwsh Tools/wsm3d.ps1 journey capture -Id <id>`
6. Screenshots land under journey manifest dirs / `Tools/wsm3d-capture` output; cross-check `Player.log` for `[WSM3D]` and NML compile lines when validating in-game

## Pitfalls (real, observed)

**Asset bundle shader reality (2026-06):** the `worldsphere` + `wsm3d-shaders` bundles and their strip/compatibility constraints are current truth. Keep bundle changes aligned with `Core.cs` gates (`ShaderBundleAvailable`, `PostFxShaderBundleAvailable`) and `SafeShaders[]` allowlist.

**Material.enableInstancing = true is silent-fail on unsupported shaders.** Setting the flag does not error if the shader lacks the instancing variant; always read back `material.enableInstancing` in Player.log to confirm it took effect, or the impostor will render as solid white.

**VoxelRender Postfix only hides vanilla sprite if impostor drew successfully.** The line `has_normal_render[i] = false` (in the Postfix) only executes after a successful impostor render. If impostor render failed, vanilla sprite remains visible and you see z-fighting or duplicate geometry.

**Frustum cull over-broad hide.** Early frustum tests may discard entire voxel clusters that are partially on-screen. Use a conservative cull margin or disable cull during Phase validation.

**NML compile is silent on failure.** A compile error (e.g., missing using statement, syntax error) will not be logged as "[WSM3D]" error; instead, the mod DLL is not written and NML retries on next reload. Always grep for "Failed to compile mod" in Player.log.

**github-pages env branch policy.** If deploying docs to GitHub Pages, the branch policy may reject pushes to main; use `git push origin main:gh-pages` or configure the branch in Settings > Pages.

**Vercel CLI 47+ required for install.ps1 env inject.** Older versions do not support the `--env` flag. Run `npm install -g vercel@latest` before invoking install.ps1 if you see "Unknown argument" errors.
