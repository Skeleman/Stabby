---
name: merge
description: Supervisor steps to review and merge a Stabby lane branch. Use when a lane hands back.
---

# Merging a lane

1. Read the hand-back, then `git diff main...<branch> --stat` and the diff of
   src/ and content/ only. Run the architecture-check skill on it.
2. View the lane's shots once at <= 1280 px. Send Zach only fresh captures.
3. `bash tools/scripts/merge.sh <branch> "summary"`: fast-forwards main,
   reports conflicts, merges --no-ff with attribution, runs
   `check.sh --full` and pushes only on pass, or skips the run when that exact
   tree already passed a full check. A failure leaves the merge local.
   Docs-only merges (`*.md`, `ROADMAP*`) skip the quiet wait and checks and
   push at once; others wait at most 15 min for a quiet machine
   (`QUIET_MAX` s), then check anyway. `MERGE_DRY_RUN=1` previews without
   merging or pushing.
4. `python tools/scripts/roadmap-done.py "<row start>" "done (<branch> merged, GV n)"`
   moves the row to ROADMAP-DONE.md. Commit and push.
5. Start the next queued lane as a fresh agent.
