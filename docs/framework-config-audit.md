# Framework Config Audit

Scope: NML / BepInEx / WorldBox settings that affect `WorldSphereMod3D` loading, logging, performance, and the on-screen console.

Read-only audit date: 2026-05-28.

## 1. BepInEx `BepInEx.cfg`

Source: `C:\Program Files (x86)\Steam\steamapps\common\worldbox\BepInEx\config\BepInEx.cfg`

Observed values:

- `[Logging] UnityLogListening = true`
- `[Logging.Console] Enabled = false`
- `[Logging.Console] LogLevels = Fatal, Error, Warning, Message, Info`
- `[Logging.Disk] WriteUnityLog = false`
- `[Logging.Disk] Enabled = true`
- `[Logging.Disk] LogLevels = Fatal, Error, Warning, Message, Info`

Findings:

- `UnityLogListening = true` mirrors Unity log messages into BepInEx.
- This does not create fixed per-frame I/O on its own. The cost is event-driven: when Unity emits logs, BepInEx receives and processes them. The performance risk comes from log volume, not from a background frame loop.
- `WriteUnityLog = false` is the safe setting for avoiding duplicate disk writes from Unity log mirroring. Turning it on can add extra file writes for every console/log message and is not useful unless you specifically need standard output copied into Unity's log.
- `Logging.Console.Enabled = false` is the safe default for a game modpack unless you need a visible console window for live diagnostics.
- `Logging.Console.LogLevels` matters only if the console is enabled. With the console disabled, it has no on-screen cost.
- `Logging.Disk.LogLevels` currently includes Info/Message, so normal informational logs already go to disk. That is fine for debugging, but it is still extra churn if the mod or framework is noisy.

Recommendation:

- Keep `UnityLogListening = true` unless you are chasing a logging bug and want to reduce cross-logger chatter further.
- Keep `Logging.Console.Enabled = false` for normal play.
- Keep `WriteUnityLog = false`.
- If startup or runtime logging gets noisy, narrow disk and console `LogLevels` to `Fatal, Error, Warning` for day-to-day play and temporarily widen them only for debugging.

## 2. NeoModLoader config and compile path

Observed install layout:

- NML is installed under `C:\Program Files (x86)\Steam\steamapps\common\worldbox\worldbox_Data\StreamingAssets\Mods\NML`.
- That folder contains `Assemblies`, `CompiledMods`, `Assembly-CSharp-Publicized.dll`, `commit`, `mod_compile_records.json`, and `tab_order_records.json`.
- There is no separate `Mods/NeoModLoader/` directory in this install.
- There is also no NML-specific config file in `BepInEx/config`; only `BepInEx.cfg` and `key.worldbox.keygui.cfg` are present there.

Findings from repo evidence and live logs:

- WSM3D and upstream are both still source-install mods with `Code/` present.
- NML logs show precompiled detection as: `...[uid] detected as precompiled, compilation phase will be skipped on it!`
- This repo's ADR evidence shows the branch condition is filesystem-based: NML checks whether any `.dll` exists in the mod folder and skips compile when one is present.
- The repo's own docs and tests consistently state that NeoModLoader compiles `Code/*.cs` at startup.
- I did not find a separate NML "show log on screen", dev-mode, or hot-reload config surface that is exposed as a dedicated config file in this install.

Interpretation:

- For this install, NML's visible behavior is: source `Code/*.cs` is runtime-compiled at game launch unless the mod folder trips the precompiled condition.
- There is no evidence here of a documented config flag that disables Roslyn compilation while keeping a source-layout mod intact.
- The practical "skip compile" path is to ship a precompiled artifact so NML treats the mod as precompiled. In current NML behavior, that is driven by filesystem layout, not by a `mod.json` flag.

Recommendation:

- If the goal is faster load and avoidance of NML Roslyn edge cases, the only demonstrated lever is precompiled packaging, not a config toggle.
- Leave the current runtime-source path in place until there is a deliberate packaging change, because source layout still supports fastest mod iteration for development.

## 3. WorldBox PlayerPrefs / registry flags

Registry hive checked:

- `HKCU\Software\mkarpenko\WorldBox`

