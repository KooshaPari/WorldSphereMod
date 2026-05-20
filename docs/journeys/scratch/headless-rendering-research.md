# Headless WorldBox rendering research

**Date:** 2026-05-19  
**Scope:** Can WorldBox/WSM3D run for tests without a visible window or GPU?

## Bottom line

WSM3D is not a pure headless app today. `WorldSphereMod/Code/Mod.cs` still throws `IncompatibleHardwareException` when `SystemInfo.supportsInstancing` is false, and only soft-falls back when compute shaders or indirect-args buffers are missing. That means a solution must still present a real Unity graphics device that advertises instancing support.

## Findings

- **Unity `-batchmode -nographics`**
  - Unity docs describe this as desktop headless mode that runs without initializing the graphics device.
  - That is useful for CI or server-style logic, but it is not a renderer. It will not satisfy a mod gate that requires `supportsInstancing`, because no graphics device is created.
  - Verdict: good for non-rendering tests only.

- **Linux Xvfb + Wine + DXVK**
  - Xvfb is just a virtual X server. It gives Wine a display to talk to, but it does not provide GPU capabilities by itself.
  - DXVK is a D3D8/9/10/11-to-Vulkan translation layer and its docs require a Vulkan-capable driver. So this path still depends on a real Vulkan backend on the host.
  - Verdict: workable for windowless Linux execution, but not a true no-GPU path unless the host also has a usable Vulkan software/backend stack.

- **Windows NVIDIA vGPU / WARP**
  - NVIDIA vGPU is virtualized access to supported physical NVIDIA GPUs; it is not a CPU-only renderer.
  - WARP is Microsoft’s software rasterizer for Direct3D. Microsoft says it fully supports Direct3D features and can be used where hardware is unavailable.
  - This is the only option in the list that can plausibly present a real D3D device while staying CPU-backed.
  - Verdict: highest-priority experiment for native Windows headless testing.

- **Docker + Xvfb + Wine**
  - Docker isolates processes; it does not add graphics capability.
  - This is just packaging around the Xvfb/Wine/DXVK path, so it inherits the same GPU/Vulkan limitations.
  - Verdict: useful for reproducibility, not for bypassing the renderer requirement.

## Gate Implication

The mod gate is not “must have a visible window.” It is “must have a graphics device that reports the required capabilities.” A software renderer can pass only if it exposes:

- instancing support, and
- compute shader support, and
- indirect-args buffer support.

`-batchmode -nographics` cannot do that because it suppresses graphics device creation. Xvfb cannot do that because it only provides a display server. DXVK can only do that if the underlying Vulkan stack is available and capable. WARP is the only software path that might actually satisfy the gate.
NVIDIA vGPU can only do that when backed by supported GPU hardware, so it does not solve the no-GPU requirement by itself.

## Ranked Recommendations

1. **Try Windows WARP first.** It is the best fit for “no visible window, no physical GPU” while still giving Unity a real D3D device. Smoke-test the gate in a throwaway build before investing further.
2. **Use `-batchmode -nographics` only for non-rendering tests.** This is appropriate for logic, loading, and save-game workflows that never touch render initialization.
3. **Use Linux Xvfb + Wine + DXVK only if you already have a usable Vulkan backend.** It is a compatibility/container strategy, not a GPU replacement.
4. **Treat Docker + Xvfb + Wine as an operational wrapper, not a solution.** It improves reproducibility, not renderer capability.

## Practical conclusion

If the goal is to run WSM3D tests without a visible window or physical GPU, the software-renderer question is: **maybe WARP, not batchmode/nographics, and not Xvfb alone**. I would not assume WARP passes the gate until the mod is launched once under WARP and `SystemInfo.supportsInstancing`, `supportsComputeShaders`, and `supportsIndirectArgumentsBuffer` are checked in logs.

## Sources

- [Unity manual: Desktop headless mode](https://docs.unity3d.com/ja/current/Manual/desktop-headless-mode.html)
- [Microsoft Learn: Windows Advanced Rasterization Platform (WARP)](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/directx-warp)
- [Microsoft Learn: Compute Shader Overview](https://learn.microsoft.com/en-us/windows/win32/direct3d11/direct3d-11-advanced-stages-compute-shader)
- [NVIDIA Docs: Virtual GPU Software Supported GPUs](https://docs.nvidia.com/vgpu/gpus-supported-by-vgpu.html)
- [DXVK README](https://github.com/doitsujin/dxvk)
- [DXVK driver support wiki](https://github.com/doitsujin/dxvk/wiki/Driver-support)
- [Xvfb manual page](https://man.archlinux.org/man/extra/xorg-server-xvfb/Xvfb.1.en)
