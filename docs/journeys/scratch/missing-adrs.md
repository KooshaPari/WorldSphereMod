# Missing ADRs Audit

Audit result: the ADR set is **not uniformly template-complete**.

Covered well:
- `0001`, `0004`, `0005`, `0016`, `ADR-0008`, `ADR-0010`, and `ADR-0012` have explicit `Decision` / `Consequences` sections and dated status metadata.
- `0002` / `0003` use the older table-style status/date format but still record the decision and consequences.

Gaps:
- `0011` is the weakest record: it has no explicit `Date`, `Decision`, `Consequences`, or `Status` headings.
- `0012`-`0015` are postmortem-style notes, not canonical ADRs. They usually have `Status` and `Consequences`, but they omit an explicit `Decision` section, so they do not satisfy the requested ADR shape.
- `ADR-0007` followup / main and `ADR-0009` also miss at least one required section (`Decision` or `Consequences`), so they are only partial ADRs.

Session decisions:
- `BridgeServer` port `8766`: **not backed by an ADR**. This is a runtime-facing port choice and should be captured in a dedicated ADR.
- `MeshInstanceBatcher.ForceFallbackPath()` defaulting `true`: **not backed by an ADR**. This changes the steady-state render path and deserves an ADR.
- `SavedSettings.VoxelSpriteDepth = 3`: **not backed by an ADR**. This is a visible geometry policy choice and should be recorded.
- `Sprites/Default` as the fallback material: **already backed in part** by ADR-0002’s shader fallback chain, so it does not need a new ADR unless the repo wants a separate material-selection contract.

Three missing-ADR moments:
1. BridgeServer HTTP port `8766`.
2. `ForceFallbackPath` defaulting to `true`.
3. `VoxelSpriteDepth` defaulting to `3`.