Observed values:

- `Debug_Log_h859589551 = 0`
- `Developer_Mode_h3636625871 = 0`

Not found in the hive:

- `show_console_on_error`
- `show_console_on_start`

Findings:

- I could not locate `show_console_on_error` or `show_console_on_start` in the current registry-backed PlayerPrefs hive.
- The only directly relevant debug-style flags present in this hive were `Debug_Log` and `Developer_Mode`.
- The hive also contains the usual Unity screen and gameplay flags, but nothing named like the requested console-on-error/start options.

Interpretation:

- In this install, those console options are not exposed as visible `HKCU\Software\mkarpenko\WorldBox` values under their literal names.
- If WorldBox still has equivalent console behavior, it is either stored elsewhere, renamed, or not enabled in this install state.

Recommendation:

- Leave `Debug_Log` off for normal play.
- Leave `Developer_Mode` off unless you are intentionally debugging or using a developer surface.
- Treat `show_console_on_error` / `show_console_on_start` as not confirmed for this install until a broader playerprefs search is done against the game process/runtime, not just the registry hive.

## 4. `mod.json` for `WorldSphereMod3D`

Source: `WorldSphereMod/mod.json` and installed copy in `Mods/WorldSphereMod3D/mod.json`

Observed values:

- `name = WorldSphereMod3D`
- `author = Melvin Shwuaner (fork)`
- `version = 2.0.0-beta.6`
- `GUID = worldsphere3d.fork`
- `iconPath = GameResources/WorldSphereMod/Logo.png`

Findings:

- The fork GUID is confirmed as `worldsphere3d.fork`, which is the co-installable identifier used throughout the repo docs.
- The manifest is minimal: it does not include a `precompiled` or `compiled` flag.
- There is no manifest evidence here that could instruct NML to skip runtime compile by itself.
- Compatibility with NML is therefore not driven by a special manifest key in this repo; it is driven by the install layout and NML's own runtime compile logic.

Recommendation:

- Keep the GUID unchanged.
- Do not add an assumed `precompiled` key unless you are also changing the install/load policy and have verified how current NML consumes it.

## 5. Would a precompiled DLL help?

Short answer:

- Yes for startup speed.
- Yes for avoiding this class of NML Roslyn failures.

Evidence and reasoning:

- The repo already documents that NML compiles `Code/*.cs` at runtime.
- The repo's ADR evidence says NML's precompiled branch is triggered by a `.dll` in the mod folder and skips the compile phase.
- That means a precompiled packaging mode should avoid the source-compile path that is currently rejecting some code patterns more strictly than `dotnet build`.
- It should also reduce load time because Roslyn compilation is skipped entirely for that mod.

Important caveat:

- A precompiled DLL only helps if the mod is actually loaded from the compiled artifact path that NML recognizes as precompiled.
- I did not change packaging or config in this audit. This is a recommendation, not a shipped change.

## Ranked recommendations

1. Keep BepInEx logging conservative for normal play: `Console.Enabled = false`, `WriteUnityLog = false`, and narrow disk log levels if logs become noisy.
2. Leave `UnityLogListening = true` unless you have a specific need to reduce logger bridging during a diagnostic session.
3. Keep `Debug_Log` and `Developer_Mode` off in WorldBox PlayerPrefs for normal use.
4. Treat NML source-compile as the current default and do not assume a hidden config switch exists to bypass it.
5. If the main pain point is NML Roslyn rejecting otherwise-valid code, move WSM3D to a deliberate precompiled-DLL packaging path after you confirm the exact loader behavior in a controlled test.
6. Do not add or rely on a `precompiled` manifest key without verifying it against the installed NML build first.

## Bottom line

- The current BepInEx settings are already sane for normal play, except that disk logs still include Info-level output.
- NML in this install is source-compile-first and does not expose an obvious config file for the behavior you asked about.
- `WorldSphereMod3D`'s manifest is minimal and does not contain a precompiled flag.
- A precompiled DLL path is the most plausible way to avoid the Roslyn incompatibilities and cut startup cost, but it should be treated as a packaging change, not a config tweak.
