#!/usr/bin/env bash
# Supervisor merge train: train.sh <branch>... Merges each branch as its own commit, runs one full
# check, pushes all only if it passes. On a conflict or failure main is reset to origin/main.
. "$(dirname "$0")/env.sh"; cd "$ROOT"
[ $# -gt 0 ] || { echo "usage: train.sh <branch>..."; exit 2; }
[ "$(git branch --show-current)" = main ] || { echo "not on main"; exit 2; }
git fetch -q origin && git merge -q --ff-only origin/main || { echo "main not fast-forward to origin"; exit 2; }
undo() { git merge --abort 2> /dev/null; git reset -q --hard origin/main; }
for br in "$@"; do
  if ! git merge-tree --write-tree --name-only HEAD "origin/$br" > /dev/null 2>&1; then echo "CONFLICT: $br against the train so far"; undo; exit 1; fi
  echo "$br: commits $(git rev-list --count HEAD..origin/$br), comment lines $(git diff HEAD...origin/$br -- '*.cs' '*.shader' '*.hlsl' '*.compute' | grep -c -E "^\+\s*(//|/\*)")"
  git merge -q --no-ff "origin/$br" -m "Merge $br" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01No8Ts9XNFMeBwqC9oEsoTi" || { echo "merge failed: $br"; undo; exit 1; }
done
if bash tools/scripts/check.sh "" --full; then git push -q && echo "PUSHED $(git log --oneline -1) ($# branches)"
else echo "TRAIN FAILED: main reset to origin/main; split the train to find the branch"; undo; exit 1; fi
