# Phase 4 Water Render Audit

## Findings

- **(1) Cull-lift bug pattern: no.** `WaterSurface.RebuildMesh()` does not read actor/building render positions at all; it walks `World.world.tiles_list`, uses each `WorldTile`'s `x/y`, and builds vertices from `Core.Sphere.SpherePos(cx, cy, sea)` instead of a raw-vs-lifted cull pair ([WaterSurface.cs:71-127]). The only per-tile depth source is `WaterMaskBuffer.RebuildMask()`, which reads `WorldTile.GetHeight()` and `t.main_type.render_z` to classify water ([WaterMaskBuffer.cs:10-33]). I do not see a cull-lift split in this path.

- **(2) Mesh resource lifecycle: no obvious leak in this code.** `Create()` allocates one `GameObject`, `MeshFilter`, `MeshRenderer`, one `Mesh`, and one per-renderer material copy ([WaterSurface.cs:26-52]). `Destroy()` tears down the mesh, the instance material, the GO, and the static shared material/template, then clears the static latches ([WaterSurface.cs:55-69]). The lifecycle hook in `VoxelFrameDriver.LateUpdate()` only toggles on state changes ([VoxelRender.cs:548-586]), so I do not see a steady-state allocation leak here.

- **(3) First water draw can stall on shader load: yes.** `EnsureMaterial()` performs a synchronous `Resources.Load<Shader>("Shaders/WaterGerstner")` on the main thread and immediately creates/configures a material if it succeeds ([WaterSurface.cs:140-198]). That call is reached from `Create()` during world-load/enable transitions ([WaterSurface.cs:26-49]), so the first water enable can pay a load hitch. The shader asset itself is present in `Resources/Shaders/WaterGerstner.shader` and is documented as a `Resources.Load` target ([WaterGerstner.shader:1-6]).

- **(4) Vanilla background/sky interaction: water draws after the background, not before it.** The active 3D camera is cleared with `CameraClearFlags.Skybox` ([3DCamera.cs:114-117]), and the procedural sky shader is tagged `Queue="Background"` with `ZWrite Off` ([ProceduralSky.shader:22-28]). Water is tagged transparent and forced to render queue 3000, with `ZWrite On` ([WaterGerstner.shader:30-35], [WaterSurface.cs:238-239]). Net: the sky/background pass renders first; water overlays it where the depth test allows. I did not find a separate vanilla background-tile override in this repo.

## Bottom Line

Phase 4 water is structurally sound on lifecycle/leak risk and does not show the cull-lift bug pattern. The main audit concern is the synchronous shader/material bring-up on the first enable, which can hitch the frame.
