#!/usr/bin/env bash
# Create zero-byte WorldBox reference DLL placeholders for CI (build-gate, test-gate, lint-gate).
# See docs/ci-mod-compile-gap.md — placeholders satisfy MSBuild HintPath resolution only;
# they do not contain types and cannot produce a loadable mod binary.
set -euo pipefail

STUB_ROOT="${WORLDBOX_STUB_ROOT:-${RUNNER_TEMP:-/tmp}/worldbox-stub}"
MANIFEST="${BASH_SOURCE%/*}/ci-worldbox-ref-dlls.manifest"

if [[ ! -f "$MANIFEST" ]]; then
  echo "ci-stub-worldbox-refs: manifest not found at $MANIFEST" >&2
  exit 1
fi

while IFS= read -r line || [[ -n "$line" ]]; do
  line="${line%%#*}"
  line="$(echo "$line" | xargs)"
  [[ -z "$line" ]] && continue
  target="$STUB_ROOT/$line"
  mkdir -p "$(dirname "$target")"
  : > "$target"
done < "$MANIFEST"

export WORLDBOX_PATH="$STUB_ROOT"
if [[ -n "${GITHUB_ENV:-}" ]]; then
  echo "WORLDBOX_PATH=$STUB_ROOT" >> "$GITHUB_ENV"
fi

echo "ci-stub-worldbox-refs: wrote $(grep -cve '^\s*\(#\|$\)' "$MANIFEST") placeholder DLLs under $STUB_ROOT"
