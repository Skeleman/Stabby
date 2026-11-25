---
name: tests
description: What Stabby's tests cover, how to run them, and how to add new ones while keeping the suite fast and deterministic. Use when adding, changing or debugging tests in tests/Stabby.World.Tests.
---

# Tests

Run (Release, after a build):
```
dotnet build -c Release -v q
dotnet test -c Release --no-build
dotnet test -c Release --no-build --filter SameSeedSameWorld
```

`tests/Stabby.World.Tests/WorldTests.cs` covers:
- **Seed sweep** - seeds 1-20 pass every `WorldChecks` invariant; `check.sh`
  sweeps seeds 1-100 at 256 and 1-20 at 512 with the same checks.
- **Determinism** - same seed gives the same `Fingerprint()`, different seeds
  differ; an edited sketch saved and reloaded gives the same world.
- **Authored edits** honoured; `--rain` terrain stage skips climate and biomes.
- **Physical sanity** - rain shadows behind ranges; worlds have rivers, lakes
  and mountains.
- **Content** - biomes load and match their schema.
- **Architecture** - Stabby.World has no engine, file, console or JSON references.

Rules:
- Read full worlds through `TestWorlds`: `World(seed, size)` and
  `Get(seed, size)` (world, detail, edits, object field) are generated once
  per (seed, size) and shared by every test class. Never mutate them; use
  `TestWorlds.Fresh` for a world you change or a determinism check. Build a
  new `ObjectField` from the shared parts when a test records deltas.
- Smaller worlds: `Cfg(seed, size)` scales cells (`60 * 512 / size` m) so the
  world keeps its ~30 km span. Only go larger when a test needs detail.
- Invariants belong in `WorldChecks`, not in test asserts; tests call
  `WorldChecks.Run` so the CLI sweep checks the same thing.
- Never pin a fingerprint value. Compare two runs instead; pinned hashes
  break on every intended GeneratorVersion bump.
- No `System.Random`, wall clock or file order dependence in tests.
- Keep the suite under about a minute; a slow test is a bug.

## Time budgets

Every test has a budget (CLAUDE.md rule 14): 10 s, or the ceiling its line in
`tools/scripts/time-budget.txt` gives (`test Class.Method seconds reason`).
Sweep check groups have a per-seed median and max in ms there too
(`sweep size group median max reason`). `check.sh` runs `budget.sh`: a BUDGET
line at 1.5x a budget (unlisted tests: over 10 s) only warns; a RUNAWAY line
at 5x or over 10 min fails the check. A new test or check must fit its budget,
or the change adds or raises its line and says why in the commit message.
Expensive invariants go in sweep check groups, not per-seed tests.
