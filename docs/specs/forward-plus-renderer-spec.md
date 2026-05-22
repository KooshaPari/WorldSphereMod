# WSM3D Forward+ Hand-Roll Renderer — Full Spec (SOTA, 2026)

> **Status:** Tier 5 design — 1-2 week effort, last-resort if AssetBundle
> shader bake (Tier 1) + custom RenderFeature (Tier 2) don't close visual
> gaps. Documented now so a focused session can execute.

## Goal

Bypass WorldBox's vanilla render submission for WSM3D-owned entities (voxel
actors / buildings / foliage / water / impostors) by intercepting Unity's
render loop with a `CommandBuffer` injected via `Camera.AddCommandBuffer`.
Render those entities with our own forward+ light-loop, depth-prepass, and
post-FX chain — independent of the Standard shader limitations we keep
hitting.

## Why Forward+ over alternatives

| Approach | Pros | Cons | Verdict |
|---|---|---|---|
| Deferred (G-buffer) | Many lights | Stripped Unity 2022 BRP doesn't expose deferred targets; transparent objects bad | reject |
| Forward (per-object lighting) | Simple, supported | Light count limited to 8/object; can't scale to dynamic torches | reject |
| **Forward+ (tile-based light culling)** | 256+ lights, transparent works | More compute; CPU-side light list per tile | **chosen** |
| Cluster Forward | Best perf for many lights | Volume-grid culling expensive | overkill for scene scale |
| Visibility Buffer (VBuffer) | SOTA 2025 (UE5 Nanite-style) | Compute-heavy; Unity 2022 BRP lacks plumbing | future-version |

SOTA reference: AMD GPUOpen FidelityFX Forward+ (2024); Activision-Blizzard
talk "Practical Clustered Shading" (Siggraph 2017); Unity HDRP source
(tile-based light loop).

## Architecture

```
┌──────────────────────────────┐
│  WorldBox vanilla Update()   │
└──────────┬───────────────────┘
           │ Postfix on
           │ ActorManager.precalculateRenderDataParallel
           ▼
┌──────────────────────────────────────────────────────┐
│  WSM3DRenderer (MonoBehaviour root, DontDestroyOnLoad)│
│                                                       │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐ │
│  │ Light cull   │  │ Mesh submit  │  │ Post-FX chain│ │
│  │ tile pass    │  │ + depth      │  │ (LUT/SSAO/   │ │
│  │ (16×16 tiles)│  │ prepass      │  │  bloom/sky)  │ │
│  └──────────────┘  └──────────────┘  └──────────────┘ │
│         │                  │                  │       │
│         └───────────┬──────┴──────────────────┘       │
│                     ▼                                 │
│        CommandBuffer "WSM3D.Forward+"                 │
│        injected at CameraEvent.BeforeImageEffects     │
└──────────────────────────────────────────────────────┘
```

Components:

1. **`WSM3DRenderer` MonoBehaviour** — root, persistent, owns the
   `CommandBuffer` lifecycle. Attached via `Mod.PostInit`.
2. **Light tile cull pass** — compute shader (or CPU fallback) culls dynamic
   point lights against 16×16 screen tiles, produces tile-light-index buffer.
3. **Depth prepass** — render WSM3D-owned meshes to a depth-only target so
   the main color pass can do early-Z reject + SSAO/SSGI can sample our
   geometry.
4. **Color pass** — render WSM3D meshes with our custom `Forward+VertexColor`
   shader that reads the tile-light buffer + applies up to 256 lights.
5. **Post-FX chain** — apply LUT, SSAO, bloom, ACES tonemap via blits at
   `BeforeImageEffects` event.
6. **Composite back** — final blit copies our render target into the camera's
   color target so WorldBox UI / vanilla sprites render on top.

## Spec details

### 1. CommandBuffer lifecycle

```csharp
public sealed class WSM3DRenderer : MonoBehaviour
{
    CommandBuffer _cb;
    Camera _mainCam;
    int _depthRT, _colorRT, _aoRT;

    void OnEnable()
    {
        _mainCam = CameraManager.MainCamera ?? Camera.main;
        _cb = new CommandBuffer { name = "WSM3D.Forward+" };
        _depthRT = Shader.PropertyToID("_WSM3D_DepthRT");
        _colorRT = Shader.PropertyToID("_WSM3D_ColorRT");
        _aoRT    = Shader.PropertyToID("_WSM3D_AORT");
        _mainCam.AddCommandBuffer(CameraEvent.BeforeImageEffects, _cb);
    }

    void OnDisable()
    {
        if (_mainCam != null && _cb != null)
            _mainCam.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _cb);
        _cb?.Release();
    }

    void LateUpdate()
    {
        _cb.Clear();
        AllocateTargets();
        DepthPrepass();
        TileLightCull();
        ColorPass();
        PostFXChain();
        Composite();
    }
}
```

### 2. Tile light culling

16×16 screen-tile grid. Each tile gets a list of overlapping point lights.
For a 1920×1080 screen → 120×68 = 8160 tiles. Per-tile light cap = 32.

Compute shader path (if GPU supports CS5.0):

```hlsl
[numthreads(16, 16, 1)]
void CullLights(uint3 id : SV_GroupID)
{
    uint tileIdx = id.x + id.y * _TileCountX;
    AABB tileFrustum = ComputeTileFrustum(id.xy, _DepthMinMax[tileIdx]);
    uint count = 0;
    [loop] for (uint i = 0; i < _LightCount && count < 32; i++)
    {
        if (SphereOverlapsAABB(_Lights[i], tileFrustum))
            _TileLights[tileIdx * 32 + count++] = i;
    }
    _TileLightCount[tileIdx] = count;
}
```

