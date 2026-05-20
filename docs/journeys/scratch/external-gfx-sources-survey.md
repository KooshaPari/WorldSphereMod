# External Graphics Sources Survey — WorldSphereMod3D

Date: 2026-05-20
Scope: Identify ready-to-import sources of textures, shaders, HDRIs, models, and post-processing that can drop into the Unity 2022.3 voxel pipeline WITHOUT writing each from scratch. Mod ships free on GitHub, so redistribution must be unambiguous.

---

## TL;DR — Top 5 to Integrate First

1. **Poly Haven (HDRIs + textures + models)** — CC0, single-source, covers categories 1+2+5 at once. Integrate first.
2. **ambientCG (formerly CC0Textures)** — CC0, ~2000+ PBR materials, biome-aligned (rock/dirt/water/snow). Largest single PBR firehose.
3. **keijiro's GitHub (Kino, Klak, ShaderSketches)** — Unlicense / MIT. Drop-in URP/HDRP post FX + creative shader patterns. Best Unity-native code source.
4. **OpenGameArt voxel section + MagicaVoxel community packs** — mixed CC0/CC-BY, ~84 dedicated voxel packs + thousands of supporting assets. Highest aesthetic fit for voxel art direction.
5. **3dtextures.me** — CC0, ~1300+ textures with strong fabric/wood/floor coverage that ambientCG is thinner on. Filler for category 5.

Everything else below is supplemental or deferred.

---

## 1. Texture Packs

### Poly Haven Textures — RECOMMENDED
- URL: https://polyhaven.com/textures
- License: **CC0 1.0** — full redistribution, commercial use, no attribution required. Confirmed via polyhaven.com/license.
- Inventory: ~800 PBR texture sets (8K source), all channels (diffuse/normal/rough/AO/disp).
- Size: 8K sets ~200-400 MB each; pre-pack at 1K-2K (~5-20 MB) for mod redistribution. Plan a curated subset (~40-60 textures, ~500 MB packed) shipped via release artifact, NOT in git.
- Integration cost: LOW. Unity's standard URP Lit shader consumes these directly. Author one ScriptableObject `BiomeTextureLibrary` that maps biome id → albedo/normal/MRA triplet.
- Quality: AAA scan-grade. Best in class.

### ambientCG (formerly CC0Textures.com) — RECOMMENDED
- URL: https://ambientcg.com
- License: **CC0 1.0** confirmed at docs.ambientcg.com/license — "copy, modify, distribute and perform the assets, even for commercial purposes, all without asking permission."
- Inventory: ~2000+ PBR materials (largest CC0 PBR set on the web). Has terrain-relevant collections: Ground/, Rock/, Snow/, Bark/, Bricks/.
- Size: 1K JPG variants ~5 MB each, 2K PNG ~30 MB. API available for automated download.
- Integration: LOW. Same Unity URP Lit pipeline.
- Quality: High; slightly less photographic than Poly Haven but more breadth.

### 3dtextures.me — RECOMMENDED (filler)
- URL: https://3dtextures.me
- License: **CC0** confirmed on about page; redistribution allowed.
- Inventory: ~1300+ textures, strongest on metal (346), fabric (207), wall (248), tiles, stone, wood.
- Integration cost: LOW.
- Quality: Mid-to-high. Fills gaps the others miss (stylized fabric, ornate tiles).

### Minecraft texture packs (BDcraft / Patrix / Faithful) — AVOID FOR REDISTRIBUTION
- BDcraft Sphax PureBDcraft: free for personal use only; **no redistribution** in mods.
- Patrix: paid Patreon, all rights reserved.
- Faithful: CC-BY-NC-SA 4.0 — non-commercial; mod itself is free but the NC + SA virality is a footgun.
- "Minecraft texture pack open source" GitHub results are mostly Faithful forks under CC-BY-NC-SA and unsuitable.
- **Recommendation:** Skip. Use Poly Haven + ambientCG + 3dtextures instead.

### Quixel Megascans — CONDITIONAL
- URL: https://quixel.com / https://fab.com
- License post-Fab-transition (2024-2026): Fab Standard License. Free Megascans require a Fab account; redistribution as raw assets inside a public mod repo is **disallowed** under Fab Standard. You CAN ship baked/modified derivatives.
- Recommendation: Use only for one-off baked atlases (capture in Substance, export the final atlas, ship the derivative). Avoid raw redistribution to stay clean.

