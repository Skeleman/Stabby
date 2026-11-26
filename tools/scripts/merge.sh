#!/usr/bin/env bash
# Supervisor merge: merge.sh <branch> ["one-line summary"]. Run in the main checkout on main,
# in the background (full tests take minutes). Pushes only if every check passes.
. "$(dirname "$0")/env.sh"; cd "$ROOT"
br="$1"; sum="${2:-}"; [ -n "$br" ] || { echo "usage: merge.sh <branch> [summary]"; exit 2; }
dry="${MERGE_DRY_RUN:-}"; cap="${QUIET_MAX:-900}"
if [ -n "$dry" ]; then echo "dry run: no merge, no push"; git fetch -q origin || exit 2
else
  [ "$(git branch --show-current)" = main ] || { echo "not on main"; exit 2; }
  git fetch -q origin && git merge -q --ff-only origin/main || { echo "main not fast-forward to origin"; exit 2; }
fi
mkdir -p out
if ! git merge-tree --write-tree --name-only origin/main "origin/$br" > out/merge-tree.txt 2>&1; then
  echo "CONFLICTS:"; sed -n '2,/^$/p' out/merge-tree.txt; exit 1; fi
files=$(git diff --name-only origin/main...origin/$br)
echo "commits: $(git rev-list --count origin/main..origin/$br)  files: $(printf '%s' "$files" | grep -c .)"
echo "new comment lines:"; git diff origin/main...origin/$br -- '*.cs' '*.shader' '*.hlsl' '*.compute' | grep -E "^\+\s*(//|/\*)" | head -15
docs=0; [ -n "$files" ] && ! printf '%s\n' "$files" | grep -vqE '(\.md$|(^|/)ROADMAP[^/]*$)' && docs=1
msg=$(mktemp); { echo "Merge $br"; [ -n "$sum" ] && { echo; echo "$sum"; }; echo
  echo "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  echo "Claude-Session: https://claude.ai/code/session_01No8Ts9XNFMeBwqC9oEsoTi"; } > "$msg"
if [ -n "$dry" ]; then rm -f "$msg"
else git merge -q --no-ff "origin/$br" -F "$msg" || { echo "merge failed"; git merge --abort; exit 1; }; rm -f "$msg"; fi
if [ "$docs" = 1 ]; then
  echo "quiet wait: skipped, docs-only merge"; echo "checks: skipped, docs-only merge"
  [ -n "$dry" ] && { echo "dry run: would push now"; exit 0; }
  git push -q && echo "PUSHED $(git log --oneline -1)"; exit
fi
busy() { powershell -NoProfile -Command "@(Get-CimInstance Win32_Process | ? { \$_.Name -eq 'Stabby.Cli.exe' -or \$_.Name -eq 'Unity.exe' -or (\$_.Name -eq 'dotnet.exe' -and \$_.CommandLine -match ' test |vstest') }).Count" 2>/dev/null | tr -d '\r'; }
t_quiet=$SECONDS; capped=0
while [ "$(busy)" != 0 ]; do [ $((SECONDS - t_quiet)) -ge "$cap" ] && { capped=1; break; }; sleep 30; done
if [ "$capped" = 1 ]; then echo "quiet wait capped after $((SECONDS - t_quiet)) s (cap $cap s), machine still busy; checking anyway"
else echo "quiet wait: $((SECONDS - t_quiet)) s"; fi
[ -n "$dry" ] && { echo "dry run: would run checks, then push on pass"; exit 0; }
pass="$(cd "$(git rev-parse --git-common-dir)" && pwd)/stabby-check-pass/$(git rev-parse 'HEAD^{tree}')"
if [ -f "$pass" ]; then echo "checks: skipped, this exact tree passed a full check ($(cat "$pass"))"; git push -q && echo "PUSHED $(git log --oneline -1)"
elif bash tools/scripts/check.sh "" --merge | tee out/merge-check.txt; [ "${PIPESTATUS[0]}" = 0 ]; then
  git push -q && echo "PUSHED $(git log --oneline -1)" && { grep '^BUDGET' out/merge-check.txt || true; }
else echo "CHECKS FAILED: merge commit left local, not pushed (git reset --hard origin/main to undo)"; grep -E '^(BUDGET|RUNAWAY)' out/merge-check.txt; exit 1; fi
