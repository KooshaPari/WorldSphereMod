# ADR-0014 — Settings Lifecycle

**Status:** Problem documented, fix proposed but not implemented
**Date:** 2026-05-25

## Context

`SavedSettings` is the single configuration object for the WorldSphereMod3D
fork. It contains ~50 fields (booleans, floats, strings, enums) that gate
every phase of the 3D conversion pipeline. The object is serialized to
`mods_config/WorldSphereMod.json` via Newtonsoft.Json and deserialized at
mod load time in `Core.LoadSettings()`.

During the alpha.1 through alpha.8 development cycle, **settings staleness**
was the single most frequently re-encountered class of bug, responsible for
5+ independent debugging incidents in a single session. Each incident
followed the same pattern: a code default was changed, the on-disk JSON
retained the old value, and the deserialized result silently overrode the
new default, disabling or misconfiguring a feature.

## Architecture

### Current flow

```
Core.Init()
  -> Core.LoadSettings()
       -> File.ReadAllText("mods_config/WorldSphereMod.json")
       -> SavedSettingsJson.TryDeserialize(raw, out settings)
            -> SavedSettingsJson.ApplyTerrainSmoothingMigration(raw)
            -> JsonConvert.DeserializeObject<SavedSettings>(raw)
       -> if (version != SettingsVersion)
            -> ApplySchemaVersionMigration(settings)
            -> settings.Version = SettingsVersion
            -> SaveSettings()
       -> Core.savedSettings = settings
       -> LogPhaseFlagDefaults(settings)
  -> Core.SaveSettings()  [on first-install path only]
```

### Key participants

- **`SavedSettings`** (`WorldSphereMod/Code/SavedSettings.cs`): Plain
  `[Serializable]` class with public fields and C# field initializers as
  defaults. No constructor logic. Two static presets (`ApplyLightweightPreset`,
  `ApplyFullPreset`) for bulk configuration.

- **`SavedSettingsJson`** (`WorldSphereMod/Code/SavedSettingsJson.cs`):
  Unity-free deserialization helper. Runs one named migration
  (`ApplyTerrainSmoothingMigration`) that renames a field. Exposes
  `TryDeserialize()` for the load path and unit test fuzzing.

- **`Core.ApplySchemaVersionMigration()`** (`WorldSphereMod/Code/Core.cs`):
  On version mismatch, iterates all types with `[PhaseAttribute]`, collects
  their `SettingsFlagName` strings, and resets those boolean fields on the
  loaded settings object to the `new SavedSettings()` defaults. Also resets
  `CurrentShape` to the new default.

- **`PhasePatchManager`** (`WorldSphereMod/Code/PhasePatchManager.cs`):
  Hot-patches Harmony hooks at runtime when a phase flag is toggled via the
  in-game UI or bridge API. Calls `Core.Patcher.CreateClassProcessor(type).Patch()`
  or `Unpatch()` per type.

- **`PhaseDefaultsDriftTests`** (`tests/.../PhaseDefaultsDriftTests.cs`):
  E2E test that parses `SavedSettings.cs` boolean field initializers and
  compares them against the `$script:PhaseDefaults` hashtable in
  `Tools/wsm3d.ps1`. Catches drift between the two sources.

### Version field

`SavedSettings.Version` is a string (`"2.3"` at time of writing).
`Core.SettingsVersion` is the expected version. On mismatch,
`ApplySchemaVersionMigration` runs and bumps the persisted version.

## Root Cause: No Single Source of Truth

The fundamental problem is that **field initializers in `SavedSettings.cs`
are the intended defaults, but Newtonsoft deserialization unconditionally
overwrites them with whatever the JSON file contains.** There is no
mechanism to distinguish between:

1. A value the user explicitly set (should be preserved).
2. A value that was the old code default and was never touched by the user
   (should be updated to the new default).

Newtonsoft's `DefaultValueHandling` and `NullValueHandling` do not solve
this because the JSON file contains explicit values for every field (written
by `SerializeObject` with `Formatting.Indented`), so there is no "missing
field" signal to trigger default injection.

### The staleness pattern

1. Developer changes `VoxelEntities = false` to `VoxelEntities = true` in
   `SavedSettings.cs`.
2. Existing `WorldSphereMod.json` on disk still has `"VoxelEntities": false`.
3. `JsonConvert.DeserializeObject<SavedSettings>(raw)` produces an object
   with `VoxelEntities = false` (JSON wins over field initializer).
4. If `Version` in JSON matches `Core.SettingsVersion`, no migration runs.
5. The mod loads with `VoxelEntities = false`. The entire voxel pipeline is
   disabled. Debugging ensues.

### Why ApplySchemaVersionMigration is insufficient

- It only runs on **version mismatch**. If the developer changes a default
  but forgets to bump `SettingsVersion`, no migration fires.
- It only covers **`[Phase]`-gated boolean fields**. Non-phase fields
  (`VoxelScaleMultiplier`, `VoxelSpriteDepth`, `FogDensity`, `LODScale`,
  `RenderRange`, `BuildingSize`, etc.) are never migrated.
- It resets phase booleans to code defaults **unconditionally**, which means
  a user who intentionally disabled a phase flag will have it re-enabled on
  every version bump. There is no user-preference preservation.
