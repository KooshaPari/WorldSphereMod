# Runtime Water & Slope Investigation — 2026-05-27

Player.log: `C:/Users/koosh/AppData/LocalLow/mkarpenko/WorldBox/Player.log`
Game state: alive, bridge 127.0.0.1:8766, isWorld3D=true, lastNonZeroDrawCalls=36.

## TL;DR — Root cause

Both water and slope renderers crash inside `Core.Sphere.CenterCapsule`
with `Transform child out of bounds`. The exception occurs at the very
first lifecycle entry, BEFORE `WaterSurface.Create` / `MountainSlopeSurface.Create`
is invoked. Materials never get a chance to render.

- **Water**: 0 surfaces created. Property fails once, `_lastMeshWater`
  was set to `true` BEFORE the try, so subsequent ticks early-return and
  the surface is never built. No `[WSM3D] Water material resolved...`,
  no `[WSM3D] Water shader:` lines, no `[WSM3D] Water mesh:` lines.
- **Slope**: Failed 59 frames, eventually succeeded on frame ~59 once
  `Manager.transform.GetChild(0)` returned cleanly. The slope material
  IS created (`WSM3D/OpaqueVertexColor`, instancing=True,
  1,095,925 verts). But the mesh is still black on screen — so for
  slopes the issue is downstream (material/shader/lighting), not the
  CenterCapsule race.

This means: water is INVISIBLE because it was never instantiated;
slopes are BLACK despite a successful build — investigate why
`OpaqueVertexColor` with vertex colors is rendering black for the slope
mesh specifically.

## Settings sanity (lines 638-666)

```
[WSM3D] Settings sanity: MountainSlopeSmoothing loaded=True default=False
[WSM3D] Settings sanity: MeshWater loaded=True default=False
[NML]: Set mesh_water toggle to True
[NML]: Set mountain_slope_smoothing toggle to True
```

Both flags enabled. Not a settings staleness issue.

## Shader bundle load (lines 918-971)

```
918: Mismatched serialization in the builtin class 'Shader'. (Read 5944 bytes but expected 5972 bytes)
919: Failed to load GpuProgram from binary shader data in 'WSM3D/GerstnerWater'.
920: [WSM3D] Loaded shader from wsm3d-shaders bundle: WSM3D/GerstnerWater -> WSM3D/GerstnerWater
969: Mismatched serialization in the builtin class 'Shader'. (Read 80 bytes but expected 4372 bytes)
970: [WSM3D] Shader 'ColorGradingLUT' loaded with empty name — bake produced corrupted asset, skipping LoadedShaders cache.
971: [WSM3D] LoadedShaders[count=2]: OpaqueVertexColor, GerstnerWater
```

The GerstnerWater shader bundle prints "Failed to load GpuProgram from
binary shader data" — this is a Unity engine-version mismatch warning.
It DID land in the LoadedShaders cache though (line 971), and the asset
itself loaded (line 920). The 5944 vs 5972 byte mismatch is suspicious —
this shader may render as the fallback "magenta error shader" or as
pitch black if compiled passes are missing.

The ColorGradingLUT shader IS confirmed corrupted (line 970, empty
name, 80 vs 4372 bytes) and explicitly skipped from cache. The 5972 vs
5944 mismatch on GerstnerWater is smaller (28 bytes) and the shader name
DID resolve — but a GPU program serialization failure means
**`shader.passCount` may be 0 or the compiled subshaders may be missing**.
Per `WaterRender.cs:290`, the code logs an error and disables water if
passCount==0. We never see that log because we never reach the create path.

## WaterRender failure (line 1239)

```
1236: [WSM3D] BridgeSurvival.Run in render3DStuff failed: UnityEngine.UnityException: Transform child out of bounds
1237:   at (wrapper managed-to-native) UnityEngine.Transform.GetChild(UnityEngine.Transform,int)
1238:   at WorldSphereMod.Core+Sphere.get_CenterCapsule () [0x00000] in Code\Core.cs:549
1239:   at WorldSphereMod.Water.WaterRender.UpdateLifecycle () [0x00039] in Code\Water\WaterRender.cs:24
1240:   at WorldSphereMod.Voxel.VoxelFrameDriver.<TickPerFrame>g__Measure|11_0 (System.Action action) [0x00007] in Code\Voxel\VoxelRender.cs:1248
```

