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

# Standard local verification set.
check: build-all test-all lint

# Pre-release gate: build + tests + lint + docs build all green.
release-prep: build-all test-all lint docs-build

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
    npm --prefix docs run docs:dev

# Build VitePress static site.
docs-build:
    npm --prefix docs run docs:build

# ── Lint / Format ─────────────────────────────────────────────────────────

# Verify code formatting (fails if files would change).
lint:
    dotnet format --verify-no-changes

# Auto-format C# code via dotnet format.
lint-fix:
    dotnet format

# ── Release gate ──────────────────────────────────────────────────────────

# Alias for release-prep.
release-check: release-prep

# ── Journeys ──────────────────────────────────────────────────────────────

# Verify all Phenotype journey manifests in mock mode (offline, deterministic).
journeys:
    powershell.exe -NoLogo -NoProfile -File Tools/verify-journeys.ps1

# Semi-deterministic pipeline: dotnet tests, journey mock verify, optional -Live playcua+SSIM.
live-verify *ARGS='':
    powershell.exe -NoLogo -NoProfile -File Tools/wsm-live-verify.ps1 {{ARGS}}
# Build journey-records crate.
journey-records-build:
    cargo build --manifest-path tools/journey-records/Cargo.toml --release

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

# Environment diagnostics (human summary + JSON with -Json).
doctor:
    pwsh Tools/wsm3d.ps1 doctor -Json

# ── Git Hooks ─────────────────────────────────────────────────────────────

# Install git pre-commit hooks (run once after clone).
hooks-install:
    pwsh Tools/wsm3d.ps1 hooks install