- The `CurrentShape` reset is a one-off hardcoded fixup, not a generalizable
  pattern.

### Incidents traced to this root cause (this session)

| # | Symptom | Stale field | Time lost |
|---|---------|-------------|-----------|
| 1 | Voxel actors invisible | `VoxelEntities=false` | ~30 min |
| 2 | VoxelScaleMultiplier=4 after code changed to 8 | `VoxelScaleMultiplier=4.0` | ~20 min |
| 3 | Phase 6 skeletal animation not firing | `SkeletalAnimation=false` | ~15 min |
| 4 | PostFX not applying | `PostFX=false` | ~10 min |
| 5 | Debug sanity cube not rendering | `DebugSanityCube=false` | ~5 min |

Each was resolved by checking `[WSM3D] Settings sanity:` log lines, then
manually editing the JSON or deleting the file. The `Settings sanity` log
(added in `LogPhaseFlagDefaults`) was itself a diagnostic introduced to
catch this class of bug faster.

## Migration Gaps

1. **No migration for non-boolean fields.** `VoxelScaleMultiplier`,
   `VoxelSpriteDepth`, `FogDensity`, `LODScale`, `WaterDetail`,
   `FoliageDensity`, `VoxelInflationStyle`, `VoxelNeutralLuminance`,
   `VoxelShadowRecession`, `AutoScreenshotIntervalSeconds`, and
   `AutoScreenshotPath` are never migrated. A stale value persists forever.

2. **No migration for enum fields.** `SSAOQuality` (enum `SsaoQuality`)
   is deserialized from JSON and never validated against the current enum
   range. A removed enum variant would throw on deserialization.

3. **`ApplyTerrainSmoothingMigration` is string-level, not schema-level.**
   It operates on raw JSON text before deserialization, checking for
   substring `"TerrainSmoothing"`. This is fragile (e.g., a comment or
   unrelated field containing that substring would trigger it).

4. **`PhaseDefaultsDriftTests` only covers the `wsm3d.ps1` PhaseDefaults
   hashtable**, not the actual on-disk JSON files users have. It catches
   drift between two source-of-truth candidates but does not prevent the
   runtime staleness problem.

5. **Version string is manually bumped.** There is no CI check or
   pre-commit hook that detects a default change without a version bump.

## Proposed Fix: Delete-on-Version-Bump + Field Validation

### Option A: Delete-on-version-bump (recommended for alpha)

On version mismatch, delete the entire JSON file and re-save from
`new SavedSettings()`. Simple, brutal, and correct for alpha where no user
has invested in custom configuration. The current `ApplySchemaVersionMigration`
already resets phase flags; extending to "reset everything" is a one-line
change.

**Pros:** Eliminates the entire class of staleness bugs. Zero ongoing
maintenance. Users who care about settings can back up the file.

**Cons:** Loses user preferences on every mod update. Unacceptable for beta
or release.

### Option B: Per-field version stamps (recommended for beta)

Add a `Dictionary<string, string> FieldVersions` to `SavedSettings` that
records the version at which each field was last explicitly set by the user
(vs. loaded from default). On version mismatch, only reset fields whose
`FieldVersions[field]` is older than the version that changed that field's
default.

**Pros:** Preserves user intent. Only resets fields whose defaults actually
changed.

**Cons:** Requires maintaining a changelog of which version changed which
default. More complex serialization. Overkill for alpha.

### Option C: Separate user-overrides file

Split settings into `defaults.json` (shipped, read-only, regenerated on
version bump) and `overrides.json` (user-written, merged on top). Only
fields present in `overrides.json` are preserved; everything else comes
from `defaults.json`.

**Pros:** Clean separation. User intent is explicit. Missing fields in
overrides naturally fall through to current defaults.

**Cons:** Two-file merge logic. Migration of existing single-file users.
Largest implementation cost.

### Immediate mitigations (already in place)

- `LogPhaseFlagDefaults()` logs every phase flag's loaded vs. default value
  at startup, making staleness diagnosable from Player.log.
- `PhaseDefaultsDriftTests` catches drift between `wsm3d.ps1` and
  `SavedSettings.cs` in CI.
- The `wsm3d.ps1 settings set` command and MCP `settings_toggle` tool both
  call `SaveSettings()` after mutation, ensuring the JSON is always
  well-formed.

### Recommended next step

Implement Option A (delete-on-version-bump) for the remainder of alpha.
Gate it behind a `const bool AggressiveSettingsReset = true` in `Core.cs`
so it can be flipped to false when entering beta. Add a CI check that
fails if any `SavedSettings` field initializer changes without a
corresponding `SettingsVersion` bump.

## Verification Criteria

- After implementing Option A: changing any field default in
  `SavedSettings.cs` + bumping `SettingsVersion` results in the old JSON
  being deleted and all fields loading at their new defaults.
- `[WSM3D] Settings sanity:` log lines show `loaded == default` for every
  phase flag after a version-bump load.
- `PhaseDefaultsDriftTests` continues to pass.
- No regression: toggling a setting in-game, saving, and reloading
  preserves the toggled value (same-version round-trip still works).

## Linked ADRs

- ADR-0005: Default-on flags per phase ship gate
- ADR-0010: Voxel actor visibility (RC-8: settings staleness)
- ADR-0018: Default-on flag cascade
