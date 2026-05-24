# WSM3D OnRenderImage PostFX RenderFeature — Spec

> **Status:** Tier 2 — 1-2 day effort. Closes the "PostFX phase enabled but
> nothing visible" gap by injecting a custom post-pass chain through
> `Camera.OnRenderImage` instead of relying on URP's `RendererFeature`
> system that WorldBox doesn't expose.

## Implementation status (2026-05-23)

| Spec target | Shipped today | Notes |
|---|---|---|
| `WSM3DPostStack` unified ping-pong chain | **Not shipped** | SSAO / SSGI / LUT are separate `MonoBehaviour` passes on `CameraManager.MainCamera`. |
| SSAO via `OnRenderImage` + `ScreenSpaceAO.shader` | **Shipped** | `WorldSphereMod.PostFx.ScreenSpaceAO` — gated on `SSAOEnabled`, quality from `SSAOQuality`. |
| SSGI via `OnRenderImage` | **Shipped** | `WorldSphereMod.PostFx.ScreenSpaceGI` — attach/detach via `SSGIEnabled`. |
| LUT grade via `OnRenderImage` | **Shipped** | `WorldSphereMod.Lighting.ColorGradingLUT` — `ColorGradingLut` flag. |
| Bloom + ACES in one stack | **Not shipped** | No BRP bloom/ACES materials in repo; chain ends at per-pass blits. |
| `PostFX` master toggle → visible stack | **Partial** | `PostFxController` still targets URP `Volume` + `renderPostProcessing` (types usually absent in WB Managed/). Built-in passes ignore `PostFX` and use their own flags. |

**Runtime wiring today**

- `Core.ApplyPhaseToggle` → `PostFxController.ApplySetting` (`PostFX`), `ScreenSpaceAO.ApplySetting` (`SSAOEnabled`), `ScreenSpaceGI.ApplySetting` (`SSGIEnabled`), `ColorGradingLUT.ApplySetting` (`ColorGradingLut`).
- `VoxelRender.TickPerFrame` reconciles PostFX / SSAO / SSGI on change only; `EnsureCreated` retries late camera bind.
- `Mod` world-init applies persisted SSAO after scene transitions.

**Gap vs this spec:** multiple `OnRenderImage` callbacks run in Unity script order (undefined relative order among SSAO, SSGI, LUT). Tier-2 work is to fold passes into `WSM3DPostStack` with ping-pong RTs and retire URP `PostFxController` for WorldBox.

**E2E guardrails:** `tests/WorldSphereMod.Tests.E2E/SsaoPostFxInvariantsTests.cs`, `OnRenderImagePostFxSpecInvariantsTests.cs`.

## Goal

Implement a working post-FX chain (SSAO → SSGI → Bloom → ACES tonemap → LUT
grade) that runs on WorldBox's existing camera without requiring URP or
the (stripped) PostProcessing v2 package. Uses only `OnRenderImage`
callbacks + the shipped shaders (`ScreenSpaceAO.shader`,
`ColorGradingLUT.shader`).

## Why OnRenderImage

| Approach | Available in WB | Works for our needs |
|---|---|---|
| URP `RendererFeature` | ❌ — WB uses BRP | n/a |
| `PostProcessLayer` (PPv2) | ❌ — assembly not shipped | n/a |
| Unity 2023 Volume framework | ❌ — Unity 2022 | n/a |
| **`Camera.OnRenderImage(src, dst)`** | ✅ — works on every MonoBehaviour | **chosen** |
| Custom CommandBuffer | ✅ — but heavier-weight | reserve for Tier 5 Forward+ |

`OnRenderImage` is the simplest BRP-compatible hook. Unity feeds the camera's
output as the source texture; we apply zero+ passes by `Graphics.Blit` and
write to the destination. Multi-pass chains require ping-pong RTs.

## Architecture

