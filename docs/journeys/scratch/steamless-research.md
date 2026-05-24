# Steamless Research For CI

Goal: run `worldbox.exe` in CI without an interactive Steam login so we can spawn `N` parallel instances for WSM3D testing.

## Short answer

The lowest-risk path is to launch WorldBox directly with a local `steam_appid.txt` and whatever SteamAPI stubs the game already expects, then verify whether the game reaches Unity and loads NeoModLoader normally. Steamworks documents that `steam_appid.txt` disables the Steam-client relaunch check for local testing, and that direct launch otherwise needs Steam context or `SteamAPI_Init` can fail.

## Findings

1. GoldbergEmu / Goldberg Steam Emulator
- What it is: a Steam API emulator that replaces `steam_api.dll` / `steam_api64.dll` with a local implementation so games can satisfy Steamworks calls without the real client.
- What it is useful for here: if WorldBox makes Steamworks calls during startup, Goldberg is the compatibility layer most likely to let the process boot in CI without Steam.
- Risk: this is a third-party emulator for Steam API behavior. It is not a clean upstream path and should be treated as a compatibility workaround, not a default shipping dependency.
- Source: [Goldberg Steam Emulator README](https://github.com/killvxk/goldberg_emulator)

2. Steamless / cracked-binary patch
- What it is: a DRM unpacker for SteamStub-packed executables.
- Legal posture: this is the grayest option. It is explicitly a DRM remover, and even its own README says it is not for piracy and should only be used on legally owned games.
- For this project: flag as a last-resort compatibility investigation only. Do not make it the default CI path.
- Source: [Steamless README](https://github.com/atom0s/Steamless)

3. Direct `worldbox.exe` launch with `steam_appid.txt`
- Steamworks docs say `steam_appid.txt` suppresses the "must be launched through Steam" restart path for local development.
- This is the cleanest candidate for headless CI if WorldBox only needs an AppID context, not an actual authenticated Steam session.
- Practical implication: if WorldBox starts and NML loads with this setup, we do not need a Steam client or a crack-layer emulator.
- Source: [Steamworks API docs](https://partner.steamgames.com/doc/api/steam_api?l=english&language=english)

4. Does NML/Harmony work in steamless mode?
- NML's own install docs only require dropping `NeoModLoader.dll` into `worldbox_Data/StreamingAssets/mods`, deleting `NCMS_memload.dll`, and starting the game with experimental mode enabled.
- The docs do not mention Steam as a prerequisite for the loader itself. That strongly suggests the Harmony/NML path is inside the game process and should work as long as WorldBox boots.
- Caveat: NML can still depend on whatever WorldBox does at startup. If the game has a Steamworks gate before Unity reaches mod loading, then a stub/emulator may still be needed.
- Source: [NeoModLoader README](https://github.com/WorldBoxOpenMods/ModLoader)

## Recommended CI posture

- Preferred: direct launch with `steam_appid.txt`, then validate whether WorldBox + NML boots non-interactively.
- Fallback: Goldberg-style Steam API emulation if the game insists on Steamworks APIs during startup.
- Avoid as default: Steamless / binary patching, because it is legally messier and harder to defend as a CI baseline.

## Legal note

We own the game license, and this research is for our own mod CI, not redistribution. That said, the line between "compatibility testing" and "DRM circumvention" matters. The safest engineering posture is to prefer official Steamworks-supported local launch, keep any emulator use contained to private CI, and avoid shipping patched binaries or distributing any unpacked executable.

## What still needs live validation

- Confirm whether this specific WorldBox build starts cleanly with `steam_appid.txt` alone.
- Confirm whether NML loads before any Steam-dependent early-exit path.
- If direct launch fails, test whether a local SteamAPI stub is sufficient before considering any heavier emulator path.
