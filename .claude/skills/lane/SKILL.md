---
name: lane
description: How a Stabby lane agent works a card cheaply - ids, checks, shots and hand-back. Use at the start of every lane card.
---

# Working a lane card

1. `bash tools/scripts/next-ids.sh` for the next GeneratorVersion, capture
   phase, object shape, feature edit shape and discovery kind. Use the ids your
   card names if it names them.
2. Keep context small: few large tool calls, grep big files instead of reading
   them whole, no Monitor or poll loops. Run anything over a minute in the
   background and wait for its completion notice.
3. While iterating, run only the tests you touch with
   `dotnet test -c Release --filter <Class>` (no lock, no sweeps, no Unity).
   Run `bash tools/scripts/check.sh [filter]` once before hand-back, and
   again only after merging main or a review change: build, filtered
   tests, sweeps, Unity compile, LF, comment count. Phases run in parallel
   and the ones your diff cannot affect are skipped; one check runs per
   machine (others wait on a lock, and every run is logged with its wait
   in stabby-checks.csv beside the git dir). WIP commits need no check. Optionally,
   before hand-back, `check.sh "" --full` on the merged-up branch records the tree,
   so merge.sh skips its rerun. It finds the user-profile .NET SDK itself (or set DOTNET_HOME).
4. Shots: capture once on final code with `-force-d3d11` (d3d12 fence
   timeouts crash captures), view each at <= 1280 px, once. Kill a hung or
   crashed capture editor by its PID.
5. Commit resumable WIP, push the branch, then hand back with: what changed,
   check.sh summary lines, sweep numbers, GeneratorVersion, shot paths, open
   issues. No narration.

## Time budgets

New tests and checks fit `tools/scripts/time-budget.txt` (CLAUDE.md rule 14;
the tests skill has the format). If check.sh prints a BUDGET line for your
change, speed it up or raise the budget with the reason in the commit message,
and name the line in the hand-back. BUDGET lines never block a merge; RUNAWAY
(5x or 10 min) fails the check.
