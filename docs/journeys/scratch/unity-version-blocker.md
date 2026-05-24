# Asset Bundle Unity Version Blocker

## Root cause confirmed 2026-05-22

WorldBox runtime: **Unity 2022.3.54f1** (from `worldbox_Data/Managed/UnityEngine.dll`)

Available locally:
- Unity 2021.3.45f1 ❌ too old
- Unity 6.3.11f1 ❌ too new

Bundles built with either version produce NML "Failed to load asset bundle"
on every launch → shaders never register → Voxel material falls back to
Standard → black-render cascade.

## Fix

Install Unity 2022.3 LTS (closest available — 2022.3.54f1 or any 2022.3.x)
via Unity Hub. Then re-bake with [`Tools/bake-shaders.ps1`](../../../Tools/bake-shaders.ps1).

## Actionable checklist

Use this order; each step is verifiable before moving on.

- [ ] **Install Unity 2022.3 LTS** via Unity Hub (match game: `2022.3.54f1` or any `2022.3.x` patch).
- [ ] **Point the bake project at 2022.3** — edit `Tools/Unity-Bake-Project/ProjectSettings/ProjectVersion.txt` so `m_EditorVersion` is `2022.3.<patch>f1` (open once in Hub if Unity rewrites settings).
- [ ] **Run the bake script** — auto-detect scans Unity Hub for `2022.3.*` editors; if none are installed, the script exits with next steps and requires `-UnityExe`:
  ```powershell
  pwsh Tools/bake-shaders.ps1
  # or, when Hub has no 2022.3 on PATH:
  pwsh Tools/bake-shaders.ps1 -UnityExe "$env:ProgramFiles\Unity\Hub\Editor\2022.3.54f1\Editor\Unity.exe"
  ```
- [ ] **Heed ProjectVersion warnings** — if `Tools/Unity-Bake-Project/ProjectSettings/ProjectVersion.txt` is not `2022.3.*`, open the bake project once in Hub 2022.3 before shipping bundles.
- [ ] **Confirm bake log** — tail `Tools/bake-shaders.log`; exit code 0 and `[WSM3D-Bake]` success lines.
- [ ] **Confirm bundle output** — `WorldSphereMod/AssetBundles/**/worldsphere` updated (script prints paths + byte sizes).
- [ ] **Install mod + launch WorldBox** — no NML "Failed to load asset bundle" on startup.
- [ ] **Confirm shader load in game log** — see success criteria below.

Integration tests assert the bake **infrastructure** is present (`Tools/bake-shaders.ps1`, `Tools/Unity-Bake-Project/`) and that the script auto-detects `2022.3.*`, validates `ProjectVersion.txt`, and prints next steps when Unity is missing; they do not run Unity headless (CI has no editor).

## Bake script reference

| Item | Path |
|------|------|
| Headless bake entrypoint | [`Tools/bake-shaders.ps1`](../../../Tools/bake-shaders.ps1) (auto-detect `2022.3.*`; `-UnityExe` when missing) |
| Unity project (batchmode target) | `Tools/Unity-Bake-Project/` (`ProjectVersion.txt` should be `2022.3.*`) |
| Editor bake method | `BakeShaders.BakeAll` in `Tools/Unity-Bake-Project/Assets/Editor/BakeShaders.cs` |
| Shader sources copied at bake | `WorldSphereMod/AssetBundles/Shaders/*.shader` |
| Bake log | `Tools/bake-shaders.log` |

Workarounds (uncompressed bundles, strict mode) did **not** overcome the version barrier.

## Status

- 7 shader sources committed in WorldSphereMod/AssetBundles/Shaders/
- BakeShaders.cs ready to bake when correct Unity version present
- All runtime code (Core.LoadAssets shader force-load, VoxelRender shader
  candidates, MountainSlopeSmoothing chain) wired for WSM3D/* shader names
- Single blocker: bake project needs Unity 2022.3 LTS to produce compatible
  bundle binaries

After Unity 2022.3 install + re-bake, expect:
- '[WSM3D] Loaded shader from bundle: WSM3D/OpaqueVertexColor -> ...'
- 'Voxel material resolved via inline WSM3D/OpaqueVertexColor'
- Voxels render with vertex colors instead of Standard-lit-black
- Magenta MountainSlope tris fixed
- HDR skybox CubemapLighting connects to ProceduralSky.shader

## Related docs

- Phase 5 prep (Compound-Spheres submodule + same Unity 2022.3 requirement): [`docs/phase5-prep.md`](../../phase5-prep.md)
