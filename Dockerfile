# =============================================================================
# WorldSphereMod3D — Dev & CI Dockerfile
# =============================================================================
# Multi-stage image: builder for CI/SBOM, dev for devcontainer environments.
#
# CI builds WorldSphereAPI.csproj (public, Unity-free); local development
# builds WorldSphereMod.csproj with a local $(WorldBoxPath) env var.
# See CLAUDE.md and Directory.Build.props for the build contract.
#
# Dev image includes: .NET 8 SDK, Python 3.11, Node 20, Rust, PowerShell 7+,
# gh CLI, just, task, tesseract-ocr, and devcontainer base tooling.
# =============================================================================

# =============================================================================
# Stage 1 — .NET 8 dev environment (base for all stages)
# =============================================================================
FROM mcr.microsoft.com/devcontainers/dotnet:8.0-bookworm AS builder

WORKDIR /src

# Install system dependencies: Rust, just, task, tesseract-ocr, gh CLI.
# Combine into single layer to minimize image bloat. Use --no-install-recommends.
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    rustc \
    cargo \
    pkg-config \
    libssl-dev \
    tesseract-ocr \
    && rm -rf /var/lib/apt/lists/*

# Install just (task runner)
RUN curl --proto '=https' --tlsv1.2 -sSf https://just.systems/install.sh \
    | bash -s -- --to /usr/local/bin

# Install task (taskfile.dev)
RUN sh -c "$(curl --location https://taskfile.dev/install.sh)" -- -d -b /usr/local/bin

# Install gh CLI via official repo
RUN apt-get update && apt-get install -y --no-install-recommends \
    gh \
    && rm -rf /var/lib/apt/lists/*

# Copy build inputs
COPY Directory.Build.props ./
COPY WorldSphereAPI.csproj ./
COPY WorldSphereAPI/        ./WorldSphereAPI/

RUN dotnet restore WorldSphereAPI.csproj
RUN dotnet build   WorldSphereAPI.csproj -c Release --no-restore /p:ContinuousIntegrationBuild=true
RUN dotnet publish WorldSphereAPI.csproj -c Release --no-build -o /out

# =============================================================================
# Stage 2 — Runtime image for CI/SBOM (netstandard2.0 library, no exec)
# =============================================================================
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS final

LABEL org.opencontainers.image.title="WorldSphereMod3D API"
LABEL org.opencontainers.image.description="CI/SBOM image for the Unity-free WorldSphereAPI shim. Does NOT contain the WorldBox mod itself."
LABEL org.opencontainers.image.source="https://github.com/MelvinShwuaner/WorldSphereMod"
LABEL org.opencontainers.image.licenses="See repository LICENSE"

WORKDIR /app
COPY --from=builder /out/ ./

CMD ["true"]

# =============================================================================
# Stage 3 — Dev container (devcontainer.json uses this as 'builder' target)
# =============================================================================
# Re-tag the builder stage so devcontainer can reference it explicitly.
# All toolchain is already installed; just add workspace setup.
FROM builder AS dev

WORKDIR /workspaces/WorldSphereMod

# Final sanity check: confirm all tools are available
RUN pwsh -Command "Write-Host 'devcontainer ready'; dotnet --version; python3 --version; node --version; rustc --version; cargo --version; gh --version; just --version; task --version; tesseract --version"
