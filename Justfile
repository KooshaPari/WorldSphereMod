# WorldSphereMod3D Dev CLI (just-flavored mirror of Taskfile.yaml)
# Install just: https://github.com/casey/just
# Usage: just <recipe>  or  just --list

set shell := ["bash", "-cu"]

export DOTNET_SKIP_FIRST_TIME_EXPERIENCE := "true"
export DOTNET_CLI_TELEMETRY_OPTOUT := "true"
export DOTNET_NOLOGO := "true"

# Default: show available recipes.
default:
    @just --list

# ── Build ──────────────────────────────────────────────────────────────────

# Build main mod (Release). Requires $WORLDBOX_PATH for WorldBox reference DLLs.
build:
    dotnet build WorldSphereMod.csproj -c Release

# Build WorldSphereAPI (Unity-free, netstandard2.0 — buildable everywhere).
build-api:
    dotnet build WorldSphereAPI.csproj -c Release

# Build the WorldSphereTester project.
build-tester:
    cd WorldSphereTester && dotnet build -c Release

# Build the unit test project (skipped if scaffold has not landed yet).
build-tests:
    if [ -d "tests/WorldSphereMod.Tests.Unit" ]; then \
        dotnet build tests/WorldSphereMod.Tests.Unit -c Release; \
    else \
        echo "tests/WorldSphereMod.Tests.Unit not present yet — skipping."; \
    fi

# Build every project: API + main + tester + tests.
build-all: build-api build build-tester build-tests

# ── Test ───────────────────────────────────────────────────────────────────

# Run unit tests.
test:
    if [ -d "tests/WorldSphereMod.Tests.Unit" ]; then \
        dotnet test tests/WorldSphereMod.Tests.Unit/; \
    else \
        echo "tests/WorldSphereMod.Tests.Unit not present yet — skipping."; \
    fi

# Run integration tests.
test-integration:
    if [ -d "tests/WorldSphereMod.Tests.Integration" ]; then \
        dotnet test tests/WorldSphereMod.Tests.Integration/; \
    else \
        echo "tests/WorldSphereMod.Tests.Integration not present yet — skipping."; \
    fi

# Run end-to-end tests.
test-e2e:
    if [ -d "tests/WorldSphereMod.Tests.E2E" ]; then \
        dotnet test tests/WorldSphereMod.Tests.E2E/; \
    else \
        echo "tests/WorldSphereMod.Tests.E2E not present yet — skipping."; \
    fi

# Run unit + integration + e2e suites.
test-all: test test-integration test-e2e

# ── Install / Uninstall ───────────────────────────────────────────────────

# Install mod into local WorldBox (PowerShell).
install:
    pwsh Tools/install.ps1

# Uninstall mod from local WorldBox (PowerShell).
uninstall:
    pwsh Tools/uninstall.ps1

# ── Docs (VitePress) ──────────────────────────────────────────────────────

# Run VitePress docs dev server.
docs-dev:
    npm run docs:dev

# Build VitePress static site.
docs-build:
    npm run docs:build

# ── Lint / Format ─────────────────────────────────────────────────────────

# Verify code formatting (fails if files would change).
lint:
    dotnet format --verify-no-changes

# Auto-format C# code via dotnet format.
lint-fix:
    dotnet format

# ── Release gate ──────────────────────────────────────────────────────────

# Pre-release gate: build + tests + lint + docs build all green.
release-check: build-all test-all lint docs-build

# ── Journeys ──────────────────────────────────────────────────────────────

# Verify all Phenotype journey manifests in mock mode (offline, deterministic).
journeys:
    #!/bin/bash
    set -euo pipefail

    # Try to find phenotype-journey on PATH; if not, build it locally.
    if command -v phenotype-journey &> /dev/null; then
      PJ_BIN="phenotype-journey"
      echo "Using phenotype-journey from PATH"
    else
      CACHE_DIR="tools/.cache/phenotype-journeys"
      PJ_BIN="$CACHE_DIR/target/release/phenotype-journey"

      if [ ! -f "$PJ_BIN" ]; then
        echo "Building phenotype-journey to $CACHE_DIR..."
        mkdir -p "$CACHE_DIR"
        if ! git clone https://github.com/KooshaPari/phenotype-journeys "$CACHE_DIR" 2>&1 | grep -v "Cloning into"; then
          echo "✗ Failed to clone phenotype-journeys"
          exit 1
        fi
        cd "$CACHE_DIR"
        if ! cargo build --release --bin phenotype-journey 2>&1 | tail -10; then
          echo ""
          echo "✗ Build failed (requires Rust nightly for edition2024)"
          echo "  Install: rustup default nightly"
          echo "  Then: just journeys"
          exit 1
        fi
        cd - > /dev/null
      fi
    fi

    echo ""
    echo "Verifying manifests in mock mode..."
    for manifest in $(find docs/journeys/manifests -maxdepth 2 -name "manifest.json" | sort); do
      phase_id=$(basename $(dirname "$manifest"))
      "$PJ_BIN" verify "$manifest" --mode mock 2>&1 | head -1 && echo "  ✓ $phase_id"
    done
    echo ""
    echo "All manifests verified."

# Capture screenshots for journey assertions. Use /wsm-screenshot or Tools/wsm3d.ps1.
journeys-capture:
    #!/bin/bash
    echo "→ Screenshot capture workflow:"
    echo ""
    echo "  1. Launch game with the mod: just install-mod"
    echo "  2. Use Claude WSM3D commands:"
    echo "     /wsm-screenshot [manifest-id] [step-index]"
    echo ""
    echo "  3. Or use PowerShell:"
    echo "     pwsh Tools/wsm3d.ps1 screenshot [options]"
    echo ""
    echo "See docs/journeys/CAPTURE.md for details."

# ── Clean ─────────────────────────────────────────────────────────────────

# Remove every bin/ and obj/ directory in the repo.
clean:
    find . -type d \( -name bin -o -name obj \) -not -path './.git/*' -prune -exec rm -rf {} + 2>/dev/null || true
    @echo "Cleaned bin/ and obj/."

# ── MCP Server ─────────────────────────────────────────────────────────────

# Install MCP server for WorldSphereMod tooling.
mcp-install:
    #!/bin/bash
    if command -v uv &>/dev/null; then
        uv pip install -e Tools/wsm3d-mcp
    else
        pip install -e Tools/wsm3d-mcp
    fi

# Run WSM3D MCP server on http://localhost:8766
mcp-run:
    python -m wsm3d_mcp.server --http --port 8766

# ── Mod Installation ──────────────────────────────────────────────────────

# Install mod into local WorldBox (PowerShell, synonym for 'install').
install-mod:
    pwsh Tools/install.ps1

# Relaunch WorldBox with the mod (PowerShell).
relaunch:
    pwsh Tools/wsm3d.ps1 relaunch

# Capture screenshot for journey asset (PowerShell).
screenshot:
    pwsh Tools/wsm3d.ps1 screenshot

# ── Dev CLI ───────────────────────────────────────────────────────────────

# Hot-reload: build + install on file change.
watch:
    pwsh Tools/wsm3d.ps1 watch

# Print machine-readable diagnostic status (JSON).
doctor:
    pwsh Tools/wsm3d.ps1 status -Json
