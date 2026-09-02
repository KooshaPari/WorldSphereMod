# WorldSphereMod Runtime Verification

Machine-first verification of the live mod — no human eyes, no Unity Editor GUI.

## Method

The mod exposes a BridgeServer HTTP RPC on `127.0.0.1:8766` with endpoints
that allow enabling phases and reading telemetry at runtime.

### Enable a phase at runtime

```
POST /settings/{key}?value=true
```

This routes through `UpdateSettingQueued` → sets the field → calls
`Core.ApplyPhaseToggle(fieldName, phaseValue)` on the main thread → saves
settings → invalidates voxel cache when relevant.

### Read live state

```
GET /health      → { ok, isWorld3D, version, ... }
GET /settings    → full runtime saved-settings map
GET /telemetry   → renderFoundation (terrain/actor mesh, shaders, sun) +
                   actorDrawCumulative, lastNonZeroDrawCalls, drawCalls
```

## Verified state (2026-09-02, HEAD 21a5d4a, v2.0.0-beta.11)

22 feature/phase flags enabled at runtime without crash:

| Metric | Value |
|---|---|
| isWorld3D | true |
| terrainShader | WSM3D/OpaqueVertexColor |
| terrainMeshVertCount | 66,049 |
| actorMeshVertCount | 648 |
| actorDrawCumulative | 2,027,714 |
| lastNonZeroDrawCalls | 20 |
| sun (directional + trilight) | present |

## Pixel-level confirmation (screenshot histogram)

Full-screen capture via Win32 `PrintWindow(PW_RENDERFULLCONTENT)`:

- 10,359 unique colors (rich 3D scene, not blank)
- 20,839 px of (78,72,255) + 11,805 px of (64,61,255) — ocean/water
- ~15,000 px light-blue/white band — clouds / ice caps
- center pixel (164,255,255) cyan — shallow water / biome terrain
- 3 corners (0,0,0) — deep-space skybox background

Visual signature: a 3D Earth-like planet (blue water wrapping a sphere,
cyan terrain, clouds) rendered against black space.

## Notes

- `lastNonZeroDrawCalls` rose 11 → 20 after enabling phases: per-frame
  draw count increased ~2× with features on.
- The compile-time default flags remain OFF by design (spec-enforced,
  test-verified via `SavedSettingsTests`). Runtime toggling is the
  supported path for end-user feature enablement.
- `actorVerts` fluctuates 152 → 648 → 784 → 648 across sessions; this is
  the embedded procedural mesh being rebuilt with different subdivision
  based on loaded state, not a regression.