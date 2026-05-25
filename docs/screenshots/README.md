# Phase screenshots (manual + PlayCUA sync)

PlayCUA desktop captures are synced here by:

```powershell
pwsh Tools/sync-playcua-screenshots.ps1
```

Files are **gitignored** (local proof only). Expected slugs after `run-all`:

| Phase | Files |
|------|--------|
| 1 | `phase-1-actors.png` |
| 2 | `phase-2-buildings.png` |
| 3 | `phase-3-foliage.png`, `phase-3-clouds.png` |
| 4–10 | `phase-4-water.png` … `phase-10-lod-ladder.png` |

Human gate: load **save slot 2**, populated kingdom, 360° camera — confirm visuals match `docs/smoke-test-phase*.md`.
