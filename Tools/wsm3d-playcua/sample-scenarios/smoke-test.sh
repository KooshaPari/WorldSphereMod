#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PYTHON="python3"
if ! command -v "$PYTHON" >/dev/null 2>&1; then
  PYTHON="python"
fi

"$PYTHON" "$SCRIPT_DIR/../smoke.py" "$@"