```
Camera.OnRenderImage(src, dst)
        │
        ▼
┌────────────────────────────┐
│ WSM3DPostStack             │
│  - PreCheck flags          │
│  - PingPong(src, dst)      │
│                            │
│  ┌───────────────────────┐ │
│  │ SSAO pass             │ │ (if SSAOEnabled)
│  │  Blit(src, ao,        │ │
│  │       _ssaoMat)       │ │
│  └───────────────────────┘ │
│  ┌───────────────────────┐ │
│  │ SSGI pass             │ │ (if SSGIEnabled)
│  │  Blit(ao, gi,         │ │
│  │       _ssgiMat)       │ │
│  └───────────────────────┘ │
│  ┌───────────────────────┐ │
│  │ Bloom pass            │ │ (if BloomEnabled)
│  │  Threshold + blur     │ │
│  │  + composite          │ │
│  └───────────────────────┘ │
│  ┌───────────────────────┐ │
│  │ ACES tonemap          │ │ (always if PostFX on)
│  └───────────────────────┘ │
│  ┌───────────────────────┐ │
│  │ LUT grade             │ │ (if ColorGradingLUT)
│  │  Blit(prev, dst,      │ │
│  │       _lutMat)        │ │
│  └───────────────────────┘ │
└────────────────────────────┘
```

## Implementation

```csharp
public sealed class WSM3DPostStack : MonoBehaviour
{
    Material _ssaoMat, _ssgiMat, _bloomThreshold, _bloomBlur, _aces, _lutMat;
    RenderTexture _ping, _pong;
    int _bloomDownsample = 4;

    void OnEnable()
    {
        TryCreateMaterials();
    }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (Core.savedSettings == null || !Core.savedSettings.PostFX)
        {
            Graphics.Blit(src, dst);
            return;
        }
        EnsurePingPong(src);
        RenderTexture cur = src, next = _ping;
        if (Core.savedSettings.SSAOEnabled && _ssaoMat != null)
        {
            Graphics.Blit(cur, next, _ssaoMat);
            Swap(ref cur, ref next);
        }
        if (Core.savedSettings.SSGIEnabled && _ssgiMat != null)
        {
            Graphics.Blit(cur, next, _ssgiMat);
            Swap(ref cur, ref next);
        }
        if (Core.savedSettings.BloomEnabled && _bloomThreshold != null)
        {
            Graphics.Blit(cur, next, _bloomThreshold);
            Swap(ref cur, ref next);
        }
        if (_aces != null)
        {
            Graphics.Blit(cur, next, _aces);
            Swap(ref cur, ref next);
        }
        if (Core.savedSettings.ColorGradingLUT && _lutMat != null)
        {
            Graphics.Blit(cur, dst, _lutMat);
        }
        else
        {
            Graphics.Blit(cur, dst);
        }
    }

    void EnsurePingPong(RenderTexture src)
    {
        if (_ping != null && _ping.width == src.width && _ping.height == src.height) return;
        if (_ping != null) RenderTexture.ReleaseTemporary(_ping);
        if (_pong != null) RenderTexture.ReleaseTemporary(_pong);
        _ping = RenderTexture.GetTemporary(src.descriptor);
        _pong = RenderTexture.GetTemporary(src.descriptor);
    }

    static void Swap(ref RenderTexture a, ref RenderTexture b)
    {
        var t = a; a = b; b = t;
    }
}
```

## Acceptance

- `pwsh Tools/wsm3d.ps1 toggle SSAOEnabled true` produces visible darker
  crevices.
- `ColorGradingLUT=true` + a shipped LUT changes scene tonal balance.
- Toggling `PostFX=false` → original camera output, no perf cost beyond
  one `Graphics.Blit`.

## Attachment

`WSM3DPostStack.AddComponent` on `CameraManager.MainCamera` in
`Mod.PostInit`. Re-attach on scene transition (same DontDestroyOnLoad
pattern as bridge).

## Out-of-scope

- Motion blur (needs velocity buffer; defer to Forward+)
- Depth of field (needs CoC compute; defer)
- Temporal anti-aliasing (needs jittered projection; defer)
- HDR bloom intensity > 4× (clamped in ACES; defer)
