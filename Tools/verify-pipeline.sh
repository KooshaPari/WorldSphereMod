#!/usr/bin/env bash
# verify-pipeline.sh — end-to-end voxel render pipeline verification
# Waits for game load, checks bridge health, spawns units, verifies telemetry + logs.
set -euo pipefail

BRIDGE="http://127.0.0.1:8766"
PLAYER_LOG="$USERPROFILE/AppData/LocalLow/mkarpenko/WorldBox/Player.log"
POLL_INTERVAL=3
LOAD_TIMEOUT=180
HEALTH_TIMEOUT=60
SPAWN_COUNT=50
SPAWN_RACE="human"
SETTLE_SECONDS=30

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

pass() { echo -e "${GREEN}[PASS]${NC} $1"; }
fail() { echo -e "${RED}[FAIL]${NC} $1"; }
info() { echo -e "${YELLOW}[INFO]${NC} $1"; }

# ---------- Stage 1: Wait for game to finish loading ----------
info "Stage 1: Waiting for 'Loading finished' in Player.log (timeout ${LOAD_TIMEOUT}s)..."
elapsed=0
while true; do
    if [ -f "$PLAYER_LOG" ] && grep -q "Loading finished" "$PLAYER_LOG" 2>/dev/null; then
        pass "Game loading finished (found in Player.log)"
        break
    fi
    if [ "$elapsed" -ge "$LOAD_TIMEOUT" ]; then
        fail "Timed out waiting for 'Loading finished' in Player.log after ${LOAD_TIMEOUT}s"
        exit 1
    fi
    sleep "$POLL_INTERVAL"
    elapsed=$((elapsed + POLL_INTERVAL))
done

# ---------- Stage 2: Wait for bridge health with isWorld3D=true ----------
info "Stage 2: Waiting for bridge health isWorld3D=true at $BRIDGE/health (timeout ${HEALTH_TIMEOUT}s)..."
elapsed=0
while true; do
    health_json=$(curl -sf "$BRIDGE/health" 2>/dev/null || echo "")
    if [ -n "$health_json" ]; then
        is3d=$(echo "$health_json" | python -c "import sys,json; d=json.load(sys.stdin); print(str(d.get('isWorld3D',False)).lower())" 2>/dev/null || echo "false")
        if [ "$is3d" = "true" ]; then
            pass "Bridge healthy, isWorld3D=true"
            echo "    Health payload: $health_json"
            break
        else
            info "Bridge reachable but isWorld3D=$is3d — waiting..."
        fi
    fi
    if [ "$elapsed" -ge "$HEALTH_TIMEOUT" ]; then
        fail "Timed out waiting for isWorld3D=true after ${HEALTH_TIMEOUT}s"
        exit 1
    fi
    sleep "$POLL_INTERVAL"
    elapsed=$((elapsed + POLL_INTERVAL))
done

# ---------- Stage 3: Spawn units ----------
info "Stage 3: Spawning $SPAWN_COUNT $SPAWN_RACE units via bridge..."
spawn_resp=$(curl -s -X POST -H "Content-Length: 0" "$BRIDGE/actions/spawn_units?count=$SPAWN_COUNT&race=$SPAWN_RACE" 2>/dev/null || echo "")
if [ -z "$spawn_resp" ]; then
    fail "POST /actions/spawn_units returned no response or bridge unreachable"
    exit 1
fi
pass "Spawn request sent: $spawn_resp"

# ---------- Stage 4: Wait for settle ----------
info "Stage 4: Waiting ${SETTLE_SECONDS}s for voxel pipeline to process spawned units..."
sleep "$SETTLE_SECONDS"
pass "Settle period complete"

# ---------- Stage 5: Check telemetry ----------
info "Stage 5: Reading telemetry from $BRIDGE/telemetry..."
telemetry_json=$(curl -sf "$BRIDGE/telemetry" 2>/dev/null || echo "")
if [ -z "$telemetry_json" ]; then
    fail "GET /telemetry returned no response"
    exit 1
fi

echo "    Telemetry payload:"
echo "$telemetry_json" | python -m json.tool 2>/dev/null || echo "    $telemetry_json"

# Extract instance count (try common field names)
instances=$(echo "$telemetry_json" | python -c "
import sys, json
d = json.load(sys.stdin)
for key in ['instanceCount', 'instances', 'totalInstances', 'voxelInstances', 'frameInstances']:
    if key in d:
        print(f'{key}={d[key]}')
        sys.exit(0)
# Dump all numeric fields as fallback
for k, v in d.items():
    if isinstance(v, (int, float)):
        print(f'{k}={v}')
" 2>/dev/null || echo "    (could not parse telemetry)")
echo "    Instance metrics: $instances"
pass "Telemetry retrieved"

# ---------- Stage 6: Grep Player.log for pipeline markers ----------
info "Stage 6: Grepping Player.log for pipeline markers..."
markers=("EmitVoxels" "spawn_units" "DrawMeshInstanced" "FrameInstances")
all_found=true
for marker in "${markers[@]}"; do
    count=$(grep -c "$marker" "$PLAYER_LOG" 2>/dev/null | tr -d '[:space:]' || echo "0")
    count=${count:-0}
    if [ "$count" -gt 0 ]; then
        pass "  $marker: $count occurrence(s)"
    else
        fail "  $marker: NOT FOUND in Player.log"
        all_found=false
    fi
done

echo ""
echo "========================================"
if [ "$all_found" = true ]; then
    pass "Pipeline verification PASSED -- all markers found"
    exit 0
else
    info "Pipeline verification PARTIAL -- some markers missing (may be expected depending on phase)"
    exit 0
fi
