# WSM3D #208 — Water surface flat at sea level

Branch: wip/208-height-fix (9 ahead of origin)
Owner: apex (this chat)
Worker: codex-spark in background

## Hard rules
- DO NOT switch submodule branches; sota/gpu-compute-golive lives in Compound-Spheres submodule on separate branch.
- Decouple water-vertex Y from seabed Y; keep seabed→depth only.
- 1-line comment explaining constant water Y.
- Programmatic test only; no visual claims.
- Commit: `fix(208): water surface flat at sea level, decoupled from seabed`
- FR-render-water-surface

## Status
- [pending] codex dispatched
- [pending] codex: locate coupling site
- [pending] codex: decouple + comment + commit
- [pending] codex: programmatic Y verify (water, seabed, depth on deep + shallow tile)
- [pending] report SHA + 3 Y values + next blocker