Looking at `WaterRender.cs:16-43`:

```csharp
public static void UpdateLifecycle()
{
    bool now = Core.IsWorld3D && Core.savedSettings.MeshWater;
    if (now == _lastMeshWater) return;
    _lastMeshWater = now;     // <-- set BEFORE the try, so a throw permanently locks us out
    if (now)
    {
        WaterMaskBuffer.RebuildMask();
        Transform? capsule = Core.Sphere.CenterCapsule;  // <-- throws here (line 24)
        ...
    }
}
```

`_lastMeshWater = now` (line 20) executes BEFORE the throw on line 24.
After the throw, the next call sees `now == _lastMeshWater` and
early-returns. The WaterSurface is never created and never re-attempted.

There is only ONE WaterRender exception in the entire log (the rest are
all slope EnsureActive calls). This is the smoking gun: water rendering
silently dies after a single race-condition exception.

The `Sphere.Begin` BeginPostfix patch (`WaterRender.cs:46-68`) was
registered (line 4315 confirms 5 MeshWater Harmony types active) but
either ran before `Manager` had a child OR didn't run at all — there
are zero `[WSM3D] Water material resolved (Sphere.Begin)...` log lines,
which would have been emitted on a successful create. BeginPostfix
itself ALSO calls `Core.Sphere.CenterCapsule` (line 55) — so it likely
threw silently too (Harmony swallows postfix exceptions by default).

## CenterCapsule race (Core.cs:557)

```csharp
public static Transform CenterCapsule => Manager.transform.childCount > 0 ? Manager.transform.GetChild(0) : null;
```

`Sphere.Begin.ManagerCreated(async)=1937.669ms` (log line 1206) — the
Manager is created async. The ternary's `childCount > 0` check passes,
then between that check and `GetChild(0)` execution the child either:
- has not been added yet, or
- has been removed (unlikely on initial spawn).

But the more likely interpretation given the stack: `Manager` itself is
null and the NRE is being thrown by Unity natively as "Transform child
out of bounds" (Unity sometimes converts native errors). Or the
expression-body compilation line-mapped Core.cs:549 actually IS
`GetChild(0)`.

Either way the property is unsafe for early callers. Both water and
slope hit it.

## MountainSlopeSurface failure pattern (lines 1253-2168)

```
1253:   at WorldSphereMod.Terrain.MountainSlopeSurface.EnsureActive () [0x00037] in Code\Terrain\TerrainSmoothing.cs:134
```

`TerrainSmoothing.cs:134` is exactly:

```csharp
Transform? capsule = Core.Sphere.CenterCapsule;
```

Same root cause. Slope's EnsureActive is called every frame from
VoxelRender.cs:1508 and is **retried** until it succeeds — because
`EnsureActive` checks `Instance != null` to short-circuit, and the
exception happens before Instance is set.

Exactly **59 EnsureActive exceptions** are logged from line 1253 to
line 2168. After that, on the next frame, it finally succeeds:

```
2265: [WSM3D] Mountain slope material resolved via Core.Sphere.LoadedShaders cache.
2266: [WSM3D] Mountain slope material created: shader='WSM3D/OpaqueVertexColor' instancing=True
2267: [WSM3D] MountainSlopeSmoothing rebuilt 29135 cliff quads -> 43837 smooth tiles, 1095925 verts.
```

So slope IS built. Material is `WSM3D/OpaqueVertexColor` with
instancing on. 43,837 smooth tiles, ~1.1M vertices.

## Why is slope still black?

The slope material is the SAME `WSM3D/OpaqueVertexColor` shader that the
voxel actors use, and voxel actors ARE visible (Phase 1 victory). So the
shader itself works. Possibilities:

