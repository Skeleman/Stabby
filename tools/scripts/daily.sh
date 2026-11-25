#!/usr/bin/env bash
# Daily full check of origin/main in its own worktree (merges sweep fewer seeds). Records the last
# passing commit; on a failure lists the merges since it, so the culprit is among them.
. "$(dirname "$0")/env.sh"; cd "$ROOT"
common="$(cd "$(git rev-parse --git-common-dir)" && pwd)"; tree="$common/stabby-daily"; last="$common/stabby-daily-pass"
git fetch -q origin
if [ -d "$tree" ]; then git -C "$tree" checkout -q --detach origin/main; else git worktree add -q --detach "$tree" origin/main; fi
sha=$(git -C "$tree" rev-parse --short HEAD)
if (cd "$tree" && bash tools/scripts/check.sh "" --full --no-unity); then echo "$sha" > "$last"; echo "DAILY PASS $sha"
else
  echo "DAILY FAILED $sha; merges since the last pass ($(cat "$last" 2> /dev/null || echo none)):"
  [ -f "$last" ] && git log --oneline --merges "$(cat "$last")..$sha" | head -30
  exit 1
fi
