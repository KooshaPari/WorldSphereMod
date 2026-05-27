# Research: Assembly Publicizer for WSM3D

**Date:** 2026-05-26
**Status:** Assessment complete -- partially applicable

## Background

WorldSphereMod3D uses extensive `System.Reflection` calls and Harmony
`AccessTools` to reach private/internal members of WorldBox's
`Assembly-CSharp.dll`. These reflection sites are fragile: they fail silently
when method signatures change across WorldBox versions.

## Current State: NML Already Ships a Publicized Assembly

NeoModLoader (NML) **already produces and distributes**
`Assembly-CSharp-Publicized.dll`. It lives at:

```
<WorldBox>/worldbox_Data/StreamingAssets/mods/NML/Assembly-CSharp-Publicized.dll
```

The WSM3D `.csproj` already references it:

```xml
<Reference Include="Assembly-CSharp">
  <HintPath>$(WorldBoxNML)/Assembly-CSharp-Publicized.dll</HintPath>
</Reference>
```

This means **all WorldBox types are already fully accessible at compile time**.
Every `private`, `internal`, and `protected` member in `Assembly-CSharp` is
rewritten to `public` in the publicized variant. The mod compiles against
publicized types and NML loads it at runtime with the real assembly (access
checks are disabled by Mono's permissive runtime).

### What "Publicizer" means

A publicizer (whether BepInEx.AssemblyPublicizer, NStrip, or NML's built-in
Mono.Cecil pass) rewrites IL metadata in an assembly:

1. All `private`/`internal`/`protected` fields -> `public`
2. All `private`/`internal`/`protected` methods -> `public`
3. All `private`/`internal`/`protected` types -> `public`

The rewritten DLL is used as a **compile-time reference only**. At runtime the
original assembly loads; Mono/.NET ignores access modifiers in already-JITted
call sites (this is the "IgnoresAccessChecksTo" pattern).

### No Additional BepInEx Plugin Needed

BepInEx is in the WorldBox modding ecosystem but WSM3D loads through NML, not
BepInEx. NML's own publicizer covers the same ground as
`BepInEx.AssemblyPublicizer`. There is **no additional tool to install**.

## Reflection Audit: What Can Be Eliminated

### Category 1: WorldBox internal field/method access -- ELIMINABLE

These sites access WorldBox types that are already public in the publicized
assembly. They should be direct member access instead of reflection.

| File | Line(s) | Target | Reflection Used | Can Replace? |
|------|---------|--------|-----------------|--------------|
| `Lighting/TimeOfDay.cs` | 35-38 | `MapBox.world_time` (float) | `typeof(MapBox).GetField("world_time", ...)` | YES -- direct field access |
| `Core.cs` | 807 | `SphereManager.SphereTiles` | `typeof(SphereManager).GetField("SphereTiles", NonPublic)` | YES -- direct field access |
| `Terrain/TerrainSmoothing.cs` | 558-560 | `Core.Sphere` private field | `typeof(Core.Sphere).GetField(..., NonPublic)` | YES -- direct field access |
| `Mod.cs` | 46, 53-54 | `WrappedAssetBundle.assetBundle` + `LoadAllAssets` | `typeof(WrappedAssetBundle).GetField("assetBundle", NonPublic)` | YES -- direct field/method access |
| `AutoTest.cs` | 198-203 | `WorldTilemap.rerenderEverything` / `refreshAll` / `clearAndRedraw` | Multi-fallback `GetMethod` chain | PARTIAL -- name varies across versions; keep fallback chain but try direct call first |
| `ProcGen/DebugSpawnBuildingsDriver.cs` | 93-105 | `BuildingManager.addBuilding` | `GetMethods` + parameter-match loop | YES -- direct call with publicized access |
| `WorldSphereTab.cs` | 478-502 | `Windows.hide` / `Windows.close` | `GetMethod("hide" / "close")` | PARTIAL -- name varies; try direct, keep fallback |
| `Bridge/BridgeServer.cs` | 1260-1261 | `VoxelRender._material` | `GetField("_material", NonPublic)` | N/A -- this is our OWN type, not WorldBox (just make the field `internal` or add an accessor) |

### Category 2: SavedSettings reflection -- NOT ELIMINABLE (by design)

Multiple files reflect over `SavedSettings` fields dynamically:

- `AutoTest.cs` (lines 48, 55, 92): iterates phase flags by string name
- `BridgeServer.cs` (lines 965, 1311, 1315, 1328): RPC endpoint resolves
  settings by string key from JSON
- `WorldSphereTab.cs` (lines 507, 514, 727): UI toggle binding
- `Core.cs` (line 98): phase flag lookup by name
- `PhasePatchGate.cs` (line 33): dynamic gate check
- `WorldSphereAPI.cs` (line 94): external API settings query

`SavedSettings` is **our own type** (defined in `SavedSettings.cs`). This
reflection is intentional -- it's a dynamic dispatch pattern for phase flags,
not an access-modifier problem. A publicizer would not help here. If desired,
this could be replaced with a `Dictionary<string, Func<bool>>` but that is a
separate refactor.

### Category 3: URP/PostProcessing reflection -- NOT ELIMINABLE

- `Fx/PostFxController.cs` (lines 58-300+): resolves URP Volume, Bloom,
  ColorAdjustments, Vignette, Tonemapping, etc. by string type name
- `Lighting/ShadowCascadeConfig.cs` (lines 56-71): resolves URP shadow
  cascade properties by name

This reflection exists because **WorldBox ships no URP runtime DLLs** in its
`Managed/` folder. The types may or may not exist at runtime depending on the
Unity render pipeline version. This is a runtime capability probe, not an
access-modifier issue. A publicizer cannot help.

### Category 4: Unity API reflection -- NOT ELIMINABLE

- `Texture/McPackLoader.cs` + `WorldSphereTab.cs`: `TryLoadPngViaReflection`
  tries `Texture2D.LoadImage` and `ImageConversion.LoadImage` -- these APIs
  may or may not exist depending on the Unity version NML ships with.
- `ScreenshotCapture.cs`: resolves `ScreenCapture.CaptureScreenshot` by name.

These are Unity version compatibility shims, not WorldBox access problems.

### Category 5: Harmony transpiler `AccessTools` -- ALREADY CORRECT

Files like `3DCamera.cs`, `General.cs`, `QuantumSprites.cs`,
`DimensionConverter.cs` use `AccessTools.Method(typeof(...), ...)` inside
Harmony transpilers. This is **idiomatic Harmony usage** -- transpilers
manipulate IL and need `MethodInfo` references. These are not replaceable with
direct calls; they are IL-level operands.

## Summary of Actionable Eliminations

| Site | Current | Replacement | Risk |
|------|---------|-------------|------|
| `TimeOfDay._wbTimeField` | Reflection `GetField` | `MapBox.instance.world_time` (direct) | Low -- field exists in publicized DLL |
| `Core.cs:807 SphereTiles` | Reflection `GetField` NonPublic | Direct field access | Low -- NML publicizes it |
| `TerrainSmoothing.cs:558` | Reflection `GetField` NonPublic | Direct field access | Low |
| `Mod.cs:46 WrappedAssetBundle` | Reflection `GetField` NonPublic | Direct field access | Low -- NML type, always publicized |
| `DebugSpawnBuildingsDriver` | Reflection method search | `manager.addBuilding(...)` direct | Low |
| `BridgeServer:1260 VoxelRender._material` | Reflection on own type | Make field `internal` + direct access | Zero risk (own code) |

Approximate count: **6 reflection sites can be converted to direct access**,
eliminating ~40 lines of reflection boilerplate and gaining compile-time
breakage detection.

## Risk Assessment

### Why direct access is safe

1. **NML guarantees the publicized assembly**. It is the official mod-dev
   reference for WorldBox modding. Every NML mod compiles against it.
2. **Mono runtime ignores access checks** on already-loaded assemblies. The
   publicized DLL is a compile-time fiction; at runtime the real
   `Assembly-CSharp.dll` loads and Mono's JIT does not enforce visibility.
3. **Compile-time breakage is the goal**: if WorldBox renames `world_time` to
   `worldTime`, the build fails immediately instead of silently returning null
   from `GetField`.

### Cross-version compatibility risk

- **Direct access breaks loudly** on signature changes -- which is better than
  silent reflection failures.
- The multi-fallback sites (`AutoTest` trying `rerenderEverything` then
  `refreshAll` then `clearAndRedraw`) exist because WorldBox has renamed these
  methods across versions. For those, keep the fallback pattern but try the
  direct call first to get compile-time validation of at least one variant.
- WorldBox updates ~2-4 times per year. Each update may rename or restructure
  internals. The publicized DLL must be regenerated (NML does this
  automatically on first launch after a WorldBox update).

### What would NOT change

- Harmony transpilers stay as-is (they need `MethodInfo` for IL rewriting).
- URP reflection stays as-is (runtime capability probe, not access issue).
- Unity API reflection stays as-is (version compatibility shim).
- SavedSettings self-reflection stays as-is (intentional dynamic dispatch).

## Recommendation

**Do not add any new tooling.** NML's publicizer is already in place and the
`.csproj` already references the publicized assembly. The 6 identified
reflection sites should be refactored to direct member access in a future
cleanup pass. This would:

1. Eliminate ~40 lines of fragile reflection code
2. Convert silent runtime failures into compile-time errors
3. Require zero new dependencies or build pipeline changes

Suggested scope: a single `chore: replace reflection with direct publicized access`
commit touching the 6 files listed above.
