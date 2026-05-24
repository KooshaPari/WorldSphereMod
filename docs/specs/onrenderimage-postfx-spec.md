# WSM3D OnRenderImage PostFX RenderFeature — Spec

> **Status:** Tier 2 — 1-2 day effort. Closes the "PostFX phase enabled but
> nothing visible" gap by injecting a custom post-pass chain through
> `Camera.OnRenderImage` instead of relying on URP's `RendererFeature`
> system that WorldBox doesn't expose.

## Implementation status (2026-05-24)

| Spec target | Shipped | Notes |
|---|---|---|
| `WSM3DPostStack` unified post stack | **Shipped** | Single `OnRenderImage` callback with deterministic SSAO→SSGI→Bloom→ACES→LUT pass order. |
| SSAO via `OnRenderImage` + `ScreenSpaceAO.shader` | **Shipped** | Kernel/params shared from `ScreenSpaceAO.Kernel` into unified stack. |
| SSGI via `OnRenderImage` | **Shipped** | Kernel/params shared from `ScreenSpaceGI.Kernel` into unified stack. |
| LUT grade via `OnRenderImage` | **Shipped** | LUT texture + shader resolved in stack init. |
| Bloom + ACES in one stack | **Shipped** | BRP shaders: `BrpBloom.shader` (threshold+blur+composite) + `BrpACES.shader` (filmic tonemap). |
| `PostFX` master toggle → visible stack | **Shipped** | `PostFX` flag gates entire `WSM3DPostStack`; sub-passes gated by `SSAOEnabled`, `SSGIEnabled`, `BloomEnabled`, `ACESTonemapping`, `ColorGradingLut`. |

**Runtime wiring**

- `Core.ApplyPhaseToggle` → `WSM3DPostStack.ApplySetting` (`PostFX`), `WSM3DPostStack.RefreshMaterials` (sub-pass toggles).
- `VoxelRender.TickPerFrame` reconciles PostFX / SSAO / SSGI via WSM3DPostStack on change only and no longer recreates legacy `ScreenSpaceAO` / `ScreenSpaceGI` components.
- `Mod` world-init calls `WSM3DPostStack.EnsureCreated()` after scene transitions.
- Legacy `ScreenSpaceAO`, `ScreenSpaceGI`, `ColorGradingLUT` MonoBehaviours auto-removed by `RemoveLegacyPasses()` on stack attach.
- `PostFxController` (URP Volume path) remains for potential URP-capable builds but is no longer the primary runtime.

**E2E guardrails:** `tests/WorldSphereMod.Tests.E2E/SsaoPostFxInvariantsTests.cs`, `OnRenderImagePostFxSpecInvariantsTests.cs`, `VoxelFrameDriverPostFxInvariantsTests.cs`.

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
write to the destination. This stack uses one reusable chain RT plus local
temporary RTs for bloom.

## Architecture

```
Camera.OnRenderImage(src, dst)
        │
        ▼
┌────────────────────────────┐
│ WSM3DPostStack             │
│  - PreCheck flags          │
│  - Chain RT + local temps  │
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
