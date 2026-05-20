# Containerized WSM3D Test Design

Goal: run `N` parallel WorldBox + WSM3D instances in containers, each reachable by a bridge client, without baking the game itself into a redistributable image.

## Recommended Split

- `wsm3d-game`: one container per WorldBox instance.
- `wsm3d-orchestrator`: one client container that schedules scenarios and talks to each game bridge.

The game container owns Wine, DXVK, Xvfb, the Steam stub, the mod install, and the bridge RPC listener. The orchestrator stays separate so CI can scale jobs without coupling scheduling logic to the game image.

## Image Strategy

Do **not** publish WorldBox binaries in the image. The image should contain only runtime deps and entrypoint logic; mount the licensed game install at runtime from a private cache/volume or a self-hosted runner workspace.

```dockerfile
FROM ubuntu:24.04

RUN apt-get update && apt-get install -y \
    wine64 winetricks xvfb vulkan-tools mesa-vulkan-drivers \
    libvulkan1 cabextract curl ca-certificates \
 && rm -rf /var/lib/apt/lists/*

# DXVK and Steam stub are runtime overlays, not the game itself.
COPY docker/dxvk/ /opt/dxvk/
COPY docker/goldberg/ /opt/goldberg/
COPY tools/wsm3d-bridge/ /opt/wsm3d-bridge/

ENV WINEPREFIX=/wine \
    DISPLAY=:99 \
    WINEDEBUG=-all

ENTRYPOINT ["/opt/wsm3d-bridge/entrypoint.sh"]
```

Entrypoint flow:

1. Start Xvfb on `:99`.
2. Install/refresh DXVK into the Wine prefix.
3. Drop `steam_api64.dll`, `steam_appid.txt`, and Goldberg config next to `worldbox.exe`.
4. Launch `worldbox.exe` under Wine.
5. Start or expose the bridge RPC on `127.0.0.1:8765`.

## GPU Path

Preferred order:

1. Host GPU passthrough with Vulkan + DXVK.
2. Software Vulkan via llvmpipe if the runner has no GPU.
3. D3D9 WARP-style fallback only for launch smoke, not for full 3D coverage.

WSM3D’s hardware gate still matters: if instancing, compute, or indirect args are missing, the container should report “smoke only” rather than pretending it can run the full phase suite.

## Scaling And Footprint

- Use shared image layers plus per-container writable volumes for saves, logs, and bridge state.
- Keep one private game cache volume per runner, then clone/copy into per-container workdirs.
- Expect each instance to pay most of the WorldBox RSS cost independently; the win is disk reuse and coordinated startup, not a shared process.
- Practical target: 8 parallel instances on average CI hardware, 16 on a beefy self-hosted runner if memory and GPU pressure stay below the failure threshold.

Cold-start cost is dominated by Wine prefix setup and first launch shader compilation. Cache the prefix, prewarm once, and reuse the same base workdir template for each replica.

## Compose Shape

```yaml
version: "3.9"

x-game: &game
  image: wsm3d-game:private
  shm_size: "1g"
  volumes:
    - worldbox-cache:/game:ro
    - wsm-work:/work

services:
  game-1:
    <<: *game
    environment:
      INSTANCE_ID: "1"
      BRIDGE_PORT: "8765"
  game-2:
    <<: *game
    environment:
      INSTANCE_ID: "2"
      BRIDGE_PORT: "8765"
  game-3:
    <<: *game
    environment:
      INSTANCE_ID: "3"
      BRIDGE_PORT: "8765"

  orchestrator:
    image: wsm3d-orchestrator:latest
    depends_on:
      - game-1
      - game-2
      - game-3
    environment:
      TARGETS: "game-1:8765,game-2:8765,game-3:8765"
    command: ["run", "--targets", "game-1:8765,game-2:8765,game-3:8765"]

volumes:
  worldbox-cache:
  wsm-work:
```

For `N = 8..16`, generate the `game-<n>` blocks from a template or small script, then point the orchestrator at the emitted target list. In GitHub Actions, prefer a matrix of scenario bundles, each job starting its own compose project. That gives clearer logs and avoids one stalled container blocking all scenarios.

## Notes

- GoldbergEmu is a stub for launch compatibility, not a license workaround; keep it private to CI/self-hosted use.
- If the bridge already exists in-process, the orchestrator only needs a stable localhost JSON API and a health check.
- Keep screenshots and artifacts on the orchestrator side so the game container stays disposable.
