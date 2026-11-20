# Stabby rules

1. `Stabby.World` never references UnityEngine, System.IO file APIs, Console or
   System.Text.Json. A test enforces it. It must compile in Unity unchanged
   (netstandard2.1, C# 9, no records, no `init`).
2. One representation per fact. Ownership flows heightfield -> drainage graph
   (`Drainage`: fill, receivers, stack) -> hydrology (water surface, lakes,
   rivers) -> biomes. Each stage reads the one before it and never derives a
   second copy.
3. Authored data lives in `content/` and is read only by `Stabby.Content`.
4. Every world invariant lives in `WorldChecks`. Tests and the CLI sweep both
   call it; add a check there before fixing the bug it catches.
5. Randomness only through `Rng.Stream(seed, "name")`, one named stream per
   purpose, so adding a stream never shifts another. No `System.Random`, no
   trig in generation paths; Pow, Exp and Log go through `FMath`. Same seed +
   generator version is bit-identical on the same runtime and CPU
   architecture; small float drift across platforms is allowed, and host
   chunk sync (`WorldChunks`) corrects it.
6. Changing generation output bumps `WorldConfig.GeneratorVersion`.
7. No code comments; reasons go in commit messages. `tools/Stabby.Lint` runs
   in the tests and also requires Unity Game systems to be Burst `ISystem`
   unless `lint-allow.txt` gives a one-line reason.
8. Files are LF (`.gitattributes`).
9. This file is the architecture contract. Foundational changes to it need
   Zach's review; a PreToolUse hook asks before any edit. Stage outputs stay
   separable: sketch, terrain + hydrology (`--rain`), then climate and biomes.
10. Unity code never blocks the main thread on heavy work. Generation, sync,
    meshing and other bulk work run in tasks or jobs; the main thread only
    applies finished results within a per-frame budget. `tools/Stabby.Lint`
    rejects blocking waits and unchecked `Task.Result` in runtime Unity code
    unless `lint-allow.txt` gives a reason. Runtime game data is unmanaged
    (ECS components, blobs, native containers) so Burst and jobs can use it;
    managed code stays at load and network boundaries and converts once.
