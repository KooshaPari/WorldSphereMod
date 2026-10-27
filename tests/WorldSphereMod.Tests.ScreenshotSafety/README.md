# Automatic screenshot safety regression tests

This standalone project compile-links the actual production driver, capture
coroutine, and settings. Its fake Unity assembly lets the tests advance each
interval/end-of-frame yield explicitly and observe capture invocation. It is not
a Unity runtime test and does not create or verify a PNG.

Run the dedicated target explicitly (not included in `test-all` or the solution's
default test target, because a caller-approved scratch root is required):

```powershell
$env:WSM_SCREENSHOT_TEST_ROOT = 'C:/Users/koosh/agents/sandbox/wsm-screenshot-safety/tests'
task test-screenshot-safety
# Equivalent: just test-screenshot-safety
# Without either runner: pwsh -NoProfile -File Tools/test-screenshot-safety.ps1
```

The common PowerShell entrypoint rejects missing/relative/filesystem-root values
before invoking dotnet. Build artifacts and test results go beneath that root in
`artifacts/` and `results/`; no default path is inferred.

Use an approved explicit absolute scratch root on other machines. Each test uses
a unique child directory. Successful cases create directories, but the fake
capture only records the requested filename. Artifacts are preserved, not deleted.

The automatic path must be an explicit canonical absolute local directory, no
more than 180 characters (space reserved for the generated filename). Roots,
UNC/device paths, relative or dot-normalized paths, wildcard/invalid/reserved
components, trailing dots/spaces, files, and detected reparse ancestors are
rejected without choosing a fallback. Equivalent native/alternate separators
are accepted. Reparse checks are a defensive check, not a security boundary
against a concurrent filesystem attacker.

An invalid path or capture failure stops that component's loop until the
component is disabled and enabled again. Changing only the setting does not
restart a terminal loop. The successful-capture limit remains 20 per component;
disabled polling and cancelled pending captures do not count as successes.
Duplicate components do not automatically take over from a disabled owner.

Persisted settings are not migrated: an old explicitly enabled valid path stays
enabled. The new OFF/empty defaults only apply when settings are newly created
or those fields are absent. A live test must supply isolated settings with an
approved output directory before launch. The manual two-argument capture API
and manual camera capture remain independent of automatic settings.

Cancellation can prevent a capture before it is submitted to Unity; it cannot
withdraw a screenshot request already submitted to the engine. These offline
tests do not prove engine scheduling, actual image persistence, save isolation,
or protection against filesystem link changes racing validation and writing.
