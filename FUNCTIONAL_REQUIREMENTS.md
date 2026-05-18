# Functional Requirements — WorldSphereMod3D

**Document ID:** FR-WSM
**Status:** Living
**Last updated:** 2026-05-18

Every behavioral commitment of the fork is captured here as a numbered
`FR-WSM-NNN`. Use RFC 2119 keywords (SHALL / SHOULD / MAY). Tests and code
trace back via `// Traces to: FR-WSM-NNN` comments (Phenotype convention,
`docs/phenotype-conventions.md` §4).

Numbering scheme: `FR-WSM-0NN` per phase block (000s = cross-cutting,
100s = Phase 1, 200s = Phase 2, …, 1000s = Phase 10).

---

## 0xx — Cross-cutting / governance

| ID | Requirement |
|---|---|
| FR-WSM-001 | The mod SHALL load under NeoModLoader with GUID `worldsphere3d.fork`, distinct from upstream's `worldsphere`, enabling co-installation. |
| FR-WSM-002 | The build SHALL succeed via `dotnet build WorldSphereMod.csproj -c Release` given a valid `WORLDBOX_PATH` env var (or default Steam install per `Directory.Build.props`), returning 0 errors. |
| FR-WSM-003 | The `WorldSphereAPI` v1 surface (`IsWorld3D`, `MakeActorNonUpright`, `MakeBuildingNonUpright`, `MakeProjectileNonUpright`, `EditEffect`, `GetSetting<T>`) SHALL remain signature-compatible with upstream. |
| FR-WSM-004 | The `WorldSphereAPI` v2 surface (`IsModel3D`, `RegisterCustomMesh`, `RegisterBuildingRules`) SHALL no-op safely when the connected host is upstream rather than this fork. |
| FR-WSM-005 | Every rendering phase SHALL be gated by a `SavedSettings` boolean flag and MUST NOT break another phase when flipped OFF. |
| FR-WSM-006 | The mod SHALL persist settings under `SettingsVersion = "2.0"` and migrate v1.5 preferences forward without loss. |
| FR-WSM-007 | A single `WorldUnloadPatch` Prefix on `Core.Sphere.Finish` SHALL drain all fork-side caches (voxel, procgen, foliage, rig, batcher buckets, impostor atlas, LOD hysteresis, world-UI rig) on world unload. |
| FR-WSM-008 | When the GPU fails the compute-shader / instancing / indirect-args gate, the mod SHALL set `ImpostorOnlyMode = true` rather than throwing `IncompatibleHardwareException`. |

## 1xx — Phase 1 (voxel actors + items + projectiles)

| ID | Requirement |
|---|---|
| FR-WSM-101 | The system SHALL voxelize each sprite into a cube mesh when `VoxelEntities` is enabled, with per-cube colour baked into a vertex-colour attribute. |
| FR-WSM-102 | `SpriteVoxelizer` SHALL apply greedy meshing on the X/Y plane (Z-thickness=1 default) and yield ~5–10× vertex reduction over naive per-texel emit. |
| FR-WSM-103 | `VoxelMeshCache` SHALL key on `Sprite.GetInstanceID()` and use LRU eviction with deferred `Object.Destroy` to avoid destroying in-flight meshes. |
| FR-WSM-104 | `MeshInstanceBatcher.Submit` SHALL bucket by `(mesh, material)` and `Flush` SHALL emit up to 1023 instances per `Graphics.DrawMeshInstanced` call with a correctly sized per-batch colour array (no tail-garbage tint on partial batches). |
| FR-WSM-105 | Voxel actor rotation SHALL be yaw-only (no Z/X lean) to prevent body topple while walking. |
| FR-WSM-106 | Drops, items, projectiles, and talk bubbles SHALL route through the voxel pipeline when `VoxelEntities` is enabled; talk-bubble/arrow variants MAY retain camera-billboard rotation. |

## 2xx — Phase 2 (procedural building meshes)

| ID | Requirement |
|---|---|
| FR-WSM-201 | The system SHALL emit a procedural mesh per `BuildingAsset` via footprint extrusion + multi-story inference + door/window detection + roof inference when `ProceduralBuildings` is enabled. |
| FR-WSM-202 | `BuildingMeshGen` SHALL infer roof type (flat / gable / hipped) from the dominant warm-palette cluster in the upper sprite rows. |
| FR-WSM-203 | The public API `RegisterBuildingRules(string assetId, object rules)` SHALL allow external mods to override the procgen heuristic per asset. |
| FR-WSM-204 | `ProcGenCache.Clear` SHALL route through a deferred-destroy queue (not synchronous `Object.Destroy` under lock). |

## 3xx — Phase 3 (foliage, walls, surface overlays)

| ID | Requirement |
|---|---|
| FR-WSM-301 | Trees, bushes, and rocks SHALL render as crossed-quad meshes (two perpendicular quads sharing the sprite texture) when `CrossedQuadFoliage` is enabled. |
| FR-WSM-302 | Grass / life / road surface overlays SHALL be rendered via a Prefix on `WorldTilemap.renderTile`. |
| FR-WSM-303 | Walls SHALL be rendered as 3D prisms via a Prefix on `QuantumSpriteLibrary.drawWallType`. |
| FR-WSM-304 | Wind sway vertex displacement SHALL be parameterized by asset tag (`tag_foliage` swayed, `tag_rock` zero displacement). |

