# Charter — WorldSphereMod3D

## Mission

Finish the 3D conversion of WorldBox **without losing the pixel-art
identity**. Every visible entity — actors, buildings, foliage, water,
effects, UI — becomes a real mesh; the look stays unmistakably WorldBox.
Ship as a hard fork that co-installs alongside upstream, degrades
gracefully on weak hardware, and stays approachable for the next
contributor (human or agent) walking in cold.

## Tenets

1. **Visual identity preservation is non-negotiable.** Voxelization,
   procgen, and crossed-quad foliage are all *mesh* techniques chosen
   specifically because they preserve the original sprite silhouette and
   palette. Any rendering change MUST keep the WorldBox look from any
   camera angle. Side-by-side screenshots are PR-mandatory for any
   rendering-affecting change.

2. **Feature flags are load-bearing.** Every phase ships behind a
   `SavedSettings` flag, defaults OFF, and flips ON only after in-game
   smoke test ([ADR-0005](./docs/adr/0005-default-on-flags-per-phase-ship-gate.md)).
   Flipping any flag OFF must not break another phase. No phase silently
   takes another phase down with it.

3. **Co-installability with upstream is a hard requirement.** GUID stays
   `worldsphere3d.fork`. Both mods must be installable side-by-side; the
   user enables exactly one in NeoModLoader. We do not break upstream
   users.

4. **API backwards compatibility is forever.** The `WorldSphereAPI` v1
   surface is signature-frozen. v2 additions are no-ops on v1 hosts. The
   `WorldSphereTester/` regression mod is the gate; if it breaks, we
   broke compatibility.

5. **The hardware gate is a fallback, not a wall.** Users on hardware that
   fails the compute-shader / instancing / indirect-args gate get the
   impostor-billboard LOD path, not a red icon and a do-nothing mod. The
   gate softens to `ImpostorOnlyMode = true`; it does not throw.

6. **Caches drain on world unload, in one place.** A single Harmony Prefix
   on `Core.Sphere.Finish` (`WorldUnloadPatch.cs`) drains every
   fork-side cache. New caches register there. Anything else leaks across
   reloads.

7. **Comments capture *why*, not *what*.** The code says what it does.
   Comments are reserved for non-obvious invariants, workarounds, and
   hidden constraints (z-displacement sentinel, cylindrical X-wrap,
   parallel-render-pass thread-safety, `_hasOriginals` one-shot,
   `MapBox.world_time` reflection-probe fallback). When in doubt, no
   comment.

8. **Spec roots live at repo root.** `PRD.md`, `SPEC.md`, `ADR.md`,
   `PLAN.md`, `FUNCTIONAL_REQUIREMENTS.md`, `RESEARCH.md`, `SOTA.md`,
   `CHARTER.md` — all at root, per Phenotype convention. Agents joining
   cold find them without exploring.

## Stewardship

Solo-author fork (`@KooshaPari`). PRs go to
`claude/research-ultraplan-fork-DdgI5`, not `main`. One PR per phase. CI
on the Unity-free `WorldSphereAPI.csproj`; full-mod build is local-only
(needs WorldBox reference DLLs).

> See [`PRD.md`](./PRD.md) for product framing, [`SPEC.md`](./SPEC.md) for
> the system-level technical spec, [`PLAN.md`](./PLAN.md) for the 10-phase
> implementation plan.
