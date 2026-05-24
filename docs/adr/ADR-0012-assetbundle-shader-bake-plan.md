# ADR-0012 - AssetBundle shader bake plan for VoxelLit instancing

**Status:** Proposed
**Date:** 2026-05-19

## Context

Phase 1 voxel rendering is functionally visible again, but it is not using the
intended fast path.

Commit `da14a92` changed `MeshInstanceBatcher.Flush` after an in-game failure
where enabling Phase 1 made actors disappear instead of becoming voxel meshes.

The failure was not a missing mesh, missing matrix, or unsupported GPU feature.

The mod could create materials with `Material.enableInstancing = true`, but
`Graphics.DrawMeshInstanced` rejected the material at draw time.

The observed exception was an `InvalidOperationException` from Unity's draw
path.

The root cause recorded in `da14a92` is that this WorldBox/URP build does not
make a runtime-available shader variant with `INSTANCING_ON` available for the
candidate built-in shaders.

The checked candidates included:

- `Universal Render Pipeline/Simple Lit`
- `Universal Render Pipeline/Lit`
- `Universal Render Pipeline/Unlit`
- `Universal Render Pipeline/Particles/Unlit`
- `Standard`
- `Particles/Standard Unlit`

Those shaders can exist at runtime and still fail for this use case if the
specific instancing variant was stripped or never shipped in the player data.

That distinction matters for WorldBox modding because the mod cannot rely on
Unity Editor shader import behavior at game runtime.

It also cannot assume that a shader name resolving through `Shader.Find` means
the required keyword variant is present.

The immediate fix in `da14a92` made rendering robust:

- Try `Graphics.DrawMeshInstanced` first.
- Catch the first `InvalidOperationException`.
- Set `MeshInstanceBatcher.UseFallbackPath`.
- Log a single error explaining that voxel render performance is degraded.
- Continue rendering visible voxels with one `Graphics.DrawMesh` call per
  instance.

That fallback is correct for visibility and smoke testing.

It is not the intended steady-state renderer.

The fallback path costs roughly one draw call per actor instance, which erases
the batching benefit that Phase 1 depends on when many actors are visible.

The original design goal for `MeshInstanceBatcher` remains one draw call per
mesh/material bucket per 1023 submitted instances.

To reclaim that path, the mod needs a shader whose instancing variant is known
to be compiled into content that the WorldBox player can load.

For this repo, the most controlled way to do that is to build the shader through
a Unity project and ship it inside a NeoModLoader-installed AssetBundle.

## Decision

Bake a custom `VoxelLit` shader into a Unity AssetBundle with an explicit
`INSTANCING_ON` variant, load that shader at runtime, and make it the preferred
voxel material path before any built-in shader fallback.

The shader will be authored under `WorldSphereMod/Resources/Shaders/` and baked
through a Unity AssetBundle build project or repeatable build target.

The AssetBundle output will be installed under the mod's existing
`AssetBundles/{platform}/` layout.

Runtime code will load the bundled shader and create a material from it for
voxel rendering.

The shader must support per-instance color data used by
`MeshInstanceBatcher`.

The first target is modest:

- vertex color support
- `_InstanceColor` multiply
- `_BaseColor` or equivalent base tint
- main directional light response if available
- acceptable ambient fallback
- instancing macros and buffers compiled by Unity

The AssetBundle shader is the primary fix.

Built-in URP shaders remain fallback candidates only.

The per-instance `Graphics.DrawMesh` path remains a last-resort visibility
fallback for installs where the bundle fails to load or the shader still fails
Unity's instanced draw.

Add a `RuntimeShaderInfo` diagnostic dump to verify the runtime material and
shader state before and after the AssetBundle path is enabled.

The diagnostic should record enough information to distinguish these cases:

- bundled shader loaded and instanced draw accepted
- bundled shader loaded but instanced draw rejected
- bundle missing
- shader missing from bundle
- shader loaded but material configuration wrong
- built-in fallback selected
- per-instance draw fallback active

The dump is part of the decision because this problem is runtime-variant
specific.

Compile-time success alone is not sufficient evidence that the fast path has
been reclaimed.

## Implementation steps

1. Add the shader source file as `WorldSphereMod/Resources/Shaders/VoxelLit.shader`
   with Unity instancing macros and properties matching the current voxel
   material contract.

