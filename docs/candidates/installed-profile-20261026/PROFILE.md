# Preserved installed profile with opt-in capture safety

This source candidate preserves the observed installed WorldSphereMod profile while retaining the three-file safety patch from commit83b2fac7b4fc5ee5330b01064b21fa327a953317 (PR73). It is not an installation or runtime acceptance receipt.

## Exact source accounting

The previous120-file source candidate matched104 installed files and differed on16. Reverse enumeration found5installed-only dependencies. This candidate accounts for all125Code files:122match the installed snapshot;3retain the reviewed safety candidate. Every source path and SHA256 is listed in sources.tsv.

Preserved shared changes (13files):
- Sphere/camera/terrain:3DCamera.cs, CompoundSphereScripts.cs, Core.cs, TileMapToSphere.cs, Tools.cs, ProcGen/BuildingProcRender.cs.
- Voxel renderer:Voxel/VoxelRender.cs.
- Diagnostic/save bridge:Bridge/BridgeActions.cs, Bridge/BridgeServer.cs.
- GPU rig:Rig/RigDriver.cs, Rig/RigGpuSkinning.cs.
- Texture import:Texture/McPackLoader.cs, Texture/McPackManifestIO.cs.

Preserved installed-only dependencies (5files):
- Geometry/SphereGeometry.cs and Terrain/TerrainVisualState.cs.
- Bridge/FrameSampleRing.cs, Bridge/RuntimeIdentity.cs and Bridge/SandboxSavePath.cs.

Safety overlay (3files):AutoScreenshotDriver.cs, ScreenshotCapture.cs and SavedSettings.cs. SavedSettings was checked against the installed snapshot with only AutoScreenshotEnabled=false and AutoScreenshotPath=string.Empty substituted; no unrelated setting differs. Persisted settings are not migrated. Manual capture APIs remain present; they were never invoked against Unity.

## Binary and bundle boundary

The preserved CompoundSpheres.dll SHA256 isAB3CFF0B320A5E03AAF75A55E61F353F86CAECA983317D965B428DACB2492D64, matching its installed provenance JSON. That record claims source32b5c1c760ac427563ba41a706887bda652fd3f7, sourcePublished:false and runtimeAcceptance:pending.

The preserved Windows wsm3d-shaders SHA256 is7743EC4D783E0335E20885A1154F3D8FF218269E7A7BDA65158FD68AD714ABE8, matching its installed provenance JSON. Shader-source claim:0e23ed6b5db4ee4339a129a582ebdc0bfccf2027; rig-source claim:035218a612f97d0aa0114ab0ecb4c51faa050275; playerValidation:pending. Both records are preserved claims, not independently verified upstream publication.

## Validation on2026-10-26Pacific

- Exhaustive source path/hash assertion:125/125PASS; no omitted or extra Code files.
- Settings field-level merge assertion:PASS.
- Offline restore and fake-Unity safety build:PASS.
- All32existing synchronous xUnit Fact/InlineData cases:PASS through scratch in-process invocation, with fresh instance/Dispose per case and deliberate failing-assert negative control. Standard VSTest executed zero cases; its testhost connection remains a separate environment issue.
- Actual main mod net48compile against pinnedDLL and read-only installed WorldBox/NML references:PASS,0errors/15warnings. Invoke with explicit WorldBoxPath; this sandbox leaves OS empty, so inferred paths fail.
- git diff --check:PASS.

ABI/caller coherence:Core configures GPU rig resources; RigDriver dispatches and flushes RigGpuSkinning; sphere camera/tools/buildings/voxels share SphereGeometry and Core.Sphere.IsSpherical; Core/TileMapToSphere share TerrainVisualState; bridge diagnostics and SaveAs resolve the three new bridge helpers. The main compile verifies these static symbols together, not behavior under Unity scheduling.

The security-review input-boundary pass inspected preserved SaveAs/SandboxSavePath containment, existing-destination rejection, reserved names, reparse ancestors and slot-path restoration. It does not establish protection against filesystem races or runtime save correctness.

## Provenance and remaining gate

Inputs were hash-verified snapshots under agents/sandbox/wsm-reconcile-map-20261026-2325, observed installed at worldbox_Data/StreamingAssets/Mods/WorldSphereMod. Original C: dirty source and E: checkout were not edited. All snapshots and raw build/mock results remain in the sandbox. No game launch, real capture, installed writes, user-save changes or feature-default enablement occurred.

Next gate: independently inspect the full install/update/rollback plan and declare a capture-free runtime profile before any installation. Then qualify load, terrain/actors/camera, save/reload, disable/removal and required negative controls. Do not infer that a successful compile closes those runtime obligations.
