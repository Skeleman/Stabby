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
3. `bash tools/scripts/check.sh [filter]` before every commit: build, filtered
   tests, sweeps, Unity compile, LF, comment count. Add `--no-unity` while
   iterating. It finds the user-profile .NET SDK itself (or set DOTNET_HOME).
   Don't run the full suite; the supervisor runs it at merge.
4. Shots: capture once on final code, view each at <= 1280 px, once.
5. Commit resumable WIP, push the branch, then hand back with: what changed,
   check.sh summary lines, sweep numbers, GeneratorVersion, shot paths, open
   issues. No narration.
