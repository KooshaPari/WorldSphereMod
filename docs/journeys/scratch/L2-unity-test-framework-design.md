# L2 Unity Test Framework Design

Scope: real-Unity tests for WSM3D via Unity Test Framework in `-batchmode`,
without launching `WorldBox.exe`. This is a design note only.

## 1) Editor Version

Use **Unity 2022.3 LTS**. That matches the WorldBox runtime family and is the
safe choice for shader/material/AssetBundle work. Do not target 2021.3 or Unity
6 for this harness: the repo already treats Unity 2022.3 as the bundle-bake
boundary, and the AssetBundle/shader path is version-sensitive.

Practical rule: pin the harness to a specific 2022.3 patch on the test
machine, but keep the design aligned to the 2022.3 stream so CI can be
recreated later.

## 2) Test Scene Shape

Use one minimal test scene, not a full game world.

Recommended scene contents:

- A single camera with a known clear flag and a controlled `cullingMask`.
- One directional light.
- A bootstrap object that loads the WSM3D bundle and material assets used by
  `Core.LoadAssets()` (`CompoundsphereMesh`, `CompoundSphereMaterial`,
  `SkyBox.mat`, and any Phase 5 shader assets).
- One voxel test mesh on a dedicated render layer.
- One occluder / empty-space probe object so visibility can be asserted both
  "visible" and "not visible".

Recommended checks in the scene:

- bundle contains the expected assets before any render test starts;
- `Material.enableInstancing` is set on the voxel material;
- mesh bounds are sane and non-zero;
- camera layer filtering is respected when the same object is moved between
  visible and hidden layers;
- render output changes when the object is removed or masked out.

If the test needs an image-level assertion, render into a small
`RenderTexture` and compare a hash or a small set of sampled pixels rather than
doing full-frame image diff.

## 3) Edit Mode vs Play Mode

### Edit Mode

Best for fast asset and contract tests:

- bundle existence and asset names;
- shader keyword/variant presence on material objects;
- material defaults and import-time settings;
- pure C# setup code around scene bootstrap.

Limitations:

- no real scene simulation loop;
- no trustworthy camera culling or frame-to-frame render behavior;
- poor fit for "did it actually draw" questions.

### Play Mode

Best for real visibility checks:

- scene loads like the game would load it;
- camera culling, render order, and layer interactions are real;
- instancing and shader compilation are exercised by an actual render pass;
- render-target assertions are meaningful.

Tradeoff:

- slower and more fragile;
- needs a graphics-capable Unity Editor session even in batch mode;
- any test that depends on `Camera.cullingMask`, instancing, or shader
  fallback behavior belongs here.

Recommendation: split the suite. Keep asset/contract tests in Edit Mode and
visibility tests in Play Mode.

## 4) CI Shape

Run Unity in `-batchmode`, but not as a pure `-nographics` job for the render
tests. The CI machine still needs a real graphics backend.

Suggested CI layout:

- Job 1: Edit Mode tests with `-runTests -testPlatform EditMode`.
- Job 2: Play Mode visibility tests with `-runTests -testPlatform PlayMode`.
- Export NUnit XML test results as artifacts.

Licensing:

- There is no separate Unity Test Framework license.
- The machine running Unity needs a valid Editor license activation.
- Unity Hub supports Personal, named-user, serial-based, and floating-license
  activations.
- If the team wants hosted CI, Unity Build Automation is an option; Unity’s
  supported-version list includes 2022.3.

## 5) What L2 Catches That L1 Misses

L1 can validate C# contracts and install-time invariants. L2 catches real-Unity
failures such as:

- missing instancing variants in the actual loaded material;
- shader compile/import errors that only appear in the Editor;
- `Camera.cullingMask` / layer interactions that are invisible to pure unit
  tests;
- bundle load failures caused by bad asset names or bad import state;
- render-path regressions where a mesh exists but does not actually become
  visible.

Concrete examples from the mod code:

- `MeshInstanceBatcher.Flush()` has explicit instancing fallback and camera
  diagnostics, so L2 can prove the fast path really draws and the fallback
  really recovers.
- `Core.LoadAssets()` loads bundle assets by name, so L2 can catch bad bundle
  contents early.
- `3DCamera.CameraManager.Begin()` creates the actual camera used by the mod,
  so Play Mode can validate the live render target instead of just object
  construction.

## Cost Estimate

Expected effort: **3 to 5 engineering days**.

- 0.5 day: install/pin Unity 2022.3 and create the Unity test project.
- 1 day: author the bootstrap scene and bundle/material loader.
- 1 to 2 days: add Edit Mode and Play Mode tests.
- 0.5 to 1 day: wire CI, test-result export, and license handling.
- Optional extra time if a dedicated Windows runner or GPU-backed runner is
  needed for stable Play Mode rendering.

Net: L2 is worth doing if the goal is to catch real render regressions before
manual smoke testing, but it is not a cheap layer. It should stay focused on
high-value visibility and shader-path checks, not general gameplay coverage.
