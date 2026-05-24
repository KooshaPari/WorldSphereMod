# WSM3D Headless Orchestrator Engine Selection on Windows (Docker Alternative)

## Scope

Decision target: run 8-16 parallel WorldBox + WSM3D instances from Windows hosts with:

- isolated execution,
- per-instance BridgeRPC endpoint on different IP+port,
- Steam compatibility,
- integration with `Tools/wsm3d.ps1` and GitHub Actions orchestration.

The options below are compared by:

- VM/container spawn overhead,
- Steam compatibility,
- BridgeRPC IPC viability,
- CI integration friction (especially on self-hosted Windows runners),
- practical scaling behavior for `N = 8..16`.

## Executive conclusion

1) **Best overall: Hyper-V (recommended)**

- Lowest total cost for scale after base image preparation.
- True Windows environment, no Wine layer for Steam/DRM path.
- Supports networked per-VM BridgeRPC (`ip:port`) cleanly with NAT/Bridged networking.
- Can checkpoint once, then clone disks for fast fan-out runs.

2) **Second-best: WSL2 + WSLg + Wine/DXVK**

- Lowest startup for Linux-workload style runners when Windows-native VMs are not acceptable.
- Works, but slower GPU/driver path than native Windows; Steam/DRM reliability is lower and may require emulation/proxy approaches.
- Better for cost-sensitive/dev box situations, weaker as primary scale path.

---

## Engine-by-engine comparison (at a glance)

Legend:
- **Spawn**: startup from ready template to runnable WorldBox process.
- **Steam**: DRM/game launch reliability under automation.
- **BridgeRPC**: stable per-instance TCP endpoint for orchestrator.
- **CI fit**: fit with `wsm3d.ps1`, GH Actions, and deterministic artifacts.

1) **Hyper-V**
- Spawn: **best after warm-up** (template + linked clone + checkpoint) / slower cold, but best amortized.
- Steam: **good** (native Windows + real client/install flow).
- BridgeRPC: **best** (dedicated VM IP + static port assignment, firewall rules).
- CI fit: **best** on self-hosted Windows runners with nesting enabled.
- Notes: aligns with your described snapshot-clone pattern; strongest match.

2) **WSL2 + WSLg (Wine + DXVK)**
- Spawn: good for Linux-style instances, especially with pre-created distros.
- Steam: **acceptable but not equal** (needs emulation/proxy paths; slower frames; occasional launch variance).
- BridgeRPC: good (NAT forwarded port or host bridge via localhost ports).
- CI fit: medium; needs wrapper scripts and additional launch guards.
- Notes: feasible fallback when Windows native stack is blocked by tooling policy.

3) **Windows Sandbox**
- Spawn: medium for single use, drops quickly but scaling to 16 is usually poor.
- Steam: good only if launch chain is inside sandbox; high per-run provisioning overhead.
- BridgeRPC: possible but management complexity grows with many concurrent sandboxes.
- CI fit: medium-low; sandbox lacks rich VM lifecycle APIs and fast cloning semantics.
- Notes: good manual smoke tool, weak orchestration fit.

4) **Multipass**
- Spawn: good on Linux-hosted workloads.
- Steam: **weak for WSM3D** (Windows binaries not first-class).
- BridgeRPC: good for Linux daemons, not ideal for native Steam desktop game path.
- CI fit: limited unless translating to Linux-only harness.
- Notes: not aligned with Win-native headless WorldBox strategy.

5) **Podman Desktop**
- Spawn: good for containers, not Windows desktop VMs.
- Steam: no native support (Linux container path only).
- BridgeRPC: possible only if running Windows app via Wine inside Linux container; adds translation costs.
- CI fit: good for dev tooling but not the target workload shape.
- Notes: closest modern Docker replacement, but does not solve native Windows launch.

6) **Rancher Desktop**
- Similar to Podman Desktop in this context.
- Same limitations for native Windows game orchestration.
- Notes: mostly a docker-desktop UX/productivity layer.

7) **HASE / Microsoft Dev Box**
- Spawn: poor for burst orchestration; intended for persistent developer VMs.
- Steam: good (Windows, full desktop), but not for high-density short-lived fan-out.
- BridgeRPC: possible.
- CI fit: strong for long-running dev env, weak for elastic CI matrix.

## Why Hyper-V beats the others for this job

