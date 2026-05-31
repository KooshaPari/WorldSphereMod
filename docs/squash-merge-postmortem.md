# PR #7 squash-merge postmortem

PR #7 was a 232-commit integration branch that was squash-merged to `main` as
commit `4efa128`. That choice collapsed the entire history into one commit and
destroyed the cherry-pick chain that `docs/branch-merge-plan.md` had already
warned must be preserved.

The squash also introduced two concrete regressions in the current tree:

- The SafeShaders crash-fix was dropped. The tree now carries the full shader
  list again, which reproduces the native Unity crash on the second shader load.
- The `External/Compound-Spheres` submodule pointer was reverted back to
  upstream instead of staying on the intended fork/pin.

## What went wrong

The integration branch was treated like a normal feature branch. It was not.
For a megabase branch with many dependent follow-up commits, squash-merging
erases the commit topology that later phases rely on for selective cherry-picks
and surgical reversions.

That made the merge both harder to reason about and easier to regress:

- history became one opaque squash commit instead of a traceable merge series;
- the SafeShaders gating work was effectively lost;
- the submodule pointer drifted back to upstream.

## Lesson

Never squash-merge a large integration branch.

Use a merge commit or a fast-forward instead so the commit chain stays intact
and the branch remains safe to cherry-pick from.
