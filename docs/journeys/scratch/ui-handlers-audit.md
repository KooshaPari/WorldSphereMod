# UI Handlers Audit

Scope: `WorldSphereMod/Code/WorldSphereTab.cs`, `WorldSphereMod/Code/Core.cs`, and the phase-driven runtime drivers in `WorldSphereMod/Code/Mod.cs`, `WorldSphereMod/Code/Worldspace/WorldUIRenderer.cs`, `WorldSphereMod/Code/Lighting/TimeOfDay.cs`, and `WorldSphereMod/Code/Fx/PostFxController.cs`.

## Findings

1. I did **not** find an actual null or no-op handler in the tab wrappers themselves.
   - `GenerateSlider(...)` writes the value back into `savedSettings` and saves immediately, so it is not a debug-only callback ([WorldSphereTab.cs:134-154](../../../WorldSphereMod/Code/WorldSphereTab.cs#L134-L154)).
   - `CreateButton("Open Sprites", ...)` and `CreateButton("Reset Defaults", ...)` both receive real delegates ([WorldSphereTab.cs:224-230](../../../WorldSphereMod/Code/WorldSphereTab.cs#L224-L230)).
   - The `3D Phases` entries all pass real toggle methods such as `TogglePhase_WorldspaceUI`, `TogglePhase_DayNightCycle`, `TogglePhase_PostFX`, and `TogglePhase_ParticleEffects` ([WorldSphereTab.cs:209-220](../../../WorldSphereMod/Code/WorldSphereTab.cs#L209-L220), [WorldSphereTab.cs:239-242](../../../WorldSphereMod/Code/WorldSphereTab.cs#L239-L242)).

2. The `null` seen in `CreateToggleButton(..., null, default, true)` is not the click handler.
   - The actual click path is the `GodPower.toggle_action` delegate registered in `CreateToggleButton(...)` ([WorldSphereTab.cs:347-372](../../../WorldSphereMod/Code/WorldSphereTab.cs#L347-L372)).
   - That means the handler that matters is the `toggleAction` lambda stored on the `GodPower`, not the `null` metadata slot in the button factory call.

3. The phase UI was mostly wired, but two startup-only drivers needed enable-side creation hooks.
   - `WorldspaceUI` is backed by `WorldUIRenderer.EnsureCreated()`, which is only called during `Mod.Init()` ([Mod.cs:67-82](../../../WorldSphereMod/Code/Mod.cs#L67-L82), [WorldUIRenderer.cs:31-39](../../../WorldSphereMod/Code/Worldspace/WorldUIRenderer.cs#L31-L39)).
   - `DayNightCycle` is backed by `TimeOfDay.EnsureCreated()` and `ProceduralSky.EnsureCreated()`, which are also only called during `Mod.Init()` ([Mod.cs:76-82](../../../WorldSphereMod/Code/Mod.cs#L76-L82), [TimeOfDay.cs:19-27](../../../WorldSphereMod/Code/Lighting/TimeOfDay.cs#L19-L27)).
   - Before the fix, toggling those flags on later in the session saved the setting and ran `Core.ApplyPhaseToggle(...)`, but did not create the runtime component that actually makes the feature visible ([Core.cs:81-92](../../../WorldSphereMod/Code/Core.cs#L81-L92)).

4. The other phase flags are already live-wired or self-polled.
   - `HighShadows` already has the `SunDriver.ApplyShadowSettings()` special-case in `Core.ApplyPhaseToggle(...)` ([Core.cs:84-86](../../../WorldSphereMod/Code/Core.cs#L84-L86), [SunDriver.cs:18-31](../../../WorldSphereMod/Code/Lighting/SunDriver.cs#L18-L31)).
   - `PostFX` is polled every frame from `VoxelFrameDriver.LateUpdate()` through `PostFxController.ApplySetting(...)` ([VoxelRender.cs:664-670](../../../WorldSphereMod/Code/Voxel/VoxelRender.cs#L664-L670), [PostFxController.cs:283-289](../../../WorldSphereMod/Code/Fx/PostFxController.cs#L283-L289)).
   - `ParticleEffects` is checked in the effect path, so it does not need a separate create hook ([Effects.cs:202-207](../../../WorldSphereMod/Code/Effects.cs#L202-L207)).

## SavedSettings coverage

Fields with UI controls but no `Core.ApplyPhaseToggle(...)` wiring are the non-phase persistence controls:

- `BuildingSize`
- `RenderRange`
- `TileHeight`
- `Is3D`
- `InvertedCameraMovement`
- `FirstPerson`
- `CameraRotatesWithWorld`
- `UpsideDownMovement`
- `CurrentShape`
- `PerlinNoise`
- `ProfilerDump`

That is intentional. None of those fields are phase-gated runtime systems.

## Fix Applied

`Core.ApplyPhaseToggle(...)` now recreates the startup-only drivers when their flags are enabled:

- `WorldspaceUI` now calls `WorldUIRenderer.EnsureCreated()` ([Core.cs:88-90](../../../WorldSphereMod/Code/Core.cs#L88-L90))
- `DayNightCycle` now calls `TimeOfDay.EnsureCreated()` and `ProceduralSky.EnsureCreated()` ([Core.cs:91-95](../../../WorldSphereMod/Code/Core.cs#L91-L95))

That keeps the existing UI intact and closes the “click saved the flag but nothing happened” gap for those phases without changing the broader phase model.
