# Phase 5 prep — Compound-Spheres-3D submodule + Unity version

Research notes for the Phase 5 backend rebuild (per-vertex normals, water mask).
Not implementation work yet — context for whoever picks up Phase 5.

## Upstream backend: `MelvinShwuaner/Compound-Spheres`

- URL: https://github.com/MelvinShwuaner/Compound-Spheres
- Last push: 2026-03-29
- Stars: 0, no license set (treat as proprietary until Melvin clarifies)
- Built for **Unity 2022.3** per the README ("other versions might be incompatible")
- 7 C# files, single .csproj, no Unity Asset folder — meant to be imported into a Unity project

Layout:
```
CompoundSpheres.sln
CompoundSpheres/
  BufferUtils.cs
  CompoundSpheres.csproj
  DefaultSettings.cs
  SphereManager.cs
  SphereManagerSettings.cs
  SphereRow.cs
  SphereTile.cs
Default Assets/
README.md
```

## What the API looks like

`SphereManager.Creator.CreateSphereManager(rows, cols, SphereManagerSettings, name)` returns a manager. `SphereManagerSettings` carries delegates for per-tile position/rotation/scale/color/texture-index plus a list of `IBufferData` for custom per-tile buffers (e.g. `CustomBufferData<Vector3>("AddedColors", 12, callback)`).

The mod already wires this up in `Core.cs:294-346` (`Begin()`) and `Core.cs:407-413` (`LoadAssets()`).

## Phase 5 deliverables that touch this backend

Per `docs/PLAN.md` lines 122-132:

1. **Per-vertex normals on terrain mesh.** The vendored `CompoundSpheres.dll` currently produces an unlit-shaded mesh (no per-vertex normals). Need to either:
   - Add a custom buffer for vertex normals (computed once per tile from the height field).
   - Modify the shader to derive normals via screen-space derivatives (cheap but lower quality).
   - The cleanest path is the custom buffer route — fits the existing `IBufferData` extensibility hook.

2. **Water-mask SSBO** (Phase 4 prereq). A `CustomBufferData<float>` for `sea_level - tile_height` per tile, consumed by a separate water mesh layer that overlays the terrain.

3. **Material slider `_NormalStrength`** — material-level concern, lands with the rebuilt shader.

## Submodule plan

Per `PLAN.md:48-49` and `docs/phase5-architecture.md` §8–9:

- Hard-fork upstream to **`KooshaPari/Compound-Spheres-3D`** (separate from the existing upstream pin at `External/Compound-Spheres/`).
- Add the fork as a git submodule at `External/Compound-Spheres-3D/`.
- Build in Unity **2022.3**, binary-diff against `WorldSphereMod/Assemblies/CompoundSpheres.dll`, then flip `WorldSphereMod.csproj` from vendored `<Reference>` to `<ProjectReference>` (gated by `$(UseSubmoduleBackend)` until parity is proven).

Keep `External/Compound-Spheres` on upstream `MelvinShwuaner/Compound-Spheres` for read-only diffs until the fork owns our patches (`docs/ci-mod-compile-gap.md`).

---

## Step-by-step: fork and clone `Compound-Spheres-3D`

Upstream: https://github.com/MelvinShwuaner/Compound-Spheres  
Fork (placeholder — create on GitHub first): **`https://github.com/KooshaPari/Compound-Spheres-3D`**

### 1. Create the fork on GitHub

1. Sign in to GitHub as **KooshaPari** (or the org that will own Phase 5b).
2. Open https://github.com/MelvinShwuaner/Compound-Spheres → **Fork** → your account.
3. **Settings → General → Repository name** → rename to `Compound-Spheres-3D` (matches `PLAN.md` and `External/Compound-Spheres-3D/` path).
4. Confirm the default branch is `main` and matches upstream history (parent repo currently pins submodule SHA `73a7b77` on upstream `main`).

### 2. Clone the fork locally (one-time setup)

```powershell
# Outside WorldSphereMod — scratch clone to verify fork contents
git clone https://github.com/KooshaPari/Compound-Spheres-3D.git
cd Compound-Spheres-3D
git remote add upstream https://github.com/MelvinShwuaner/Compound-Spheres.git
git fetch upstream
git log -1 --oneline main
# Expect same tree as upstream main at pin time (73a7b77 or later after intentional sync)
```

Optional: apply the local `SetTexture` guard (`dd78b11`, see `docs/ci-mod-compile-gap.md`) on the **fork** as the first divergent commit before submodule add.

### 3. Open in Unity 2022.3 (smoke build)

