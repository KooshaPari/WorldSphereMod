# Asset Bundle Unity Version Blocker

## Root cause confirmed 2026-05-28 (supersedes 2026-05-22 note)

WorldBox runtime: **Unity 2022.3.60f1** — read authoritatively from
`worldbox_Data/globalgamemanagers` (`m_UnityVersion`). WorldBox has been
patched since the 2026-05-22 reading of 2022.3.54f1; always re-read the live
`globalgamemanagers` before baking.

The PostFX empty-name failure is NOT a 2022-vs-6.x mismatch and NOT a shader
compile error (the bake log shows all 10 shaders compile cleanly with valid
names). It is an **exact-patch** mismatch: the shipped bundle was baked with
**2022.3.62f3** while WorldBox runs **2022.3.60f1**. Unity's `SerializedShader`
binary layout is not stable across 2022.3 patch releases, so a 62f3-baked
Shader asset deserializes at 60f1 runtime with an empty `.name` and trips the
native ManagedStream crash ("Mismatched serialization in builtin class
'Shader'. Read 80 bytes but expected 4936 bytes") — see ADR-0013. This is why
`SafeShaders` whitelists only OpaqueVertexColor.

Available locally (2026-05-28):
- Unity 2021.3.45f1 ❌ too old
- Unity 2022.3.62f3 ❌ wrong patch (2 releases ahead of 60f1 — causes empty-name)
- Unity 6000.3.11f1 ❌ too new

The needed editor **2022.3.60f1** is NOT installed. Headless Hub install
(`--headless install --version 2022.3.60f1 --changeset 5f63fdee6d95`) was
attempted but the ~5GB download stalls/aborts in the agent sandbox (electron
GPU-process crashes). Install must be completed interactively or in a stable
shell, then rebake.

## Fix

Install Unity **2022.3.60f1** (changeset `5f63fdee6d95`) via Unity Hub — the
EXACT WorldBox runtime build, not "any 2022.3.x". Then re-bake with
[`Tools/bake-shaders.ps1`](../../../Tools/bake-shaders.ps1). The bake script
now auto-prefers 2022.3.60f1 and `BakeShaders.cs` self-verifies every shader's
`.name`/`.isSupported` against the just-built win bundle, failing the bake if
any name comes back empty.

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
