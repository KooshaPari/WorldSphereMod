# WorldSphereMod headless container runtime

This folder provides the baseline image/runtime for headless WorldBox launch in CI:

- `Dockerfile` builds an Ubuntu image with Wine + Xvfb + Vulkan + DXVK support.
- `entrypoint.sh` starts Xvfb, initializes Wine, launches `worldbox.exe`, and blocks until
  `BRIDGE_PORT` healthcheck responds.
- `goldberg/` contains the Goldberg stub config references and default config files. The
  actual `goldberg_steam_api*.dll` is intentionally not committed; mount it from private CI cache.

## Build/run contract

1. Mount a licensed WorldBox install to `/game`.
2. Optionally mount Goldberg DLLs into `/usr/local/share/goldberg`:
   - `/usr/local/share/goldberg/goldberg_steam_api64.dll` (preferred)
   - `/usr/local/share/goldberg/goldberg_steam_api.dll` (fallback 32-bit)
3. Optionally mount DXVK under `/opt/wsm3d-headless/dxvk` when you want a specific version.
4. Run the container with `WORLDBOX_PATH=/game` and `BRIDGE_PORT=8766`.

## Environment variables

- `WORLDBOX_PATH` (default `/game`)
- `WORLDBOX_EXE` (default `worldbox.exe`)
- `BRIDGE_PORT` (default `8766`)
- `BRIDGE_HEALTH_ENDPOINT` (default `/health`)
- `BRIDGE_HOST` (default `127.0.0.1`)
- `GOLDBERG_DLL_SOURCE` (default `/usr/local/share/goldberg`)
- `DXVK_DIR` (default `/opt/wsm3d-headless/dxvk`)
- `WORLD_STEAM_APPID` (default `1055540`)
