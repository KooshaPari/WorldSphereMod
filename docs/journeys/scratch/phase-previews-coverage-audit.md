# Phase Preview Coverage Audit

Audit scope: `docs/journeys/phase-previews/phase-{1..10}-*/before.png` and `after.png`.

## Coverage

- All 10 phases are represented.
- No canonical `before.png` / `after.png` files are missing in the phase preview set.

## Size / Dimension Check

- Most canonical PNGs are consistent at `384x384`.
- File sizes are broadly in family with the expected before/after pattern: `before.png` files are ~1.4 KB to ~4.6 KB, while `after.png` files are ~8.6 KB to ~38.0 KB.
- One outlier exists: `phase-4-mesh-water/before.png` is `32x32` and `1400` bytes, while the rest of the canonical before images are `384x384`.

## Distinct-Hash Check

- The canonical before/after set alone has `17` distinct SHA-256 hashes (`7` distinct `before.png` files and `10` distinct `after.png` files).
- The actual `journeys-gate` fixture list in `.github/workflows/journeys-gate.yml` includes extra `phase-6` and `phase-8` PNGs and reaches `24` distinct hashes total.
- The gate threshold is `>= 8` distinct hashes, so this set passes with clear margin.

## Visual Distinctness

- The before/after pairs are visually distinct enough for the distinct-hash gate to be stable.
- Reused `before.png` content across a few phases does not threaten the gate because the workflow counts the full fixture list, and the after images contribute enough variety.

