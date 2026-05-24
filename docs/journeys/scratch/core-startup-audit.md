# Core Startup Audit

Scope: `WorldSphereMod/Code/Core.cs` startup sequence, plus the UI startup path it calls.

## Findings

1. `LoadSettings()` runs before any settings-dependent startup logic. In `Init()`, the first step is `LoadSettings()` and only after that does it call `WorldSphereTab.Begin()`, `DimensionConverter.Prepare()`, and `Patch()` ([Core.cs:65-79]). I did not find any static field initializer in `Core.cs` that reads from `savedSettings`; the only class-level initialization is `public static SavedSettings savedSettings = new SavedSettings();` ([Core.cs:24-27]), which constructs defaults rather than reading disk. Settings-dependent access in this file is deferred to runtime members like `GeneratingSphere` and `Sphere.Begin()` ([Core.cs:277-280], [Core.cs:343-348]).

2. Harmony patching happens after the startup UI path that can create components. `Init()` calls `WorldSphereTab.Begin()` before `Patch()` ([Core.cs:65-79]). `WorldSphereTab.Begin()` immediately creates tabs/buttons and can allocate UI objects; its `CreateWindow()` path attaches `PowerWindow` with `AddComponent<PowerWindow>()`, and `PowerWindow.init()` attaches `VerticalLayoutGroup` with `AddComponent<VerticalLayoutGroup>()` ([WorldSphereTab.cs:45-50], [WorldSphereTab.cs:387-418]). So `PatchAll` is **after** the startup `AddComponent` work, not before.

3. `Sphere.Begin()` is not idempotent and can leak the previous sphere if called twice without `Finish()`. `Begin()` overwrites the static `Manager` with a fresh `SphereManager` instance and does not check for or destroy an existing one first ([Core.cs:343-352]). `Finish()` only destroys the current `Manager` if it exists ([Core.cs:404-411]). If `Begin()` is invoked again before `Finish()`, the old manager reference is lost and its GameObject is not explicitly cleaned up here.

4. I found no `[System.NonSerialized]` fields anywhere in `WorldSphereMod/Code` ([repo-wide scan]). There is nothing in `Core.cs` that appears to be incorrectly hidden from serialization, and `SavedSettings` itself has no `[NonSerialized]` annotations ([SavedSettings.cs:4-60]).

## Bottom Line

- Settings load order: safe.
- Harmony patch order: `AddComponent` work in the startup UI path happens first, then `PatchAll`.
- Sphere lifecycle: double-`Begin()` without `Finish()` is a real leak risk.
- Serialization: no `[NonSerialized]` fields were present to review.