---

## 2. HDRI / Cubemap Sources

### Poly Haven HDRIs — RECOMMENDED (primary)
- URL: https://polyhaven.com/hdris
- License: CC0.
- Inventory: ~700 HDRIs up to 16K. Categories include skies (clear/cloudy/sunset/night), studio, outdoor nature.
- Mod sizing: ship 4K EXR (~25 MB each) or 2K (~6 MB) — pick 8-12 covering day/dusk/night/storm/biome variants (~80-200 MB total).
- Integration: Unity skybox + URP HDRI sky in Volume. Trivial.
- Quality: industry standard.

### sIBL Archive (Hyperfocal/HDRLabs) — SUPPLEMENTAL
- URL: http://www.hdrlabs.com/sibl/archive.html
- License: CC-BY-NC-SA in most cases — **non-commercial only**. Skip for safety even though mod is free; SA virality is a problem.

### HDRI-Haven — Same as Poly Haven (merged in 2020). Not a separate source.

---

## 3. Open-Source Shader Packs

### keijiro Takahashi (github.com/keijiro) — STRONGLY RECOMMENDED
- Repos (Unlicense / MIT, ~919 repos, ~2-4k stars each on top ones):
  - **Kino** — collection of post-FX (Streak/anamorphic bloom, Recolor, Overlay, Glitch, Sharpen, Slice, Test Card). HDRP-targeted; ports cleanly to URP.
  - **Klak** — creative-coding library, noise generators, modulation.
  - **Skinner** — VFX from skinned meshes (good for actor death/spawn effects).
  - **Pugrad / Smrvfx / Lasp** — gradient ramps, audio-reactive, particle.
- License: Unlicense (public domain) — confirmed on Kino repo.
- Integration cost: MEDIUM. Some are HDRP-only; pulling shader source and porting hlsl → URP costs ~hours per effect, not days. Already production-grade Unity code.

### Minecraft shader packs (BSL, Sildur's, Complementary)
- All confirmed **All Rights Reserved** / proprietary. BSL is closed-source. Complementary builds on BSL.
- They're also OptiFine/Iris GLSL specific — would need full rewrite into Unity HLSL anyway.
- **Recommendation:** Useful only as visual reference. Do not attempt to port code.

### Shadertoy
- Per-shader license; most are CC-BY-NC-SA by default.
- Use as algorithm reference (cloud noise, water, atmospheric scattering) and reimplement in Unity HLSL — no direct lift.

### awesome-shaders / awesome-unity lists
- The community lists themselves curate keijiro, Cyanilux, Catlike Coding, and Unity's own URP samples. After keijiro, the next two are:
  - **Cyanilux Unity Shaders** (github.com/Cyanilux) — MIT, URP Shader Graph nodes + HLSL custom functions.
  - **Catlike Coding tutorials** — code is permissive (CC-BY-4.0 on tutorials, code MIT-style); excellent reference, not a binary lib.

---

## 4. Voxel Models / OpenGameArt

### OpenGameArt — RECOMMENDED
- URL: https://opengameart.org
- License mix: CC0, CC-BY 3.0/4.0, CC-BY-SA, OGA-BY, GPL. Filter to CC0 + CC-BY for safe mod redistribution.
- Voxel-specific: 84 voxel 3D art entries; thousands of supporting 2D/textures.
- Integration: MagicaVoxel `.vox` files → existing WSM3D voxel importer. Direct fit for the pipeline.
- Recommendation: pull a curated ~30-asset bundle (creatures, props, vehicles, biome decoration) under CC0/CC-BY only.

### Voxelmade gallery (voxelmade.com)
- Mostly portfolio showcase. No mass-download license; most assets are commissioned/proprietary. Skip.

### Itch.io free voxel asset packs
- Per-pack license; many are CC0 ("Kenney" — kenney.nl — is the gold standard).
- **Kenney.nl** — CC0, hundreds of low-poly + voxel packs (creatures, vehicles, nature, dungeon). RECOMMENDED as supplement #6.

---

## 5. PBR Material Libraries

Covered above (ambientCG + 3dtextures + Poly Haven). Additional:

