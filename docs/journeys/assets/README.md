# Journey asset capture protocol

This folder holds screenshots and GIFs referenced inline from the five
journey docs under [`docs/journeys/`](..). Each journey has a sibling
subfolder named after the journey's slug; assets are referenced from the
markdown with relative paths like `./assets/install-and-play/02-mod-loaded.png`.

Binary assets are **not** checked in by the agent — they're meant to be
captured by a human (or a desktop-equipped agent) following the steps in
each journey end-to-end. VitePress emits a warning for missing images at
build time but the build still succeeds.

## Capture protocol

1. **Fresh install.** Start from a clean WorldBox install (or revert your
   `Mods/` folder to vanilla NeoModLoader). This ensures the screenshots
   match what a brand-new user would see.
2. **Follow the journey verbatim.** Open the corresponding journey doc and
   walk through each numbered Step. Stop at every Step that has an
   image/GIF reference inline.
3. **Capture the labelled moment.** The README inside each journey's
   subfolder lists exactly what each filename is supposed to show.
4. **Save under the journey's slug.** Use the exact filename listed.
   Overwrites are fine — just keep one canonical capture per slot.

## Recommended dimensions

| Asset type      | Format | Dimensions          | Target size |
|-----------------|--------|---------------------|-------------|
| Screenshot      | PNG    | 1280 × 720 (16:9)   | < 2 MB      |
| Animated GIF    | GIF    | 800 × 450 (16:9)    | < 2 MB      |
| Code / log shot | PNG    | 1280 × 720 or 1280 wide, crop tight | < 1 MB |

GIFs should be 2–6 seconds long, ~10–15 fps. Tools that work well:
[ScreenToGif](https://www.screentogif.com/) on Windows,
[Peek](https://github.com/phw/peek) on Linux, native macOS screen recorder
+ `ffmpeg` for compression.

## Before/after panels

Several journeys describe a visible change (sprite → voxel, upstream →
fork). Those have a `XX-before-after.png` slot that should be a
side-by-side composite — two screenshots stitched horizontally with a
1-px divider, labelled `BEFORE` / `AFTER` in the top-left of each half.

## Per-journey index

- [`install-and-play/`](./install-and-play/) — fresh install through first
  voxel actor on screen.
- [`contribute-a-phase/`](./contribute-a-phase/) — orientation through PR
  merge.
- [`extend-via-api/`](./extend-via-api/) — downstream mod authoring against
  `WorldSphereAPI.dll`.
- [`diagnose-perf/`](./diagnose-perf/) — profiler flag through Unity
  Profiler attach.
- [`upgrade-from-upstream/`](./upgrade-from-upstream/) — coexistence,
  migration log, before/after.
