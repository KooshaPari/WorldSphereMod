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

# ── Clean ─────────────────────────────────────────────────────────────────

# Remove every bin/ and obj/ directory in the repo.
clean:
    find . -type d \( -name bin -o -name obj \) -not -path './.git/*' -prune -exec rm -rf {} + 2>/dev/null || true
    @echo "Cleaned bin/ and obj/."
