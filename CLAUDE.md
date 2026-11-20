# Stabby rules

1. `Stabby.World` never references UnityEngine, System.IO file APIs, Console or
   System.Text.Json. A test enforces it. It must compile in Unity unchanged
   (netstandard2.1, C# 9, no records, no `init`).
2. One representation per fact. Terrain, water surface and rivers all come from
   the same drainage graph (`Drainage`); never derive a second copy.
3. Authored data lives in `content/` and is read only by `Stabby.Content`.
4. Every world invariant lives in `WorldChecks`. Tests and the CLI sweep both
   call it; add a check there before fixing the bug it catches.
5. Randomness only through `Rng.Stream(seed, "name")`, one named stream per
   purpose, so adding a stream never shifts another. No `System.Random`,
   no trig in generation paths (cross-platform float drift). Math.Pow/Exp
   remain in Erosion, Hydrology and Sketch; replace them with own
   implementations before worlds are synced across machines.
6. Changing generation output bumps `WorldConfig.GeneratorVersion`.
7. Comments only for reasons, not narration.
8. Files are LF (`.gitattributes`).
9. This file is the architecture contract. Foundational changes to it need
   Zach's review; a PreToolUse hook asks before any edit. Stage outputs stay
   separable: sketch, terrain + hydrology (`--rain`), then climate and biomes.
