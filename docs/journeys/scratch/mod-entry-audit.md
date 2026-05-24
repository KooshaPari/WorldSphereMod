# Mod Entry Audit

## Startup Order

`Mod.OnLoad()` only captures the NML-provided `ModDeclare`/`GameObject`, checks hardware, and sets `IsAutoTest` from env/settings. It does not build gameplay state yet. `OnLoad()` is at [`Mod.cs:17-31`](../../WorldSphereMod/Code/Mod.cs#L17).

`Mod.Init()` runs the real bootstrap in this order:
1. `Core.Init()`
2. add `VoxelFrameDriver`
3. add `ProfilerFrameDriver`
4. add `WindSwayDriver`
5. `WorldUIRenderer.EnsureCreated()`
6. `RuntimeStatsOverlay.EnsureCreated()`
7. `TimeOfDay.EnsureCreated()`
8. `ProceduralSky.EnsureCreated()`

That ordering is visible in [`Mod.cs:38-82`](../../WorldSphereMod/Code/Mod.cs#L38).

`Core.Init()` itself does:
1. `LoadSettings()`
2. `WorldSphereTab.Begin()`
3. `DimensionConverter.Prepare()`
4. `Patch()`
5. `SunDriver.Init()` if `Core.IsWorld3D`
6. `DoSomeOtherStuff()` hotkey registration

See [`Core.cs:65-79`](../../WorldSphereMod/Code/Core.cs#L65).

`PostInit()` is minimal: `Core.PostInit()` (`Sphere.Prepare()`), then debug/autotest driver setup. See [`Mod.cs:85-93`](../../WorldSphereMod/Code/Mod.cs#L85) and [`Core.cs:110-113`](../../WorldSphereMod/Code/Core.cs#L110).

## Ordering Hazards

I did not find the sky/time ordering bug you suspected. `TimeOfDay` is created before `ProceduralSky`, and `ProceduralSky.LateUpdate()` reads the static `TimeOfDay.Current` plus `SunRig.Sun`. `TimeOfDay.Update()` is the component that continuously writes `SunDriver.TimeOfDay`. Relevant lines: [`TimeOfDay.cs:15-55`](../../WorldSphereMod/Code/Lighting/TimeOfDay.cs#L15) and [`ProceduralSky.cs:16-57`](../../WorldSphereMod/Code/Lighting/ProceduralSky.cs#L16).

`SunDriver.Init()` already happens earlier inside `Core.Init()`, so the sky component does not depend on a later bootstrap step. See [`SunDriver.cs:18-42`](../../WorldSphereMod/Code/Lighting/SunDriver.cs#L18).

The only soft hazard is that `Core.Init()` gates `SunDriver.Init()` on `Core.IsWorld3D`. If `Mod.Init()` runs before the sphere exists, lighting setup is deferred until world generation triggers `Core.Become3D()` via the `SphereControl` patch. That looks intentional, not reversed-order breakage. See [`General.cs:16-33`](../../WorldSphereMod/Code/General.cs#L16) and [`Core.cs:252-257`](../../WorldSphereMod/Code/Core.cs#L252).

## Re-Init / Hot Reload

There is no `Mod.OnDisable()` or `Mod.OnDestroy()`, so a true mod unload/reload has no top-level teardown path. `Mod.Init()` will happily run again, but cleanup only exists for world unload, not mod unload.

World unload is wired through a Harmony prefix on `Core.Sphere.Finish()`: [`WorldUnloadPatch.cs:12-31`](../../WorldSphereMod/Code/Voxel/WorldUnloadPatch.cs#L12). That path clears world-scoped caches and tears down `WorldUIRenderer`, voxel/procgen/LOD caches, and related state. `WorldUIRenderer.OnWorldUnload()` is the most complete singleton cleanup here: [`WorldUIRenderer.cs:39-63`](../../WorldSphereMod/Code/Worldspace/WorldUIRenderer.cs#L39).

Hot-reload leak risks remain:
- `Core.Patcher` is recreated in `Core.Init()` with no global unpatch pass, so re-running `Init()` in the same AppDomain risks duplicate Harmony state. See [`Core.cs:117-149`](../../WorldSphereMod/Code/Core.cs#L117) and [`PhasePatchManager.cs:15-77`](../../WorldSphereMod/Code/PhasePatchManager.cs#L15).
- `SunDriver.Teardown()` exists but is never called. `SunRig` also keeps a static `_sun` reference with no reset path. See [`SunDriver.cs:44-53`](../../WorldSphereMod/Code/Lighting/SunDriver.cs#L44) and [`SunRig.cs:5-16`](../../WorldSphereMod/Code/Lighting/SunRig.cs#L5).
- `TimeOfDay.Current`, `ProceduralSky.Instance`, `RuntimeStatsOverlay.Instance`, and other static caches survive process-level hot reload unless their component `OnDestroy()`/world-unload path actually runs. See [`TimeOfDay.cs:6-33`](../../WorldSphereMod/Code/Lighting/TimeOfDay.cs#L6), [`ProceduralSky.cs:5-37`](../../WorldSphereMod/Code/Lighting/ProceduralSky.cs#L5), and [`RuntimeStatsOverlay.cs:21-50`](../../WorldSphereMod/Code/Worldspace/RuntimeStatsOverlay.cs#L21).

Net: startup order is mostly sane; the bigger issue is missing mod-level teardown, not a `TimeOfDay`/`ProceduralSky` ordering bug.