## 4xx — Phase 4 (mesh water)

| ID | Requirement |
|---|---|
| FR-WSM-401 | The water surface SHALL be a mesh layer overlaid on the terrain, clipped to a per-tile water mask, when `MeshWater` is enabled. |
| FR-WSM-402 | Water vertices on the X-wrap seam SHALL be deduplicated (no shimmer on cylindrical wrap). |
| FR-WSM-403 | The water material SHALL be instanced per renderer (not mutated shared template) and released on `Destroy`. |
| FR-WSM-404 | Underlying tile colour SHALL suppress its water-tint alpha when the mesh water layer is active (avoid double-blue). |
| FR-WSM-405 | Runtime `MeshWater` toggle SHALL be supported via `WaterRender.UpdateLifecycle`; tile changes SHALL invalidate the water mask via Postfixes on `UpdateBaseLayer` / `UpdateScale`. |

## 5xx — Phase 5 (sun + cascaded shadows)

| ID | Requirement |
|---|---|
| FR-WSM-501 | A directional `Sun` light SHALL be parented to a `LightingRoot` GameObject (not the camera) and rotated by `TimeOfDay`. |
| FR-WSM-502 | URP cascaded shadows SHALL be configurable via reflective bindings on the active pipeline asset when `HighShadows` is enabled. |
| FR-WSM-503 | `ShadowCascadeConfig` SHALL stash original pipeline-asset values exactly once (`_hasOriginals` set once, never cleared) so Reset→Apply→Reset never re-stashes mod values as originals. |

## 6xx — Phase 6 (skeletal animation)

| ID | Requirement |
|---|---|
| FR-WSM-601 | Voxel actors SHALL be skinned via a 12-bone humanoid rig or 9-bone quadruped rig when `SkeletalAnimation` is enabled. |
| FR-WSM-602 | `SpriteVoxelizer.BuildPerTexel` SHALL produce a non-greedy variant carrying per-voxel bone indices for the rig consumer. |
| FR-WSM-603 | `RigDriver` SHALL prefer the GPU compute-skinning path and fall back to CPU bind-pose on any compute-shader failure. |
| FR-WSM-604 | `RigCache` eviction SHALL release the matching `RigDriver` GPU mesh entry to prevent buffer leak. |

## 7xx — Phase 7 (worldspace UI)

| ID | Requirement |
|---|---|
| FR-WSM-701 | Nameplates, HP bars, and damage popups SHALL render in world space when `WorldspaceUI` is enabled. |
| FR-WSM-702 | The HP bar SHALL share a single static mesh and material across all actors. |
| FR-WSM-703 | `Actor.getHealthRatio` reflection MethodInfo SHALL be cached (no per-frame per-actor lookup). |
| FR-WSM-704 | Damage popups SHALL be pooled (64 world-canvas TMP instances) with lazy camera assignment. |

## 8xx — Phase 8 (day/night + sky + fog)

| ID | Requirement |
|---|---|
| FR-WSM-801 | `TimeOfDay` SHALL probe `MapBox.world_time` at startup via reflection and fall back to an autonomous driver when the field is absent. |
| FR-WSM-802 | `SunRig` SHALL drive a 4-anchor (night / dawn / noon / dusk) color-temperature gradient when `DayNightCycle` is enabled. |
| FR-WSM-803 | The skybox SHALL be swapped at runtime to a procedural-sky material via `RenderSettings.skybox`. |

## 9xx — Phase 9 (particles + decals + post-FX)

| ID | Requirement |
|---|---|
| FR-WSM-901 | A pool of 16 `ParticleSystem`s SHALL burst on at least 5 effect IDs when `ParticleEffects` is enabled. |
| FR-WSM-902 | `DecalPool` SHALL maintain three sub-pools — Footprint (32), Scorch (16), Blood (32) — of flat quads with TTL expiry. |
| FR-WSM-903 | The URP `Volume` + `VolumeProfile` (Bloom + ColorAdjustments + Vignette) SHALL be bound reflectively and gated by `PostFX` (default OFF). |

## 10xx — Phase 10 (LOD + impostor + profiler)

| ID | Requirement |
|---|---|
| FR-WSM-1001 | `LodSelector` SHALL choose between `Voxel`, `Proxy`, and `Impostor` tiers with 3-frame hysteresis on tier transitions. |
| FR-WSM-1002 | `FrustumCuller` SHALL cache `GeometryUtility.CalculateFrustumPlanes` once per frame. |
| FR-WSM-1003 | `ImpostorBillboard` SHALL maintain a sprite-keyed quad atlas as the compatibility-fallback rendering path. |
| FR-WSM-1004 | `FrameProfiler` SHALL emit per-system stopwatch totals once per second when `ProfilerDump` is enabled. |
| FR-WSM-1005 | `LodSelector._hyst` per-actor hysteresis state SHALL be reaped on `WorldUIRenderer.UnregisterActor`. |

---

> **Traceability:** Tests SHALL include `// Traces to: FR-WSM-NNN` so the
> compliance scanner (Phenotype `phenotype-compliance-scanner`) can map
> requirements to verification. Coverage target per
> `docs/phenotype-conventions.md` §4 is 95%+.
