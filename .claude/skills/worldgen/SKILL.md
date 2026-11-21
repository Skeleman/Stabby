---
name: worldgen
description: How to change Stabby world generation (sketch, terrain, erosion, climate, hydrology, biomes, regions) safely. Use before editing anything under src/Stabby.World that alters generated output.
---

# Changing generation

Pipeline order (`WorldGenerator`): sketch -> initial height -> provisional
climate -> stream-power erosion -> final climate -> hydrology (priority-flood
lakes, D8 rivers) -> biomes -> authored regions.

1. **Check first.** Write the invariant in `WorldChecks` and confirm it fails on
   the current code (`stabby sweep`) before fixing anything.
2. **Randomness.** New behaviour gets its own `Rng.Stream(seed, "purpose")`.
   Never draw extra values from an existing stream; that shifts every world.
3. **No trig, no new Pow/Exp** in generation paths. Use polynomial or table
   approximations, integer grid steps, or vectors.
4. **One drainage graph.** Read rivers, lakes and water levels from `Drainage`
   and `Hydrology`; never recompute flow elsewhere.
5. **Bump** `WorldConfig.GeneratorVersion` when output changes.
6. **Verify** (Release build; `stabby` = `dotnet run --project tools/Stabby.Cli -c Release --`):
   ```
   stabby sweep --count 100 --size 256    # 0 violations
   stabby sweep --count 20 --size 512     # 0 violations, note median/max ms
   stabby sweep --from 3 --count 2 --size 2048 --jobs 2   # river banks at 11 m cells
   stabby gen --seed 4                    # then open out/seed4/sheet.png
   ```
   Use `--rain 1200` to isolate terrain and hydrology from climate.
   Look at the sheet: coasts, ranges, rivers reaching the sea, lakes, rain
   shadow, biome bands. Compare against the sheet from before the change.
7. Run `dotnet test -c Release --no-build`, then the architecture-check skill.

Report: what changed, sweep numbers (violations, median/max ms) for both
sizes, the seeds you looked at, and the new GeneratorVersion.