2. Include a small always-reachable material asset, for example
   `WorldSphereMod/Resources/Materials/VoxelLit.mat`, so Unity's AssetBundle
   build has a concrete reference to the shader and its pass configuration.

3. Configure the Unity AssetBundle build so `VoxelLit.shader` and the material
   are assigned to the WorldSphereMod AssetBundle for every supported platform
   that the mod installs under `AssetBundles/{platform}/`.

4. Add an explicit shader variant collection or equivalent build-time reference
   that includes the `INSTANCING_ON` variant for the VoxelLit pass.

5. Add or update a repeatable Taskfile/PowerShell target for the AssetBundle
   build rather than documenting a manual Unity Editor click path.

6. Update `Tools/install.ps1` if needed so the baked bundle lands in the
   NeoModLoader runtime layout:
   `<WorldBox>/Mods/WorldSphereMod3D/AssetBundles/{platform}/`.

7. Add a runtime loader that resolves the platform bundle, loads the `VoxelLit`
   shader or material, and exposes a clear success/failure result to
   `VoxelRender`.

8. Change voxel material selection so the bundled `VoxelLit` material is tried
   before `Universal Render Pipeline/Simple Lit`, `Lit`, `Unlit`, and particles
   fallback shaders.

9. Add `RuntimeShaderInfo` logging for the selected shader name, bundle source,
   material instance name, `enableInstancing`, active keywords, supported pass
   count, and whether `MeshInstanceBatcher.UseFallbackPath` was set.

10. Add a one-frame instancing probe in a controlled path, or use the first real
    `MeshInstanceBatcher.Flush`, to confirm `Graphics.DrawMeshInstanced` accepts
    the material without throwing.

11. Keep the current `Graphics.DrawMesh` fallback path unchanged until the
    AssetBundle shader has passed an in-game smoke test on the target WorldBox
    build.

12. Document the smoke-test evidence in the phase notes: Phase 1 enabled,
    actors visible as voxels, `UseFallbackPath == false`, non-zero instances,
    and draw calls consistent with instanced batching instead of per-actor
    drawing.

## Consequences

### Positive

The voxel renderer can return to the intended `Graphics.DrawMeshInstanced`
batching model.

Per-actor draw call pressure should drop back toward one draw per bucket per
1023 instances.

The shader contract becomes owned by the mod instead of inferred from whatever
WorldBox happens to ship.

Runtime verification becomes explicit through `RuntimeShaderInfo` instead of
depending on whether a shader name exists.

Future voxel lighting work has a stable shader surface for vertex colors,
instance colors, ambient terms, and optional reflection/probe behavior.

### Negative

The mod gains an AssetBundle build dependency for the Phase 1 fast path.

Bundle generation must be kept in sync with Unity version, target platform, and
NeoModLoader install layout.

A shader that works in the Unity Editor can still fail in the WorldBox player if
the bundle is built for the wrong runtime, graphics API, or platform.

The install artifact becomes more important than the C# build output for this
specific rendering path.

### Neutral

The existing per-instance `Graphics.DrawMesh` fallback stays as a safety net.

The hardware gate in `Mod.OnLoad` should not be tightened for this change.

Missing compute or indirect-args support remains separate from shader variant
availability.

The feature should remain behind the relevant SavedSettings phase flag until an
in-game smoke test confirms the bundled shader path.

## Open questions

1. Which Unity version exactly matches the WorldBox player build closely enough
   for shader AssetBundle compatibility?

2. Which graphics APIs must the bundle cover on Windows installs: Direct3D11
   only, or additional backends used by WorldBox launch options?

3. Should the runtime loader consume a material from the bundle, a shader from
   the bundle, or both, with the material serving as the canonical variant
   anchor?

4. Where should `RuntimeShaderInfo` live: a lightweight voxel diagnostic class,
   the existing profiler overlay, or a broader debug dump used by later phases?

5. Should the instancing probe draw a hidden tiny mesh, or should verification
   rely only on the first real batch submitted by gameplay?

6. How should bundle load failures surface to users: log-only, stats overlay,
   settings panel warning, or a docs-driven troubleshooting step?

7. Does Phase 5 own the final lit BRDF behavior, or should this ADR's VoxelLit
   shader intentionally stay minimal until the sun/sky work lands?

8. Should CI validate the AssetBundle manifest and expected shader/material
   names even if it cannot run WorldBox and prove the runtime variant?
