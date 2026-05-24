# WSM3D Cross-Cutting Concerns Survey

Scope: WSM3D runtime, bridge, UI, locales, and bundle/loading paths.

## Observability

- Gap 1: logging is human-readable but not schema-driven. `BridgeServer` emits ad hoc strings and a JSON payload, while the stats overlay prints a fixed text line; neither carries stable fields like `event`, `component`, `request_id`, `setting_key`, or `duration_ms`. See [BridgeServer.cs](../../WorldSphereMod/Code/Bridge/BridgeServer.cs) and [RuntimeStatsOverlay.cs](../../WorldSphereMod/Code/Worldspace/RuntimeStatsOverlay.cs).
- Gap 2: telemetry stops at local console/UI and a pull endpoint. There is `/telemetry` plus the overlay, but no exporter, trace propagation, or alert hook for bridge failures, invalid requests, bundle load failures, or sustained perf regressions. `LocaleKeyCoverageTests` shows the repo already uses test-time verification for content invariants, but there is no runtime observability equivalent.
- Recommended improvement: define a tiny WSM3D log/metric contract and emit structured JSON for bridge events, plus a single metric sink that can later map to OpenTelemetry/Prometheus. Add a request id to BridgeRPC and record per-request outcome, parse failure, and setting mutation latency.

## Security

- Gap 1: BridgeRPC validation is type-based, not policy-based. `UpdateSetting()` accepts any public `SavedSettings` field by name, parses by type, writes it immediately, and applies phase toggles; there is no allowlist, authn/authz, replay protection, or rate limit on rapid settings updates. See [BridgeServer.cs](../../WorldSphereMod/Code/Bridge/BridgeServer.cs).
- Gap 2: asset trust is implicit. The mod loads `worldsphere` via NeoModLoader and uses the bundle contents without checksum or manifest verification. There is also no secrets abstraction in the mod/bridge path; any future Steam token or Anthropic key would need explicit env/secret-provider handling, redaction, and rotation before it is safe to use in journey verification tooling.
- Recommended improvement: add an explicit BridgeRPC policy layer with an allowlist, per-IP/request throttling, and optional local auth token; add SHA-256 verification for bundles against a checked-in manifest; keep any external API credentials out of JSON settings and read them only from environment or a secret store.

## Accessibility

- Gap 1: there is no user-facing accessibility mode for color contrast. The UI and voxel paths use fixed colors and a single debug outline path; I did not find a color-blind palette, outline-contrast mode, or high-contrast theme toggle. See [WorldSphereTab.cs](../../WorldSphereMod/Code/WorldSphereTab.cs) and [RuntimeStatsOverlay.cs](../../WorldSphereMod/Code/Worldspace/RuntimeStatsOverlay.cs).
- Gap 2: text/input affordances are fixed. UI labels are hard-coded at 10-14 pt with built-in Arial, and there is no font-scale setting or key-rebind surface for the mod-specific controls. The current UI also has no screen-reader hint layer or accessibility metadata.
- Recommended improvement: add a small accessibility section in `SavedSettings` with contrast preset, outline intensity, and font scale; route labels through a shared text helper; expose mod actions through rebindable shortcuts and add concise hint text for major controls.

## Internationalization

- Gap 1: English coverage is tested, but translation coverage is not. `LocaleKeyCoverageTests` verifies that `en.json` contains every key used by the 3D phase UI, yet the repo does not enforce completeness for `ru.json`, `cz.json`, or `ch.json`. Those files are partial, and the latter two include comments, so they are not strict JSON. See [LocaleKeyCoverageTests.cs](../../tests/WorldSphereMod.Tests.Unit/LocaleKeyCoverageTests.cs) and [WorldSphereMod/Locales](../../WorldSphereMod/Locales).
- Gap 2: locale-aware formatting is mostly absent. Numeric UI text is built with raw interpolated values in `WorldSphereTab.addText()` and `GenerateSlider()`, so decimals follow the invariant/default rendering instead of the player locale. There is also no RTL layout path or bidi-aware text handling.
- Recommended improvement: switch locale files to a strict key/value structure per locale, add a completeness check for all shipped locales, and format numbers through `CultureInfo.CurrentCulture` or a locale formatter. If RTL languages are in scope, make text alignment and layout direction data-driven rather than hard-coded left-to-right.

## Bottom line

WSM3D already has some useful building blocks: a bridge endpoint, a telemetry payload, and English locale coverage tests. The main gaps are that those pieces are not yet standardized, hardened, or localized enough to scale beyond the current default-English, local-only, developer-facing workflow.