1. Install Unity **2022.3 LTS** (see [Unity version risk](#unity-version-risk) below).
2. Hub → **Add** → select the fork clone folder (contains `CompoundSpheres.sln` / `CompoundSpheres/`).
3. Point `CompoundSpheres.csproj` Unity references at your WorldBox install (`worldbox_Data/Managed/UnityEngine.CoreModule.dll`) or use the fork’s existing HintPath after editing for your machine.
4. Build / let Unity compile → confirm `CompoundSpheres.dll` is produced (location depends on Unity output settings; copy path is recorded in the DLL swap checklist).

Do **not** delete `WorldSphereMod/Assemblies/CompoundSpheres.dll` until the parity diff passes.

---

## Step-by-step: add git submodule to WorldSphereMod

Run from the **WorldSphereMod repo root** (`C:\Users\koosh\Dev\WorldSphereMod`). Replace the URL after the fork exists.

```powershell
cd C:\Users\koosh\Dev\WorldSphereMod

# 1. Ensure working tree is clean (submodule add refuses otherwise)
git status

# 2. Add fork as submodule (URL placeholder — update when fork is live)
git submodule add https://github.com/KooshaPari/Compound-Spheres-3D.git External/Compound-Spheres-3D

# 3. Pin to upstream-equivalent commit on first add (example SHA — use fork's main tip)
cd External/Compound-Spheres-3D
git checkout 73a7b77
cd ../..

# 4. Record pointer in parent repo
git add .gitmodules External/Compound-Spheres-3D
git commit -m "chore(external): add Compound-Spheres-3D submodule (fork pin)"
```

Expected `.gitmodules` entry (second submodule; keep existing `External/Compound-Spheres` block):

```ini
[submodule "External/Compound-Spheres-3D"]
	path = External/Compound-Spheres-3D
	url = https://github.com/KooshaPari/Compound-Spheres-3D.git
```

**Clone for other contributors:**

```powershell
git clone --recurse-submodules https://github.com/<org>/WorldSphereMod.git
# or after a normal clone:
git submodule update --init --recursive External/Compound-Spheres-3D
```

**Push fork + parent:** push the fork’s `main` first, then push WorldSphereMod with the updated submodule pointer. Use `git push --no-recurse-submodules` on the parent if the fork remote is not ready yet (`docs/ci-mod-compile-gap.md`).

---

## DLL swap checklist

Use this list when moving from vendored `WorldSphereMod/Assemblies/CompoundSpheres.dll` to a submodule-built DLL. Do not flip production defaults until every checked item passes.

### Prerequisites

- [ ] Unity **2022.3 LTS** installed and used to build the fork (not 2021.3 / Unity 6).
- [ ] `External/Compound-Spheres-3D/` submodule added and checked out at the intended commit.
- [ ] Shader bake infrastructure green: `dotnet test tests/WorldSphereMod.Tests.Integration/` (see [Integration tests](#integration-tests) below).

### Build and parity

- [ ] Build `External/Compound-Spheres-3D/CompoundSpheres/CompoundSpheres.csproj` (Unity editor or `dotnet build` with WorldBox `Managed/` HintPaths).
- [ ] Copy build output to a scratch path, e.g. `Tools/.build/CompoundSpheres.dll`.
- [ ] **Binary diff** against vendored baseline:
  ```powershell
  fc /b Tools\.build\CompoundSpheres.dll WorldSphereMod\Assemblies\CompoundSpheres.dll
  # Or: CertUtil -hashfile ... SHA256 on both — must match for no-op fork pin
  ```
- [ ] If hashes differ, load a known `worldsphere` AssetBundle in-game and confirm terrain renders identically before proceeding (visual parity gate from `PLAN.md` Phase 0).

### `WorldSphereMod.csproj` (gated swap)

Current vendored reference (`WorldSphereMod.csproj`):

```xml
<Reference Include="CompoundSpheres">
  <HintPath>WorldSphereMod\Assemblies\CompoundSpheres.dll</HintPath>
</Reference>
```

- [ ] Add property (default **false** until parity): `<UseSubmoduleBackend>false</UseSubmoduleBackend>`.
- [ ] When `UseSubmoduleBackend` is **true**, use `<ProjectReference Include="External\Compound-Spheres-3D\CompoundSpheres\CompoundSpheres.csproj" />` instead of the `HintPath` reference.
- [ ] Confirm `<Compile Remove="External\**" />` remains — mod sources must not double-compile submodule `.cs` files; only the project reference pulls the assembly.
- [ ] Resolve duplicate `AssemblyInfo` issues if MSBuild tries to compile submodule into mod output (documented blocker in `docs/ci-mod-compile-gap.md`); fallback is copy built DLL into `Assemblies/` without `ProjectReference` until conflicts are fixed.

### Ship layout and install

- [ ] `Tools/install.ps1` still **does not** remove `CompoundSpheres.dll` (runtime dep; see `InstallScriptInvariantsTests`).
- [ ] NML still receives `CompoundSpheres` via `Assemblies/` or compatible compile path — **do not** ship a net5+ DLL (Mono CS1705; see `.claude/skills/wsm3d/SKILL.md`).
- [ ] `dotnet build` / `task build` succeeds with `UseSubmoduleBackend=false` (CI default) and with `true` locally after swap.
- [ ] In-game: terrain renders, no NML compile failure, `Core.Begin()` / `LoadAssets()` paths unchanged (`Core.cs` ~294–413).

### After feature work (Phase 5b, not parity-only)

- [ ] Per-vertex normal `CustomBufferData` + `VoxelLit.shader` bundle rebuild (`docs/phase5-architecture.md`).
- [ ] Water-mask buffer in fork; wire `WaterMaskBuffer.cs` SSBO branch (`docs/phase4-architecture.md`).
- [ ] Update vendored `CompoundSpheres.dll` in `Assemblies/` **or** flip `UseSubmoduleBackend=true` permanently; update `HANDOFF.md` phase table.

### Rollback

- [ ] Set `UseSubmoduleBackend=false` (or revert csproj commit).
- [ ] Restore `WorldSphereMod/Assemblies/CompoundSpheres.dll` from git if overwritten.
- [ ] Remove submodule only if abandoning fork: `git submodule deinit -f External/Compound-Spheres-3D` + remove `.gitmodules` entry (separate commit).

---

## Integration tests

CI does not run Unity headless; integration tests guard **repo contracts** only.

| Test file | What it asserts |
|-----------|-----------------|
| `tests/WorldSphereMod.Tests.Integration/BakeInfrastructureIntegrationTests.cs` | `Tools/bake-shaders.ps1` exists; `Tools/Unity-Bake-Project/` + `BakeShaders.BakeAll`; script targets Unity **2022.3** |
| `tests/WorldSphereMod.Tests.Unit/InstallScriptInvariantsTests.cs` | `install.ps1` never deletes `CompoundSpheres.dll` |

Run before opening a Phase 5b PR:

```powershell
dotnet test tests/WorldSphereMod.Tests.Integration/ -c Release
dotnet test tests/WorldSphereMod.Tests.Unit/ --filter "FullyQualifiedName~CompoundSpheres"
```

Related: [`docs/journeys/scratch/unity-version-blocker.md`](journeys/scratch/unity-version-blocker.md) (bundle bake checklist), [`tests/README.md`](../tests/README.md) (tier overview).

## Unity version risk

The README explicitly says Unity 2022.3. This machine has Unity Hub plus:
- `2021.3.45f1`
- `6000.3.11f1` (Unity 6)

**Neither is 2022.3.** AssetBundle compatibility across Unity major versions is fragile (different serialization formats). Risks:
- **Building** the modified Compound-Spheres in 2021 or Unity 6 may or may not produce a DLL the game's Unity 2022 runtime can load.
- **Rebuilding the `worldsphere` AssetBundle** in 2021 produces bundles incompatible with the game's 2022 runtime. Confirmed risk if Phase 5's shader work requires bundle rebuild.

**Action items before Phase 5 starts:**
1. Install Unity 2022.3 LTS via Unity Hub (multi-GB download, user choice).
2. Cut the `External/AssetBundleBuilder/` Unity project as a 2022.3 project.
3. Verify a no-op rebuild of the existing `worldsphere` bundle in 2022.3 produces a binary-identical (or game-loadable) output. Use that as the parity test before any modifications.

## Order of operations for Phase 5

Recommended (each is its own commit; whole phase = one PR):

1. Add `External/Compound-Spheres-3D/` as a submodule pointing at our fork (initially identical to upstream).
2. Build the submodule in Unity 2022.3, verify it produces the same DLL the vendored one ships.
3. Add per-vertex normal buffer + companion lit shader to the submodule.
4. Add water-mask buffer to the submodule (used by Phase 4).
5. Swap the .csproj reference from vendored to submodule.
6. Replace `_material` in `VoxelRender.cs` with a real `VoxelLit.shader` material loaded from the rebuilt bundle.
7. Add `Sun` directional light + 4-cascade shadow config to `CameraManager.Begin()`.
8. Default `SavedSettings.HighShadows = true`. Update phase table.

## Open questions for whoever owns Phase 5

- Should the fork be kept in sync with upstream (cherry-pick Melvin's changes) or diverge fully? Affects how rebase-friendly we keep the patch set.
- Should we contribute the per-vertex normals + water mask back upstream as a PR? Lower-friction for the user; higher-friction for control.
- If Unity 2022.3 install is a hard no, fallback is to keep the vendored DLL and patch shaders only via `MaterialPropertyBlock` overrides — works for some Phase 5 features (basic lighting from screen-space derivatives) but not all (e.g., the water mask needs backend cooperation).