1. **Vertex colors are zero/black.** The slope mesh builder may be
   writing `Color.black` (or default `Color()`) into `mesh.colors`. The
   `OpaqueVertexColor` shader multiplies vertex color into the final
   output — black verts = black render. **Most likely.**
2. **Mesh.colors not assigned at all.** Unity defaults to (0,0,0,0) for
   unset color buffers, which renders black opaque.
3. **Per-instance `_Color` MaterialPropertyBlock pushes black.** Per the
   alpha.8 victory note, the shader respects per-instance `_Color`.
   If the slope renderer sets MPB `_Color = Color.black` (or doesn't
   set it but the material default is black), result is black.
4. **Wrong renderQueue / depth interaction with terrain.** Slopes might
   be Z-fighting with the underlying CompoundSphere terrain and losing.
5. **Submitted via `MeshRenderer` not `MeshInstanceBatcher`.** Slope
   uses a real GameObject (`MonoBehaviour`-driven), not the batcher.
   That means lighting/shadow paths differ from the voxel batch path —
   the shader may require an explicit ambient or light contribution
   that the GO path doesn't get.

The investigation file should check `MountainSlopeSurface.RebuildMesh`
to verify vertex colors are sourced from biome/tile color sampling.

## Why is water completely invisible?

It was never created. Fix the CenterCapsule race + the
`_lastMeshWater = now` ordering and water will at least attempt to render.
Then we'll learn whether the GerstnerWater shader's GpuProgram
serialization mismatch (line 919) leaves it with 0 passes — which is
the next likely failure.

## Telemetry — water/slope are not in the batch

```
1224: [WSM3D][Telemetry] frameMs=24.82 drawCalls=1 instances=2 cacheSize=90 cacheHits=2724 cacheMisses=90 submits=3044 gcMB=365.1
2302: [WSM3D][Telemetry] frameMs=336.49 drawCalls=1 instances=2 cacheSize=37 cacheHits=10013 cacheMisses=37 submits=17064 gcMB=494.5
4340: [WSM3D][Telemetry] frameMs=443.28 drawCalls=1 instances=2 cacheSize=38 cacheHits=598 cacheMisses=38 submits=4424 gcMB=503.9
```

`drawCalls=1 instances=2` consistently. That's just the sanity-test cube
submitting through the MeshInstanceBatcher. Water and slope render via
direct `MeshRenderer` GameObjects, not through this batcher, so they
wouldn't appear in these counts. (`lastNonZeroDrawCalls=36` comes from
the bridge/full-frame Unity stats, not this telemetry.)

## RefreshSphere baseline (for comparison)

```
2285: [WSM3D] RefreshSphere ... shader=CompoundSphere materialRenderQueue=2000 materialPassCount=1 texturedTiles=40369/65536
```

Terrain itself: `materialPassCount=1`. CompoundSphere material is healthy.
Slope sits ON TOP of this — it needs to not Z-fight and have non-zero
vertex colors.

## Other errors of interest

- `Not allowed to access vertices on mesh 'voxel:main_*' (isReadable is false; ...)` —
  hundreds of these. The cached voxel meshes have `isReadable=false`.
  Doesn't block rendering but blocks any code that reads back the mesh
  (e.g. for slope/water mask sampling, IF that's a code path).

## Action items (not for this session — investigation only)

1. **Fix CenterCapsule race** in Core.cs:557 — either yield until Manager
   is ready, or move the property to throw a cached null transform
   instead of GetChild(0).
2. **Fix `_lastMeshWater = now` ordering** in WaterRender.cs:20 — only
   set after `WaterSurface.Create` succeeds, so failures retry next frame.
3. **Verify slope vertex colors** — read `MountainSlopeSurface.RebuildMesh`
   and confirm it samples biome/tile colors per vertex rather than
   leaving the color buffer unset.
4. **Validate GerstnerWater passCount** — once water gets to Create,
   check the `[WSM3D] Water shader: name='...' supported=... passCount=...`
   log. If passCount=0 the bundle is corrupted by the Unity version skew
   and needs a rebake.
