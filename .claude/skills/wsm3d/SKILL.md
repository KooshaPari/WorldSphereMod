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
- **Repo path:** `C:/Users/koosh/Dev/WorldSphereMod`
- **Game install:** `C:/Program Files (x86)/Steam/steamapps/common/worldbox`
- **Mod destination:** `<install>/Mods/WorldSphereMod3D`
- **Player.log:** `C:/Users/koosh/AppData/LocalLow/mkarpenko/WorldBox/Player.log` (cleared on game launch)
- **SavedSettings JSON:** `%USERPROFILE%/AppData/Roaming/<NML path>/WorldSphereMod.json`
- **Target:** net5.0; WorldBox runs Mono 6.12.x (no C# 9+ features)
- **NML workflow:** Roslyn compile at startup; compile failure = silent skip + retry on reload (no DLL written)
- **CompoundSpheres.dll:** 23.5 KB runtime dependency at `<mod>/Assemblies/CompoundSpheres.dll`; install.ps1 excludes it to avoid CS1705 (Mono unloadable), ships source instead
- **Git branch:** Hard fork of WorldSphereMod; PR #1 tracks KooshaPari/WorldSphereMod

## The CLI

| Command | Purpose | Example |
|---------|---------|---------|
| `build` | Compile Code/*.cs via csproj to bin/Release/*.dll | `cd C:/Users/koosh/Dev/WorldSphereMod && dotnet build -c Release` |
| `install` | Run install.ps1 to copy DLL + assets to mod folder | `& "C:/Users/koosh/Dev/WorldSphereMod/install.ps1"` |
| `launch` | Steam launch WorldBox (triggers NML compile + load) | `Start-Process "steam://rungameid/1206560"` |
| `tail-log` | Stream Player.log for [WSM3D] tags or compile errors | `Get-Content -Path "$env:USERPROFILE/AppData/LocalLow/mkarpenko/WorldBox/Player.log" -Wait -Tail 50` |
| `journey` | Run a render validation journey (Tools/wsm3d.ps1) | `& "C:/Users/koosh/Dev/WorldSphereMod/Tools/wsm3d.ps1" -Journey "phase_1_voxel"` |

## Common workflows

### Verify a Phase toggle works in-game
1. Ensure the Phase toggle is wired in `WorldSphereTab.cs` CreateButtons pattern
2. Build: `dotnet build -c Release`
3. Install: `& install.ps1`
4. Launch: `Start-Process "steam://rungameid/1206560"`
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

### Run a Phenotype journey for Phase validation
1. Journey manifests live in `docs/journeys/manifests/` (YAML)
2. Invoke via Tools/wsm3d.ps1:
   ```pwsh
   & "C:/Users/koosh/Dev/WorldSphereMod/Tools/wsm3d.ps1" `
     -Journey "phase_N_validation" `
     -GamePath "C:/Program Files (x86)/Steam/steamapps/common/worldbox" `
     -OutputDir "C:/tmp/wsm3d-renders"
   ```
3. Check output screenshots in `C:/tmp/wsm3d-renders/` for visual correctness
4. Cross-reference Player.log for compile + render errors during the journey run

## Pitfalls (real, observed)

**CompoundSpheres.dll causes CS1705 under Mono.** WorldBox runs Mono 6.12, which cannot load a net5.0 assembly. install.ps1 excludes the DLL and ships source code instead; if you manually copy the DLL, NML compile will fail silently and the mod will not load.

**Material.enableInstancing = true is silent-fail on unsupported shaders.** Setting the flag does not error if the shader lacks the instancing variant; always read back `material.enableInstancing` in Player.log to confirm it took effect, or the impostor will render as solid white.

**VoxelRender Postfix only hides vanilla sprite if impostor drew successfully.** The line `has_normal_render[i] = false` (in the Postfix) only executes after a successful impostor render. If impostor render failed, vanilla sprite remains visible and you see z-fighting or duplicate geometry.

**Frustum cull over-broad hide.** Early frustum tests may discard entire voxel clusters that are partially on-screen. Use a conservative cull margin or disable cull during Phase validation.

**NML compile is silent on failure.** A compile error (e.g., missing using statement, syntax error) will not be logged as "[WSM3D]" error; instead, the mod DLL is not written and NML retries on next reload. Always grep for "Failed to compile mod" in Player.log.

**github-pages env branch policy.** If deploying docs to GitHub Pages, the branch policy may reject pushes to main; use `git push origin main:gh-pages` or configure the branch in Settings > Pages.

**Vercel CLI 47+ required for install.ps1 env inject.** Older versions do not support the `--env` flag. Run `npm install -g vercel@latest` before invoking install.ps1 if you see "Unknown argument" errors.
