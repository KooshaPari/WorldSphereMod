# WorldSphereMod3D Maturity Audit

**Audited:** 2026-05-25  
**Scope:** Full codebase + tooling surface scan  
**Build check:** `dotnet build WorldSphereMod.csproj -c Release` -- 0 errors, 11 warnings

---

## Summary

| Dimension | Overall | Score |
|-----------|---------|-------|
| AX (Agent Experience) | Strong tooling, some gaps in error actionability and test coverage | 4 / 5 |
| DX (Developer Experience) | Excellent documentation and CLI, CI gap on mod compile, 11 build warnings | 3.5 / 5 |
| UX (User/Modder Experience) | Phase UI is comprehensive, most phases default-OFF with no first-run guide | 3 / 5 |
| Modding API | v2 surface landed but thin on events and documentation | 2.5 / 5 |

---

## AX (Agent Experience)

| Area | Maturity | Gaps | Priority | Effort | Recommended Fix |
|------|----------|------|----------|--------|-----------------|
| **CLAUDE.md completeness** | 5 | None significant. Build, conventions, pitfalls, tooling, "where to make changes" table all present and accurate. Critical paths documented. | -- | -- | Maintain as-is |
| **Bridge API coverage** | 4 | 17+ endpoints covering health, telemetry, settings CRUD, voxel introspection, save/load, spawn_units, generate_world, screenshot, texturepack import, diag dump. **Missing:** no `pause/unpause` game, no `camera_move/rotate`, no `list_saves`, no `get_actors` (only voxel/actor with index). Agent cannot navigate the camera or enumerate saves programmatically. | P1 | M | Add `/actions/pause`, `/actions/camera` (position+rotation), `/saves` (list), `/actors` (paginated list with asset IDs) |
| **PlayCUA scenario coverage** | 4 | 13 YAML scenarios covering all 10 phases + bridge smoke + save/load. **Missing:** no negative-path scenarios (e.g. "toggle phase while world is loading", "double-toggle rapid fire"), no mod-compat scenario. | P2 | S | Add 2-3 adversarial YAML scenarios for edge cases |
| **Tool CLI (wsm3d.ps1) completeness** | 5 | 2541 LOC, 20+ subcommands, tab-completion, JSON output modes, doctor diagnostics, watch mode, playcua integration. | -- | -- | Maintain |
| **MCP server tool coverage** | 4 | 22 tools across game, log, settings, build, journey, live-verify, codex categories. **Missing:** no `phase_check` tool despite being in alwaysAllow list (it may exist but isn't registered as a tool in server.py). `status` tool returns hardcoded "unknown" for all fields. | P1 | S | Implement real `status` tool (delegates to wsm3d.ps1 status -Json), verify phase_check tool exists |
| **Error message actionability** | 3 | `[WSM3D]` log prefix is consistent and grep-friendly. Bridge returns structured JSON errors with `ok:false` + `error` string. **But:** NML compile failure is silent (no `[WSM3D]` prefix), shader load failures logged at Unity level without WSM3D tag, settings staleness has no log sentinel. Agent must know to grep for "Failed to compile mod" separately. | P1 | M | Add `[WSM3D][Diagnostics]` startup probe that logs compile success/fail, shader load results, settings version match, and hardware caps in a single block. Mention this log block in CLAUDE.md |
| **Test coverage for agent workflows** | 3 | 489 tests (151 unit + 69 integration + 266 E2E + 3 skip). Strong on source invariants and manifest schema. **Missing:** no tests for Bridge HTTP endpoints (only source-content invariants), no tests for MCP server tools, no tests for wsm3d.ps1 CLI exit codes. PlayCUA scenarios test at integration level but only when game is running. | P1 | L | Add bridge HTTP endpoint unit tests (mock HttpListener or use TestServer pattern), MCP tool smoke tests, wsm3d.ps1 subcommand exit-code tests |

---

## DX (Developer Experience)

| Area | Maturity | Gaps | Priority | Effort | Recommended Fix |
|------|----------|------|----------|--------|-----------------|
| **Build instructions accuracy** | 4 | CLAUDE.md, README, HANDOFF all document build correctly. Default fallback to Steam path works on Windows. **Nitpick:** CLAUDE.md shows `$env:WORLDBOX_PATH = "C:/Program Files (x86)/Steam/steamapps/common/worldbox"` (lowercase) but install.ps1 uses `Worldbox` (capitalized W). Works because Windows FS is case-insensitive, but confusing on first read. | P2 | S | Normalize all references to `worldbox` (lowercase, matching Steam directory name) |
| **Install/test cycle friction** | 4 | Single-command install (`./Tools/install.ps1`), watch mode with auto-install on file save, tab completion. **Friction:** first clone requires `wsm3d submodule init` which isn't called out as step 1 in README. No `just setup` or `just bootstrap` recipe that runs submodule + hooks + mcp install in one shot. | P1 | S | Add `just bootstrap` recipe: `submodule init` + `hooks install` + `mcp-install` + `doctor`. Add "After clone" section to README |
| **Code organization** | 5 | Clean module structure: `Voxel/`, `ProcGen/`, `Foliage/`, `Water/`, `Lighting/`, `Rig/`, `Worldspace/`, `Fx/`, `LOD/`, `Perf/`, `Bridge/`, `Import/`, `Texture/`. Each phase maps to a directory. "Where to make changes" table in CLAUDE.md. | -- | -- | Maintain |
| **Settings management** | 3 | `SavedSettingsJson.cs` handles v1-v2 migration (TerrainSmoothing). JSON deserialization with TryDeserialize. **But:** no schema validation (unknown keys silently ignored), no settings version bump mechanism beyond the Version string, PhaseDefaults in wsm3d.ps1 can drift from SavedSettings.cs defaults (wsm3d.ps1 shows VoxelEntities=false but code default is true; CrossedQuadFoliage=true in wsm3d but false in HANDOFF). `[WSM3D] Settings sanity:` log lines mentioned in MEMORY.md but unclear if actually emitted. | P0 | M | Add settings schema test that generates PhaseDefaults from SavedSettings.cs reflection and asserts wsm3d.ps1 matches. Add startup log block: `[WSM3D] Settings sanity: VoxelEntities=true, ProceduralBuildings=false, ...` |
| **Debugging tools** | 4 | InitProfiler, RuntimeStatsOverlay, render-budget CLI, frame draw call history, BridgeRPC /telemetry, /memory, /voxel/stats, /voxel/queue, /voxel/actor, /voxel/diff. Doctor command. **Gap:** no `wsm3d.ps1 diag` command that collects a full diagnostic bundle (log tail + settings dump + bridge health + doctor + git status) into a single JSON file for bug reports. | P2 | S | Add `wsm3d diag` that collects everything into `Tools/.reports/diag-<timestamp>.json` |
| **Documentation** | 4 | HANDOFF.md is exemplary (current state, blockers, next steps, recent commits). PLAN.md is detailed 10-phase spec. Per-phase architecture docs (phase2-10). Smoke test checklists for all 10 phases. Live-verification.md. **Gap:** no `docs/architecture-overview.md` that shows the high-level module dependency graph or data flow (render pass pipeline). No API reference docs. | P2 | M | Generate architecture-overview.md with module graph and render-pass data flow |
| **CI/CD completeness** | 3 | 13 workflows covering build, test-gate, lint-gate, journeys-gate, docs-build-gate, live-verify-gate, nightly, release, dependency-security-audit, vercel deploy, screenshot. **Gaps:** (1) mod csproj build is `continue-on-error: true` in CI -- not a real gate. (2) No Windows runner for live verification. (3) No artifact upload for build output. (4) E2E tests only partially run in build.yml (3 specific test classes). (5) No code coverage reporting. | P1 | L | Add self-hosted Windows runner for mod compile gate. Upload build artifacts. Run full E2E suite in test-gate. Add coverage via coverlet |
| **Build warnings** | 3 | 11 warnings: 3x CS0618 (deprecated NML Windows API), 3x CS0618 (deprecated RenderSettings.customReflection), 1x CS0162 (unreachable code in WaterSurface), 2x CS0649 (unassigned BridgeServer fields), 1x CS0414 (unused ShadowCascadeConfig._active), 1x CS0618 (deprecated Kingdom.kingdomColor). | P1 | S | Fix the 2 CS0649 (assign _instanceGeneration in constructor), suppress the 3 NML deprecations with pragmas (documented as known), fix CS0162 unreachable code, fix CS0414 unused field |
| **Contribution guide** | 4 | Two-tier: root CONTRIBUTING.md (quick pointer) + docs/CONTRIBUTING.md (governance, branch rules, verification flow, 10 sections). MERGE_CHECKLIST.md. CODEOWNERS. **Gap:** no "good first issue" labels or onboarding tasks documented. No architecture decision records index (ADRs mentioned in HANDOFF but no docs/adr/ directory). | P2 | S | Create docs/adr/index.md linking existing ADR references. Add "good first issue" section to CONTRIBUTING.md |

---

## UX (User/Modder Experience)

| Area | Maturity | Gaps | Priority | Effort | Recommended Fix |
|------|----------|------|----------|--------|-----------------|
| **In-game settings UI (WorldSphereTab)** | 4 | 23 toggle buttons in "3D Phases" window covering all phases + sub-features (SSAO, SSGI, bloom, tonemapping, weather x3, biome blending, building style). Sliders for building_size, render_distance, tile_length_multiplier. Profiler dump toggle. Reset defaults button. **Gaps:** (1) toggles are a flat list with no grouping/categories (render, lighting, weather, debug are intermixed). (2) No tooltip hover showing current value without clicking. (3) No FogDensity, LODScale, WaterDetail, FoliageDensity sliders (only boolean toggles for those). (4) SSAOQuality enum has no UI selector. | P1 | M | Group toggles into collapsible categories (Rendering, Lighting, Weather, PostFX, Debug). Add sliders for FogDensity/LODScale/WaterDetail/FoliageDensity. Add SSAOQuality dropdown |
| **Phase toggle behavior** | 3 | Toggles call `Core.ApplyPhaseToggle(fieldName, value)` which implies some runtime effect. `PhasePatchManager` handles conditional patch dispatch. **But:** UI header says "Reload the world for full effect" -- unclear which toggles are instant vs require reload. Some phases (voxel entities) likely take effect next frame, others (mesh water, skeletal) may need world rebuild. No per-toggle indicator of "requires restart" vs "instant". | P1 | S | Add `[requires reload]` suffix to locale strings for toggles that need it. Document instant vs deferred in settings UI help text |
| **Error handling visible to user** | 2 | Compute-shader gate shows red mod icon on incompatible hardware (good). **But:** no in-game toast/notification when a phase fails to initialize. Silent failures go to Player.log only. No "Phase X failed to load: reason" popup. NML compile failures show nothing to user. | P0 | M | Add lightweight in-game notification system: if a phase flag is true but init failed, show a one-time toast with the reason and a "see Player.log" hint |
| **Performance feedback** | 3 | RuntimeStatsOverlay exists (opt-in via ProfilerDump). Shows frame draw calls, instances, frame ms. **Gaps:** no FPS counter visible by default when any 3D phase is active. No loading progress indicator when voxelizing sprites or building procgen caches. No memory usage indicator. | P2 | S | Add opt-in FPS counter overlay (separate from full profiler). Add brief "Generating meshes..." toast during heavy cache-miss bursts |
| **Mod compatibility** | 2 | Compat.cs provides polyfills for net48/Mono. Co-installable with upstream via different GUID. **But:** no documentation of tested mod combinations. No compatibility matrix. No runtime detection of conflicting mods (e.g. if another mod patches the same Harmony methods). CONTRIBUTING.md mentions co-install test but no automated test for it. | P1 | M | Add docs/compatibility.md listing tested mod combinations. Add startup log line listing detected mods + potential conflicts |
| **First-run experience** | 2 | install.ps1 copies files to Mods/. User must manually enable in NeoModLoader. No first-run wizard. No "welcome" screen explaining the 10 phases. Phases default mostly OFF (only VoxelEntities and ACESTonemapping are ON). New user sees terrain-only 3D with no obvious visual change from upstream unless they know to open "3D Phases" window. | P0 | M | Add a first-run detection (flag in SavedSettings). On first run, auto-open the Phases window with a brief intro text. Consider a "recommended preset" button (e.g. "Standard 3D" enables phases 1+3+5+7) |
| **Localization** | 3 | 4 locale files: en, ch, cz, ru. English is comprehensive (all 23 toggles + descriptions + UI labels). **Gap:** v2 fork-specific strings (phase names, descriptions) likely missing from ch/cz/ru translations. No locale coverage test. | P2 | S | Add E2E test asserting all en.json keys exist in other locale files. Fill missing translations or add `[en]` fallback prefix |

---

## Modding API Surface

| Area | Maturity | Gaps | Priority | Effort | Recommended Fix |
|------|----------|------|----------|--------|-----------------|
| **WorldSphereAPI completeness** | 3 | v1 surface preserved (IsWorld3D, MakeActorNonUpright, MakeBuildingNonUpright, MakeProjectileNonUpright, EditEffect, GetSetting). v2 adds: IsModel3D, GetVersion, GetCapabilities, HasFeature, RegisterCustomMesh, RegisterBuildingRules. **Missing from PLAN.md spec:** `RegisterRig(assetId, RigData)`, `RegisterEffectMesh(effectId, mesh)`. `OnTimeOfDayChanged` event exists on internal API but no delegate in external WorldSphereAPI.dll. | P1 | M | Add RegisterRig + RegisterEffectMesh to both internal and external API. Expose OnTimeOfDayChanged as a delegate in WorldSphereAPI.dll |
| **Extension points for other mods** | 2 | Custom mesh registration is the main extension point. Building rules override via JSON or API. **But:** no event hooks for render pipeline stages (pre-render, post-render). No way to add custom LOD tiers. No custom material injection. No custom UI widget registration in WorldSphereTab. | P2 | L | Design extension point API: `OnPreRender`, `OnPostRender`, `RegisterLodTier`, `RegisterMaterial`, `RegisterTabWidget` |
| **Event hooks available** | 2 | Only `OnTimeOfDayChanged` (internal). No events for: world load/unload, phase toggle, actor spawn/destroy, building placed, settings changed, camera moved. Other mods cannot react to WSM3D state changes. | P1 | M | Add events: `OnPhaseToggled(string phase, bool enabled)`, `OnWorldLoaded`, `OnSettingsChanged`. Expose via external API with safe no-op fallback |
| **Documentation of public API** | 2 | XML doc comments on all public methods in both WorldSphereAPI.cs files (good). README shows a usage snippet. **But:** no standalone API reference page in the docs site. No versioned changelog of API changes. No migration guide from v1 to v2. No examples beyond the README snippet. | P1 | M | Add `docs/api-reference.md` with full method signatures, parameter docs, and usage examples. Add `docs/api-changelog.md` |

---

## Priority Summary

### P0 -- Blocking / must-fix

| # | Area | Issue | Effort |
|---|------|-------|--------|
| 1 | DX/Settings | PhaseDefaults in wsm3d.ps1 can drift from SavedSettings.cs; no schema sync test | M |
| 2 | UX/Errors | No in-game notification when a phase fails to initialize; failures are silent | M |
| 3 | UX/First-run | New user sees no visual change from upstream; phases window not auto-opened; no preset | M |

### P1 -- High value

| # | Area | Issue | Effort |
|---|------|-------|--------|
| 4 | AX/Bridge | Missing pause, camera control, save list, actor list endpoints | M |
| 5 | AX/MCP | `status` tool returns hardcoded unknowns; `phase_check` may be unregistered | S |
| 6 | AX/Errors | NML compile failure not tagged `[WSM3D]`; no startup diagnostics log block | M |
| 7 | AX/Tests | No Bridge HTTP tests, no MCP tool tests, no CLI exit-code tests | L |
| 8 | DX/Onboarding | No `just bootstrap` recipe; submodule init not in README step 1 | S |
| 9 | DX/CI | Mod csproj build is `continue-on-error`; no Windows runner; partial E2E in build.yml | L |
| 10 | DX/Warnings | 11 build warnings (2 real bugs: CS0649 unassigned fields, 1 unreachable code) | S |
| 11 | UX/Settings UI | Flat toggle list; missing sliders for float settings; no SSAOQuality dropdown | M |
| 12 | UX/Toggles | No per-toggle "requires reload" indicator | S |
| 13 | UX/Compat | No compatibility matrix; no runtime conflict detection | M |
| 14 | API/Surface | RegisterRig + RegisterEffectMesh missing; OnTimeOfDayChanged not in external API | M |
| 15 | API/Events | Only 1 event hook; no phase toggle, world load, or settings change events | M |
| 16 | API/Docs | No standalone API reference page; no changelog; no migration guide | M |

### P2 -- Nice to have

| # | Area | Issue | Effort |
|---|------|-------|--------|
| 17 | AX/PlayCUA | No negative-path / adversarial scenarios | S |
| 18 | DX/Build paths | worldbox/Worldbox casing inconsistency in docs | S |
| 19 | DX/Debugging | No `wsm3d diag` bundle command | S |
| 20 | DX/Docs | No architecture-overview.md with module graph | M |
| 21 | DX/Contributing | No ADR index; no "good first issue" section | S |
| 22 | UX/Perf | No default FPS counter; no mesh generation progress toast | S |
| 23 | UX/Locales | v2 strings likely missing from ch/cz/ru translations | S |
| 24 | API/Extension | No render pipeline hooks, custom LOD tiers, or material injection | L |

---

## Effort Legend

- **S** (Small): 1-4 hours, single file or small change
- **M** (Medium): 4-16 hours, multiple files, some design needed
- **L** (Large): 16+ hours, significant new infrastructure or cross-cutting change
