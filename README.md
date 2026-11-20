# Stabby

Successor to Punchy, built engine-independent first. Phase 0 is a standalone
world core: a seed produces an editable sketch (coast and mountain ranges),
then erosion, climate, lakes, rivers and biomes are derived from it.

## Usage

```
dotnet build -c Release -v q
dotnet test -c Release --no-build
dotnet run --project tools/Stabby.Cli -c Release -- <command>
```

| Command | Does |
|---|---|
| `gen --seed N [--size 512] [--out dir] [--scale k] [--sketch sketch.json] [--rain mm]` | One world into `out/seed<N>/` |
| `sweep [--from 1] [--count 100] [--size 256] [--rain mm]` | Runs `WorldChecks` on many seeds, prints timings and violations |

- `gen` writes `map.png`, `sketch.png`, `biomes.png`, `precipitation.png`,
  `sheet.png` (all of them side by side), `sketch.json` and `metrics.json`.
- Edit `sketch.json` (islands, ranges, corridors, regions, towns) and pass it
  back with `--sketch` to regenerate from the hand-adjusted sketch.
- `--rain mm` stops after terrain and hydrology with uniform rainfall: no
  climate, no biomes. Useful for isolating erosion and rivers.
- `--size` changes resolution, not extent: every size spans about 30 km
  (default 512 cells of 60 m); smaller sizes use coarser cells.
- `--scale` sets PNG pixels per cell (default 3 at 256 and below, 2 at 512, 1 above).

## Layout

| Path | Role |
|---|---|
| `src/Stabby.World` | Pure generation library (netstandard2.1, no engine, no file access) |
| `src/Stabby.Content` | Loads and validates `content/*.json` into World types |
| `content/` | Authored data (biomes) and JSON schemas |
| `tools/Stabby.Cli` | `gen` and `sweep` commands, PNG output |
| `tests/Stabby.World.Tests` | Seed sweep, determinism, rain shadow, architecture rules |

## Pipeline

1. **Sketch** - two islands, mountain ranges, drainage corridors, biome
   regions and town landmarks; editable as `sketch.json`.
2. **Initial height** from the sketch.
3. **Provisional climate** - precipitation for erosion.
4. **Stream-power erosion** (implicit, Braun & Willett 2013).
5. **Final climate** - temperature and precipitation with rain shadows.
6. **Hydrology** - lakes by priority flood, D8 routing, discharge, river
   reaches; terrain, water and rivers share one drainage graph.
7. **Biomes** classified from climate using `content/biomes.json`.
8. **Authored regions** from the sketch applied on top.

`WorldChecks` holds every invariant; tests and `sweep` both run it.
Changing output bumps `WorldConfig.GeneratorVersion`. Rules for contributors
are in `CLAUDE.md`; project skills live in `.claude/skills/`.

## Unity viewer

`unity/` is a Unity 6000.5.8f1 URP project that loads `src/Stabby.World` as a local
package and draws a generated world: 64-cell chunks with five screen-size LODs and
skirts, smoothed river ribbons at their generated widths, lakes and a sea plane.
Open `Assets/Scenes/Viewer.unity` and press Play (RMB look, WASD, Q/E, Shift, wheel).
Batch shots and stats: `Unity -batchmode -projectPath unity -executeMethod
Stabby.Viewer.Editor.ViewerSetup.Capture [-stabbySeed N]` writes `out/unity/seedN/`.
