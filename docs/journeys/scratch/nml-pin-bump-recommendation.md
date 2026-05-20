# NeoModLoader pin audit (2026-05-20)

## Findings
- `WorldSphereMod.csproj`: references `NeoModLoader` only as a local assembly reference:
  - `$(WorldBoxPath)/worldbox_Data/StreamingAssets/mods/NeoModLoader.dll`
  - No GitHub repository URL or tag is pinned.
- `WorldSphereAPI.csproj`: has no `NeoModLoader` references.

## Interpretation
- There is no outdated fork URL or tag hard-coded in either csproj.
- This is not currently a NuGet/reference-manager pin; loader fork selection is operational/deployment-time (file in `worldbox_Data/...`).

## If/when adding a fork pin
Given active fork guidance, pin to:
- GitHub URL: `https://github.com/WorldBoxOpenMods/ModLoader`
- Suggested tag: `v1.2.0.1`

Use this in docs/build instructions and any future dependency lock mechanism if one is introduced.
