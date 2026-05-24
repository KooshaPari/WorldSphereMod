# Windows Sandbox + WSL2 for WSM3D Ephemeral Testing

## Scope and objective
Evaluate whether (a) Windows Sandbox and (b) WSL2 + WSLg + Wine + DXVK can serve as viable ephemeral hosts for WSM3D testing, including Steam-auth and Unity mod-loading concerns.

## 1) Windows Sandbox feasibility

**Current verdict: Not viable as-is for WSM3D.**

Windows Sandbox does expose GPU virtualization via WDDM and can use hardware acceleration when the host has compatible WDDM 2.5+ drivers; otherwise it falls back to Microsoft WARP (`Windows Advanced Rasterization Platform`).

- [Microsoft: Windows Sandbox architecture](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/windows-sandbox-architecture)
  - `WDDM GPU virtualization` with programs inside sandbox competing for GPU with host apps (good for graphics where available).
  - Incompatible systems use CPU-based WARP fallback.

Even with GPU access, Sandbox is the wrong fit for Steam-driven games for two reasons:

1. Steam session setup is not a natural fit inside Sandbox lifecycle.
2. Even if the game starts, maintaining a clean, repeatable **Steam-authenticated login** and mod-install state across ephemeral sandbox resets is operationally expensive and flaky.

Steam docs still expect Steam context to be established by launch flow, with `SteamAPI_RestartAppIfNecessary` bridging non-Steam launches back through Steam when needed; local-only `steam_appid.txt` is still a dev-only fallback.

- [Steamworks API Overview](https://partner.steamgames.com/doc/sdk/api?language=english)
  - `SteamAPI_Init` requires AppID context.
  - `SteamAPI_RestartAppIfNecessary` checks and re-launches through Steam.
  - `steam_appid.txt` is explicitly for local development only and should not be shipped.

**Conclusion:** we can expect repeated setup friction for credentials, launch path, and anti-cheat policy compatibility. For this repo’s goal (low-friction ephemeral parallel runs), Sandbox is lower-value than WSL2/containerized Linux Wine approach.

## 2) WSL2 + Wine + DXVK + Unity sample test attempt

**Execution status in this environment:** no live run (blocked before runtime test).

Observed environment:

- WSL is installed (`wsl --version` present, WSLg available)
- No Linux distro is currently installed.
- Attempted `wsl --install -d Ubuntu --no-launch`
- Install failed: `WINS_INSTALLDistro` network fetch from GitHub `DistributionInfo.json` could not connect.

Without a distro I could not complete the requested concrete run of:
- Wine install
- DXVK install
- Unity sample execution

**Theoretical feasibility notes (with risk):**

- WSL GUI support is official and requires vGPU/driver stack for acceleration.
- WSLg/mesa paths are explicit about OpenGL acceleration via Windows-backed virtual GPU and Mesa d3d12 backend behavior.
- DXVK is a Vulkan-based D3D8-11 translation layer for Wine.

Sources:
- [Run Linux GUI apps on WSL](https://learn.microsoft.com/en-us/windows/wsl/tutorials/gui-apps)
- [WSLg GPU selection / MESA d3d12 notes](https://github.com/microsoft/wslg/wiki/GPU-selection-in-WSLg)
- [DXVK project README](https://github.com/doitsujin/dxvk)

## 3) WSM3D-specific deal-breakers

1. **Steam login/auth path**
- If game path still runs SteamAPI init and restart checks, pure non-Steam launch will fail unless the dev-only AppID fallback behaves for that game.

2. **Anti-cheat / security layers**
- Any anti-cheat or kernel-tied service in the chain can reject Wine/virtualized environments even when rendering works.

3. **Unity graphics capability gate**
- WSM3D has a hard gate on supported graphics features (instancing/compute/indirect args style checks). CPU-only fallback paths are partial and do not provide full render coverage.
- This aligns with earlier feasibility work: WSL/Wine is useful only if it exposes a real graphics device with needed caps.

4. **Bridge + mod-loading mid-Wine boot**
- Steam/ModLoader startup order matters.
- Even if DXVK brings graphics up, NML/WSM loading depends on the game reaching the expected bootstrap point and not failing early in launch wrappers.

## 4) Integration cost estimate

**Windows Sandbox**
- Setup: 1 day for PoC if only launch testing; 3–5 days if attempting persistent Steam+mod state.
- Reliability: low for multi-run CI.
- Throughput: low due reset-driven state churn.

**WSL2 + Wine + DXVK**
- Setup: 1 sprint for base image scripts (install chain, vGPU checks, wineprefix bootstrapping, DXVK install).
- Operational reliability: medium, GPU/driver dependent; depends on host-side driver updates.
- Throughput: medium-high for containerized orchestration once distro + cache bootstrapping is stable.

## Recommendation

Use WSL2 + Wine + DXVK only as **Phase-2** path after a successful, small-scale proof run on an actual host-like environment. Keep Windows Sandbox out of the core ephemeral test strategy for WSM3D unless team later prioritizes manual Steam login session orchestration and a dedicated Steam-inbox workflow per container.