### Adobe Substance 3D Community Assets
- License: Substance 3D Asset license — free with Substance subscription; redistribution as Substance source files restricted. Baked outputs ok in some tiers, but the EULA is fiddly. Avoid for mod redistribution.

### Material Maker / FreePBR.com
- FreePBR: CC0 on most, ~500 materials. Lower quality than ambientCG. Optional.
- Material Maker (godotengine community): MIT tool, outputs your own materials — useful for procedural generation, not as a library.

---

## 6. Game-Graphics QoL

### Unity Asset Store free tier
- "Free for any Unity project" includes redistribution as compiled assets inside mods/games. Relevant freebies:
  - **Post Processing Stack v2** (deprecated but URP-compatible bridge).
  - **URP Sample Scenes** — Unity Companion License, redistributable in builds.
  - **Lens Flare presets** — Unity-published, free.
- License caveat: third-party free assets often forbid raw redistribution. Read each EULA.

### awesome-unity (github)
- Lists for tooling (Mirror, UniRx, UniTask), not directly graphics. Useful adjacent.

### Marmoset Toolbag presets
- Proprietary; for material previewing only. Skip.

### Polygon Runway / Synty (paid)
- Proprietary, paid. Skip.

---

## Ranked Recommendation Summary

| Rank | Source | License | Category | Size Budget | Integration |
|------|--------|---------|----------|-------------|-------------|
| 1 | Poly Haven | CC0 | Textures + HDRIs + Models | 200-500 MB curated | Low |
| 2 | ambientCG | CC0 | PBR materials | 200-400 MB | Low |
| 3 | keijiro repos | Unlicense | Post-FX shaders | <50 MB code | Medium |
| 4 | OpenGameArt (CC0/CC-BY only) | CC0/CC-BY | Voxel models | 50-100 MB | Low |
| 5 | 3dtextures.me | CC0 | Filler textures | 50-150 MB | Low |
| 6 | Kenney.nl | CC0 | Voxel/low-poly packs | 50-200 MB | Low |
| 7 | Cyanilux shaders | MIT | URP shader nodes | <20 MB | Medium |
| 8 | Unity URP samples | Unity Companion | Reference + presets | <100 MB | Low |

---

## Integration Plan (concrete next steps)

1. Create `Assets/External/` with subdirs `PolyHaven/`, `AmbientCG/`, `OpenGameArt/`, `Kenney/`, `Keijiro/`. Add a `LICENSES.md` per subdir mirroring the CC0/Unlicense text.
2. Ship external assets via GitHub Release artifact (`wsm3d-external-pack-v1.zip`), NOT inside the git tree. install.ps1 already downloads release artifacts — extend it to fetch + extract the pack into `Assets/External/`.
3. Author `ScriptableObject` libraries: `BiomeTextureLibrary`, `HDRILibrary`, `VoxelModelLibrary` — maps logical IDs → asset references. Lets phase 5/8 lighting code reference textures by ID and tolerate hot-swap.
4. Port Kino Streak + Recolor first (highest visual impact / lowest LOC). Both already exist as standalone .compute + .shader pairs in keijiro/Kino — copy-paste with namespace rename.
5. License audit script: `Tools/audit-external-licenses.ps1` scans `Assets/External/**/LICENSES.md` and fails CI if any non-CC0/CC-BY/MIT/Unlicense file appears. Prevents accidental Faithful-CC-BY-NC-SA infection.

## Risk Notes

- **Repository bloat**: Do NOT commit binary asset packs to git. Release artifact + install.ps1 fetch is the only sane path for ~500 MB of textures.
- **CC-BY attribution**: For any CC-BY (not CC0) asset, ship `THIRD_PARTY_NOTICES.md` listing source URL + author. Auto-generate from LICENSES.md frontmatter.
- **Megascans temptation**: Tempting because of quality, but Fab Standard License + raw redistribution is a legal trap. If used, bake in Substance and ship only the derivative atlas with a note in NOTICES that no raw Quixel files are redistributed.
- **HDRP-only keijiro effects**: Kino targets HDRP 7.1+; our pipeline is URP. Budget 2-6 hours per effect to port hlsl includes and Volume Component to URP equivalents.

---

## Word count: ~1450 words.
