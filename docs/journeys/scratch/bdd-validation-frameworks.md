# BDD + Validation Framework Survey for WSM3D

Scope: `BridgeServer`, `SavedSettings`, journey manifests, and voxel/mesh invariants. Current baseline is inline parsing in [`WorldSphereMod/Code/Bridge/BridgeServer.cs`](../../WorldSphereMod/Code/Bridge/BridgeServer.cs), plain-field JSON settings in [`WorldSphereMod/Code/SavedSettings.cs`](../../WorldSphereMod/Code/SavedSettings.cs), and version bump logic in [`WorldSphereMod/Code/Core.cs`](../../WorldSphereMod/Code/Core.cs).

| Need | Best-fit library | License | Integration cost | Fit for WSM3D |
|---|---|---|---|---|
| BDD-style behavior tests | **Reqnroll** | BSD-3-Clause | Medium-high | Best .NET Gherkin stack today; good for executable journey scenarios. Prefer Reqnroll over SpecFlow for new work because Reqnroll is the maintained fork and SpecFlow is EOL. |
| Input validation | **FluentValidation** | Apache-2.0 | Low-medium | Cleanest way to replace inline `if`/parse checks on BridgeRPC DTOs, settings writes, and asset metadata. Easy to keep validators close to request models. |
| Contract validation | **NJsonSchema** | MIT | Medium | Natural extension of the journey-manifest JSON Schema approach into BridgeRPC request/response and `SavedSettings` disk format. Validates shape at the boundary instead of trusting ad hoc parsing. |
| Schema migration for `SavedSettings` | **Custom versioned migrator** | N/A | Medium-high | There is no single dominant JSON-settings migration framework here. The current `Version` field in `SavedSettings` makes a small ordered migration pipeline the least risky path. |
| Property-based testing | **FsCheck** | BSD-3-Clause | Medium | Strong fit for voxel/mesh invariants such as “generated mesh has positive vertex count” and “random sprite inputs never produce invalid bounds.” |
| Domain validation / value objects | **Internal value objects + guard clauses** | N/A | High | Best treated as an architectural pattern rather than a package. Useful for rules like positive `VoxelScaleMultiplier` or constant `ZDisplacement`, but it is invasive and should follow the boundary validators. |

## Top 3 by Impact

1. **Contract validation**
   - Highest leverage because it can cover BridgeRPC plus `SavedSettings` serialization with one schema-driven approach.
   - Lowest-risk way to make the current JSON surfaces explicit and testable.

2. **Input validation**
   - Immediate payoff for `BridgeServer.UpdateSetting()` and any future RPC endpoints.
   - Replaces scattered parse/branch logic with reusable validators and clearer error responses.

3. **Schema migration for `SavedSettings`**
   - Prevents old configs from breaking when fields are added or renamed.
   - Matches the repo’s existing versioned settings model instead of relying only on Json.NET defaults.

## Second Tier

- **BDD / Reqnroll** is the best authoring layer if you want Gherkin scenarios for journey-like behavior specs, but it overlaps with phenotype-journeys and has more tooling overhead than boundary validation.
- **FsCheck** is the best way to harden geometry and voxelization invariants, but it pays off most after the request/config contracts are already stable.
- **Domain validation** is architecturally clean, but it is a refactor strategy, not a quick framework win.

## Short Take

If the goal is maximum practical impact for WSM3D, do this order: **schema contracts first, runtime input validation second, settings migration third**. Add **Reqnroll** and **FsCheck** after that if you want stronger behavior specs and randomized algorithm checks.
