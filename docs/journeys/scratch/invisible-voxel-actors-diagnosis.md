# Invisible voxel actors diagnosis

Scope: `WorldSphereMod/Code/Voxel/MeshInstanceBatcher.cs`, `WorldSphereMod/Code/Voxel/VoxelRender.cs`

## What the current code says

1. **Standard shader instancing**
   - `Graphics.DrawMeshInstanced` requires `Material.enableInstancing = true`; Unity documents this as a hard requirement for the API.
   - `VoxelRender.EnsureMaterial()` does set `enableInstancing = true`, then keeps going if Unity accepts it.
   - So Standard does **not** magically instanced-draw without the material flag. The flag is required; the shader alone is not enough.

2. **`ShadowCastingMode.On`**
   - This is not self-occlusion.
   - It only tells Unity the mesh may cast shadows. It does not hide the mesh in the main color pass.
   - `SanityTestCube.Draw()` also uses `ShadowCastingMode.On` and is visible, so shadow casting alone is not the culprit.

3. **Invalid matrices**
   - `Graphics.DrawMeshInstanced` docs do not describe a “single bad matrix silently no-ops the whole call” behavior.
   - The documented hard failures are unsupported platform / `enableInstancing == false`.
   - The current batcher does not validate matrices before draw, but the API itself does not point to matrix invalidity as the silent invisibility mechanism.

4. **Fallback path when `InstancingBroken` is set**
   - `MeshInstanceBatcher.Flush()` does switch to `Graphics.DrawMesh` after an `InvalidOperationException`.
   - But `VoxelRender.Submit()` returns `false` immediately when `MeshInstanceBatcher.InstancingBroken` is already true, so future voxel submissions never reach the fallback path.
   - That means the fallback is only a same-flush recovery, not a persistent recovery mode.

5. **Camera mask / layer 0**
   - The instanced call already passes `camera = null`, so it is not tied to a single camera target.
   - `ResolveRenderLayer()` derives a layer from the active camera mask only when `layer == 0`.
   - `SanityTestCube.Draw()` also uses layer 0 and is visible, which strongly suggests the live camera mask does include the default layer in the current scene.
   - So layer 0 is not the leading suspect.

## Diagnosis

The current code path is already past the old “wrong camera target” bug. The most likely remaining failure is that instancing is failing or being treated as failed, and the current `InstancingBroken` gate then suppresses all later submissions before the fallback path can be used.

## Minimum visibility fix

Smallest code change to restore visibility if instancing is the blocker:

- Remove the early `if (MeshInstanceBatcher.InstancingBroken) return false;` in `VoxelRender.Submit()`.
- Keep the `MeshInstanceBatcher.Flush()` fallback path as the recovery route.

That preserves the same mesh submission logic, but lets the already-implemented `Graphics.DrawMesh` fallback keep drawing after the first instancing failure.

## Diagnostic patch applied

I added one-flush verbose logging in `MeshInstanceBatcher.Flush()` that is armed by the first submit when `ProfilerDump` is enabled:

- logs the mesh name, material name, shader, `enableInstancing`, batch size, layer, shadow mode, and first instance position
- only runs for one flush after the first submit
- resets on `MeshInstanceBatcher.Reset()`

This should tell us whether the batcher is reaching `DrawMeshInstanced`, whether the material is actually instancing-capable at draw time, and whether the fallback flag is already active.
