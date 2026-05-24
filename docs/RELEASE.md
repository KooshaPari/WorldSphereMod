# Release checklist

Cut a pre-release after `VERSION`, `WorldSphereMod/mod.json`, and `CHANGELOG.md` agree on the same semver (without a leading `v`).

## Tag and push

```bash
git tag -a v2.0.0-beta.5 -m "v2.0.0-beta.5"
git push origin v2.0.0-beta.5
```

Pushing the tag triggers [`.github/workflows/release.yml`](../.github/workflows/release.yml), which builds `WorldSphereAPI`, extracts release notes from `CHANGELOG.md` (`[Unreleased]` when non-empty, otherwise `## [<version>]`), and publishes a GitHub pre-release.

## Version sources

| File | Role |
|------|------|
| `VERSION` | Single-line semver for tooling and tests |
| `WorldSphereMod/mod.json` | NeoModLoader manifest version |
| `CHANGELOG.md` | Keep a Changelog sections per release |

E2E tests assert `mod.json` matches `VERSION` and that `CHANGELOG.md` contains a `## [<version>]` header for the current release.