CPU fallback when compute unavailable: same logic on managed C# thread,
double-buffered to avoid main-thread stall.

### 3. Depth prepass

Render only depth, no color, for all WSM3D meshes. Output to `_depthRT`
(R32_SFloat). Used by:
- Color pass for early-Z reject
- SSAO pass for occlusion sampling
- SSGI for ray-march termination

```csharp
_cb.SetRenderTarget(_depthRT);
_cb.ClearRenderTarget(true, false, default, 1.0f);
foreach (var bucket in MeshInstanceBatcher.Buckets)
    _cb.DrawMeshInstanced(bucket.Mesh, 0, _depthOnlyMaterial,
                          0, bucket.Matrices, bucket.Count);
```

### 4. Color pass

The actual lit render. Custom shader reads tile-light-index buffer +
applies forward+ light loop:

```hlsl
fixed3 ShadeFragment(float3 wpos, float3 normal, float3 albedo)
{
    uint2 tile = (uint2)(_ScreenUV * _TileCount);
    uint tileIdx = tile.x + tile.y * _TileCountX;
    uint lightCount = _TileLightCount[tileIdx];
    fixed3 lit = 0;
    [loop] for (uint i = 0; i < lightCount; i++)
    {
        uint lightId = _TileLights[tileIdx * 32 + i];
        Light L = _Lights[lightId];
        float3 toL = L.position - wpos;
        float r2 = dot(toL, toL);
        float att = saturate(1 - r2 / (L.range * L.range));
        att *= att;
        float ndl = saturate(dot(normal, normalize(toL)));
        lit += L.color * L.intensity * ndl * att;
    }
    return albedo * (lit + _AmbientColor.rgb);
}
```

### 5. Post-FX chain

Blit-based passes in order: SSAO → SSGI → Bloom (threshold + blur + composite)
→ ACES tonemap → LUT grade → final.

### 6. Composite

```csharp
_cb.Blit(_colorRT, BuiltinRenderTextureType.CameraTarget);
```

WorldBox's UI canvas renders after `BeforeImageEffects` so it overlays cleanly.

## Performance targets

| Metric | Target | Stretch |
|---|---|---|
| Frame budget (1080p strategy zoom) | < 8 ms | < 4 ms |
| Light count | 256 dynamic | 1024 |
| Tile-cull pass | < 0.5 ms | < 0.1 ms (compute) |
| Depth prepass | < 1 ms | < 0.4 ms |
| Color pass | < 4 ms | < 1.5 ms |
| Post-FX chain | < 2 ms | < 1 ms |

## Required new shaders (bake alongside existing)

- `Forward+VertexColor.shader` — main lit shader with tile-light-loop
- `Forward+DepthOnly.shader` — depth prepass
- `Forward+TileCull.compute` — light culling compute (CS5.0)
- `Forward+Composite.shader` — final blit
- `Bloom.shader` — separable Gaussian + threshold pass

## Risks + mitigations

| Risk | Mitigation |
|---|---|
| Compute shader unsupported on user GPU | CPU fallback for tile cull (already in scaffold) |
| WorldBox UI gets overdrawn | `BeforeImageEffects` event (before UI), not `AfterEverything` |
| Camera scene-transition kills CommandBuffer subscription | `Camera.AddCommandBuffer` is camera-instance scoped; re-attach in `OnEnable` after scene reload |
| Light list overflow (>1024) | Hard cap + cull dimmest first |
| Memory: 4 full-screen RTs at 1080p = ~32 MB | Acceptable; release on scene unload |

## Out-of-scope (for v1 of this spec)

- Shadow maps (use existing built-in cascaded shadows on Standard meshes)
- Soft shadows / PCF
- Hardware ray-traced reflections (Unity 2022 BRP doesn't expose DXR)
- Cluster-forward 3D light grid (Forward+ 2D tile grid sufficient)
- Volumetric fog (deferred to v2)

## Acceptance criteria

- All 5 user-reported render bugs (black actors, oversized voxels, water
  blackworld, dragonfly limbs, 2.5D sprites) verifiably fixed via the new
  shader pipeline alone — no Standard / Sprites/Default fallbacks involved.
- Bridge `/voxel/sprite?name=X` returns correctly-sized + correctly-colored
  meshes.
- Frame budget targets met.
- Existing Harmony Postfix chain unaffected (we only ADD a render path,
  not REPLACE the vanilla one — vanilla still draws UI / cursors / etc).

## Sequencing

1. Land the AssetBundle shader bake (Tier 1) first — gives us the
   `OpaqueVertexColor.shader` infrastructure to test against.
2. Implement `WSM3DRenderer` MonoBehaviour shell (Tier 2 = CommandBuffer
   injection without tile-cull).
3. Wire depth prepass.
4. Wire color pass with single-light Forward (no tile cull).
5. Add tile-cull compute shader.
6. Add post-FX chain.
7. Performance pass + cull-bounds tuning.

References:
- AMD FidelityFX Forward+ (open source, MIT) — https://gpuopen.com/fidelityfx-forwardplus/
- Activision-Blizzard "Practical Clustered Shading" (Siggraph 2017)
- Unity URP Forward+ source (Unity 2023.3 reference)
- "Tiled Forward Shading" (Olsson & Assarsson, 2014)
