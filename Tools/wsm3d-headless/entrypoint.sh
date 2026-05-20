#!/usr/bin/env bash
set -euo pipefail

WORLD_DIR="${WORLDBOX_PATH:-/game}"
WORLD_EXE="${WORLDBOX_EXE:-worldbox.exe}"
BRIDGE_PORT="${BRIDGE_PORT:-8766}"
BRIDGE_HOST="${BRIDGE_HOST:-127.0.0.1}"
HEALTH_ENDPOINT="${BRIDGE_HEALTH_ENDPOINT:-/health}"
HEALTH_TIMEOUT_SECONDS="${BRIDGE_HEALTH_TIMEOUT_SECONDS:-300}"
HEALTH_INTERVAL_SECONDS="${BRIDGE_HEALTH_INTERVAL_SECONDS:-2}"

X_DISPLAY="${DISPLAY:-:99}"
X_SCREEN="${X_SCREEN:-0}"
X_SCREEN_GEOM="${X_SCREEN_GEOM:-1280x720x24}"

DXVK_DIR="${DXVK_DIR:-/opt/wsm3d-headless/dxvk}"
GOLDBERG_DLL_SOURCE="${GOLDBERG_DLL_SOURCE:-/usr/local/share/goldberg}"
WORLD_STEAM_APPID="${WORLD_STEAM_APPID:-1055540}"

HEALTHCHECK_URL="http://${BRIDGE_HOST}:${BRIDGE_PORT}${HEALTH_ENDPOINT}"
WORK_DIR="/work"
mkdir -p "$WORK_DIR"
cd "$WORK_DIR"

log() {
  printf '[entrypoint] %s\n' "$*"
}

fatal() {
  log "ERROR: $*"
  exit 1
}

cleanup() {
  if [[ -n "${WORLD_PID:-}" ]] && kill -0 "$WORLD_PID" >/dev/null 2>&1; then
    log "Stopping WorldBox (pid=$WORLD_PID)"
    kill "$WORLD_PID" || true
    wait "$WORLD_PID" || true
  fi
  if [[ -n "${Xvfb_PID:-}" ]] && kill -0 "$Xvfb_PID" >/dev/null 2>&1; then
    log "Stopping Xvfb (pid=$Xvfb_PID)"
    kill "$Xvfb_PID" || true
  fi
}

trap cleanup EXIT

export WINEPREFIX
export WINEARCH
export WINEDEBUG

if [[ ! -f "${WORLD_DIR}/${WORLD_EXE}" ]]; then
  fatal "Missing worldbox executable at ${WORLD_DIR}/${WORLD_EXE}"
fi

log "Starting Xvfb on ${X_DISPLAY}"
mkdir -p /tmp/.X11-unix
Xvfb "${X_DISPLAY}" -screen "${X_SCREEN}" "${X_SCREEN_GEOM}" -nolisten tcp &
Xvfb_PID=$!
trap cleanup EXIT

log "Initializing Wine prefix: ${WINEPREFIX}"
export WINEPREFIX
mkdir -p "${WINEPREFIX}"
wineboot --init >/tmp/wine-init.log 2>&1 || true

if [[ -d "$DXVK_DIR" && -f "$DXVK_DIR/setup_dxvk.sh" ]]; then
  log "Installing DXVK from ${DXVK_DIR}"
  bash "$DXVK_DIR/setup_dxvk.sh" install >/tmp/dxvk-install.log 2>&1 || true
elif [[ -d "$DXVK_DIR" ]]; then
  log "DXVK directory is present but setup_dxvk.sh was not found; skipping install"
else
  log "No DXVK overlay in image; running with host Vulkan stack"
fi

if [[ ! -f "${WORLD_DIR}/steam_appid.txt" ]]; then
  log "Injecting steam_appid.txt with ${WORLD_STEAM_APPID}"
  echo "${WORLD_STEAM_APPID}" >"${WORLD_DIR}/steam_appid.txt"
fi

if [[ -n "${GOLDBERG_DLL_SOURCE}" ]]; then
  GOLDBERG_DLL_64="${GOLDBERG_DLL_SOURCE}/goldberg_steam_api64.dll"
  GOLDBERG_DLL_32="${GOLDBERG_DLL_SOURCE}/goldberg_steam_api.dll"

  if [[ -f "$GOLDBERG_DLL_64" ]]; then
    cp "$GOLDBERG_DLL_64" "${WORLD_DIR}/steam_api64.dll"
    log "Using Goldberg 64-bit DLL from ${GOLDBERG_DLL_64}"
  elif [[ -f "$GOLDBERG_DLL_32" ]]; then
    cp "$GOLDBERG_DLL_32" "${WORLD_DIR}/steam_api.dll"
    log "Using Goldberg 32-bit DLL from ${GOLDBERG_DLL_32}"
  else
    log "No Goldberg DLL found in ${GOLDBERG_DLL_SOURCE}; skipping Steam API stub replacement"
  fi
fi

if [[ ! -f "${WORLD_DIR}/goldberg.ini" ]]; then
  log "Installing default Goldberg config to ${WORLD_DIR}/goldberg.ini"
  cp /opt/wsm3d-headless/goldberg/goldberg.ini "${WORLD_DIR}/goldberg.ini"
fi

if [[ ! -f "${WORLD_DIR}/steam_api.ini" ]]; then
  log "Installing default Goldberg per-app config to ${WORLD_DIR}/steam_api.ini"
  cp /opt/wsm3d-headless/goldberg/steam_api.ini "${WORLD_DIR}/steam_api.ini"
fi

log "Launching WorldBox: ${WORLD_DIR}/${WORLD_EXE}"
export DISPLAY="$X_DISPLAY"
export WINEPREFIX="$WINEPREFIX"
nohup wine "${WORLD_DIR}/${WORLD_EXE}" >/tmp/worldbox.log 2>&1 &
WORLD_PID=$!

if [[ "$WORLD_PID" == "0" ]] || ! kill -0 "$WORLD_PID" >/dev/null 2>&1; then
  fatal "WorldBox process failed to start. Check /tmp/worldbox.log"
fi

log "Waiting for bridge health on ${HEALTHCHECK_URL} (timeout ${HEALTH_TIMEOUT_SECONDS}s)"
deadline=$((SECONDS + HEALTH_TIMEOUT_SECONDS))
while (( SECONDS < deadline )); do
  if curl -fsS --max-time 1 "${HEALTHCHECK_URL}" >/dev/null 2>&1; then
    log "BridgeRPC health check passed"
    break
  fi
  sleep "${HEALTH_INTERVAL_SECONDS}"
done

if (( SECONDS >= deadline )); then
  fatal "BridgeRPC never reported healthy on ${HEALTHCHECK_URL}"
fi

log "Delegating to child process"
wait "$WORLD_PID"
