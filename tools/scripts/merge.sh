#!/usr/bin/env bash
# Supervisor merge: merge.sh <branch> ["one-line summary"]. Run in the main checkout on main,
# in the background (full tests take minutes). Pushes only if every check passes.
. "$(dirname "$0")/env.sh"; cd "$ROOT"
br="$1"; sum="${2:-}"; [ -n "$br" ] || { echo "usage: merge.sh <branch> [summary]"; exit 2; }
[ "$(git branch --show-current)" = main ] || { echo "not on main"; exit 2; }
git fetch -q origin && git merge -q --ff-only origin/main || { echo "main not fast-forward to origin"; exit 2; }
mkdir -p out
if ! git merge-tree --write-tree --name-only origin/main "origin/$br" > out/merge-tree.txt 2>&1; then
  echo "CONFLICTS:"; sed -n '2,/^$/p' out/merge-tree.txt; exit 1; fi
echo "commits: $(git rev-list --count main..origin/$br)  files: $(git diff --name-only main...origin/$br | wc -l)"
echo "new comment lines:"; git diff main...origin/$br -- '*.cs' '*.shader' '*.hlsl' '*.compute' | grep -E "^\+\s*(//|/\*)" | head -15
msg=$(mktemp); { echo "Merge $br"; [ -n "$sum" ] && { echo; echo "$sum"; }; echo
  echo "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  echo "Claude-Session: https://claude.ai/code/session_01No8Ts9XNFMeBwqC9oEsoTi"; } > "$msg"
git merge -q --no-ff "origin/$br" -F "$msg" || { echo "merge failed"; git merge --abort; exit 1; }
rm -f "$msg"
pass="$(cd "$(git rev-parse --git-common-dir)" && pwd)/stabby-check-pass/$(git rev-parse 'HEAD^{tree}')"
if [ -f "$pass" ]; then echo "checks: skipped, this exact tree passed a full check ($(cat "$pass"))"; git push -q && echo "PUSHED $(git log --oneline -1)"
elif bash tools/scripts/check.sh "" --merge | tee out/merge-check.txt; [ "${PIPESTATUS[0]}" = 0 ]; then
  git push -q && echo "PUSHED $(git log --oneline -1)" && { grep '^BUDGET' out/merge-check.txt || true; }
else echo "CHECKS FAILED: merge commit left local, not pushed (git reset --hard origin/main to undo)"; grep -E '^(BUDGET|RUNAWAY)' out/merge-check.txt; exit 1; fi