- **Density vs. control**: Hyper-V gives repeatable VM templates and per-instance ports with predictable isolation.
- **Steam compliance path**: true Windows + real Steam avoids most Wine/compat hacks.
- **Scaling model**: template checkpointing + differencing/clones minimizes repeated install/config cost.
- **Operational fit**: deterministic PowerShell lifecycle (`New-VM`, `Start-VM`, `Stop-VM`, `Checkpoint-VM`) maps cleanly to matrix orchestration and retry logic.

## Concrete setup commands

### Option A (recommended): Hyper-V

1. Prepare host (once):

```powershell
dism.exe /Online /Enable-Feature /FeatureName:Microsoft-Hyper-V-All /All /NoRestart
Restart-Computer
```

2. Create a production VM template, install Windows + Steam + WorldBox once, then checkpoint:

```powershell
New-VM -Name wsm3d-template -MemoryStartupBytes 8GB -Generation 2 -SwitchName "WSM-NAT" -VHDPath "C:\WSM\base\wsm3d.vhdx"
Set-VMProcessor -VMName wsm3d-template -ExposeVirtualizationExtensions $true
Set-VM -Name wsm3d-template -AutomaticStartAction StartIfRunning
Start-VM wsm3d-template
# ... install Steam + WorldBox + mod + wsm3d.ps1 dependencies once ...
Checkpoint-VM -Name wsm3d-template -SnapshotName "clean_base"
```

3. Spawn per-instance clones for N=8..16 using differencing disks:

```powershell
1..8 | ForEach-Object {
  $i = $_
  $port = 9000 + $i
  $vm = "wsm3d-$i"
  Copy-Item "C:\WSM\base\wsm3d.vhdx" "C:\WSM\live\wsm3d-$i.vhdx"
  New-VM -Name $vm -MemoryStartupBytes 6GB -Generation 2 -SwitchName "WSM-NAT" -VHDPath "C:\WSM\live\wsm3d-$i.vhdx"
  Set-VM -Name $vm -ProcessorCount 4 -DynamicMemoryEnabled $true
  Start-VM $vm
}
```

4. Start BridgeRPC in each instance with fixed port and call from GitHub Actions:

```powershell
# inside each VM run (example)
pwsh C:\WorldSphereMod\Tools\wsm3d.ps1 run --bridge-port 9010 --headless
```

5. GH Actions fan-out idea (`strategy.matrix` with parallel jobs):

```yaml
strategy:
  fail-fast: false
  matrix:
    slot: [1,2,3,4,5,6,7,8,9,10,11,12]
steps:
  - name: Map VM endpoint
    shell: pwsh
    run: |
      $slot = '${{ matrix.slot }}'
      $ip = "10.0.10.$slot"
      $port = 9000 + [int]$slot
      pwsh Tools/wsm3d.ps1 run --bridge-host $ip --bridge-port $port --scenario "${{ matrix.slot }}"
```

### Option B (fallback): WSL2 + WSLg (Wine + DXVK)

1. Initialize host runtime:

```powershell
wsl --install
wsl --set-default-version 2
wsl --shutdown
```

2. Create per-instance launch profile (inside WSL):

```bash
cat > ~/wsm3d-wsm.sh <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
DISPLAY=:0
export WINEPREFIX=/home/runner/.wine/$WORLD_INST
export WINEDEBUG=-all
export MESA_D3D12_DEFAULT_ADAPTER=1
xvfb-run -a -s "-screen 0 1280x720x24" \
  wine64 /mnt/c/WorldBox/worldbox.exe -batchmode -nographics || true
```

3. Assign fixed host ports and start bridge wrapper service:

```bash
export WORLD_INST="$INSTANCE_ID"
export BRIDGE_PORT=$((9000 + INSTANCE_ID))
pwsh.exe /c "pwsh C:\WorldSphereMod\Tools\wsm3d.ps1 run --bridge-port $BRIDGE_PORT --headless"
```

4. GH Actions matrix then schedules at most hardware-appropriate fanout based on GPU availability:

```yaml
strategy:
  max-parallel: 4
```

In practice this is often the right fallback only when Hyper-V is blocked (policy or environment limits), not the primary scale path.

## Recommendation

Adopt a **Hyper-V-first architecture** for reliable Steam-native behavior and predictable 8-16 parallel orchestration. Keep a **WSL2+Wine escape hatch** only for constrained CI nodes where Hyper-V is unavailable or nested virtualization policy prevents VM cloning.

If you want, I can next draft the `wsm3d.ps1` orchestration wrapper pseudocode that:
- provisions/recycles a VM per matrix slot,
- writes `bridge_host`/`bridge_port` into run metadata,
- uploads logs/screenshots/artifacts with unique per-slot folders, and
- fails fast cleanly without leaving detached VM children.
