# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| `2.0.x` (current `claude/research-ultraplan-fork-DdgI5`) | ✅ |
| `1.x` (upstream `MelvinShwuaner/WorldSphereMod`) | upstream maintainer |

## Reporting a Vulnerability

Email **kooshapari@gmail.com** with `[WorldSphereMod3D security]` in the subject.
Include: affected version, reproduction steps, impact assessment, and a
suggested mitigation if you have one. We will acknowledge within 72 hours and
aim to ship a fix within 14 days for critical issues.

Do **not** open a public GitHub issue for security reports.

## Scope

This project is a WorldBox mod loaded by NeoModLoader (NML). The threat surface
is limited to:

- The compiled `WorldSphereMod3D.dll` running inside WorldBox.
- Public API methods on `WorldSphereAPI.dll` consumed by external mods.
- The `Tools/install.ps1` installer script (touches the WorldBox `Mods/`
  directory but does not request elevation).

Out of scope:

- The WorldBox game itself or its base assemblies. Report upstream to
  Maxim Karpenko.
- The NeoModLoader runtime. Report to the NML maintainers.

## A note on decompile work

`docs/render-data-fields.md`, `docs/phase3-decompile-findings.md`, and other
docs reference field maps obtained by decompiling
`Assembly-CSharp-Publicized.dll`. This is the *publicized* variant produced and
distributed by NML for mod compatibility — decompiling it is the supported path
for mod authors. We do not redistribute decompiled source; only the field
inventory needed to write Harmony patches against the correct member names.
