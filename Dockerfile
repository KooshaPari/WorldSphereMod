# =============================================================================
# WorldSphereMod3D — CI / SBOM Dockerfile
# =============================================================================
# This image exists ONLY to give CI and SBOM tooling a reproducible build
# surface for the parts of the repo that DON'T need WorldBox's reference DLLs.
#
# What this builds:
#   WorldSphereAPI.csproj — the public, Unity-free, netstandard2.0 API
#   shim. This is the only project in the repo that builds without
#   $(WorldBoxPath) and its private Managed/ + NML/Assemblies/ DLLs.
#
# What this does NOT build:
#   WorldSphereMod.csproj (the actual Harmony mod) and WorldSphereTester.
#   Both require proprietary WorldBox assemblies (`Assembly-CSharp*.dll`,
#   `UnityEngine.*.dll`, `NeoModLoader.dll`, ...) that we cannot legally
#   ship inside a public Docker image. The full mod is a *local-only*
#   build — see CLAUDE.md and Directory.Build.props for the
#   `WORLDBOX_PATH` contract.
#
# Why bother:
#   - CI sanity check that the public API surface still compiles.
#   - Reproducible SBOM / dependency graph for the API shim.
#   - A baseline image other CI jobs can chain off of.
#
# Nobody is expected to `docker run` this for normal mod development.
# =============================================================================

# -----------------------------------------------------------------------------
# Stage 1 — build the Unity-free API project with the .NET 8 SDK.
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Copy just what the API project needs. Directory.Build.props is repo-wide
# but harmless here — it only sets $(WorldBoxPath) which the API doesn't use.
COPY Directory.Build.props ./
COPY WorldSphereAPI.csproj ./
COPY WorldSphereAPI/        ./WorldSphereAPI/

RUN dotnet restore WorldSphereAPI.csproj
RUN dotnet build   WorldSphereAPI.csproj -c Release --no-restore /p:ContinuousIntegrationBuild=true
RUN dotnet publish WorldSphereAPI.csproj -c Release --no-build -o /out

# -----------------------------------------------------------------------------
# Stage 2 — minimal Alpine runtime stage. Mostly symbolic: the output is a
# netstandard2.0 library, so there's no entrypoint to "run". Having a small
# final image keeps the SBOM clean and the layer count low.
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS final

LABEL org.opencontainers.image.title="WorldSphereMod3D API"
LABEL org.opencontainers.image.description="CI/SBOM image for the Unity-free WorldSphereAPI shim. Does NOT contain the WorldBox mod itself."
LABEL org.opencontainers.image.source="https://github.com/MelvinShwuaner/WorldSphereMod"
LABEL org.opencontainers.image.licenses="See repository LICENSE"

WORKDIR /app
COPY --from=build /out/ ./

# No ENTRYPOINT — this is a library. `docker run` will exit cleanly.
CMD ["true"]
