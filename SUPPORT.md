# Support

## Asking questions

| You want to… | Best place |
|---|---|
| Report a bug in the mod | Open an issue on https://github.com/KooshaPari/WorldSphereMod |
| Suggest a feature | Same — open an issue and tag `enhancement` |
| Get install help | Read `README.md` → `Installation`. Run `./Tools/install.ps1 -h` |
| Discuss design / contribute | Read `docs/CONTRIBUTING.md` then comment on PR #1 |
| Report a security issue | See `SECURITY.md` — email, do **not** file a public issue |

## Before reporting

1. Update to the head of `claude/research-ultraplan-fork-DdgI5`. The branch
   moves fast; many issues are already fixed.
2. Run `./Tools/install.ps1` to refresh your install — verify the issue
   reproduces on a clean install.
3. Check the WorldBox console (default backtick key) for `[WorldSphereMod3D]`
   log lines. Copy them into the report.
4. Note which `SavedSettings` flags are on — every phase is feature-flagged.

## Compatibility

- **WorldBox version**: track upstream. Pin to a specific build if you hit
  Harmony patch-signature errors after a WorldBox patch.
- **NeoModLoader**: any recent NML build with `Assembly-CSharp-Publicized.dll`.
- **Hardware**: GPU instancing is the hard requirement. Compute shader / indirect
  args are nice-to-have — without them the mod loads in impostor-only mode
  (`LodSelector.ImpostorOnlyMode = true`).
- **Coexistence**: ships under GUID `worldsphere3d.fork` so it can be installed
  alongside upstream `MelvinShwuaner/WorldSphereMod`. Enable only one at a time
  in NML.

## When the answer is "Phase X"

This fork is implemented in phases, each gated behind a `SavedSettings` flag.
If a feature doesn't appear, check `docs/HANDOFF.md` or the phase table in
`README.md` for whether that phase has shipped + whether its default flag is on.
