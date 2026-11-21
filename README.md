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
| `gen --seed N [--size 2048] [--out dir] [--scale k] [--sketch sketch.json] [--rain mm]` | One world into `out/seed<N>/` |
| `bake --seed N [--size 2048] [--out file]` | Generates once and writes the world file `out/maps/seed<N>-<size>.stabbymap` that the game loads instead of generating |
| `sweep [--from 1] [--count 100] [--size 256] [--rain mm] [--jobs N] [--checks 0]` | Runs `WorldChecks` on many seeds, prints per-stage time and allocation and any violations; `--jobs 1` for clean timings, `--checks 0` times generation alone |
| `scenarios [--out out/scenarios] [--scan 100]` | Generates each named acceptance scenario and writes a review sheet with the witness marked; `--scan` lists which seeds show each one |

- `gen` writes `map.png`, `sketch.png`, `biomes.png`, `precipitation.png`,
  `sheet.png` (all of them side by side), `sketch.json` and `metrics.json`.
- Edit `sketch.json` (islands, ranges, corridors, regions, towns) and pass it
  back with `--sketch` to regenerate from the hand-adjusted sketch.
- `--rain mm` stops after terrain and hydrology with uniform rainfall: no
  climate, no biomes. Useful for isolating erosion and rivers.
- `--size` changes resolution, not extent: every size spans about 23 km
  (default 2048 cells of 11.25 m); smaller sizes use coarser cells. Above
  `ErosionSize` (512) erosion runs coarse-to-fine: all but `FineSteps` (8)
  steps on the 512 grid, then the rest at full resolution.
- `--scale` sets PNG pixels per cell (default 3 at 256 and below, 2 at 512, 1 above).

## Layout

| Path | Role |
|---|---|
| `src/Stabby.World` | Pure generation library (netstandard2.1, no engine, no file access) |
| `src/Stabby.Content` | Loads and validates `content/*.json` into World types |
| `content/` | Authored data (biomes) and JSON schemas |
| `tools/Stabby.Cli` | `gen`, `bake`, `sweep` and `scenarios` commands, PNG output |
| `tests/Stabby.World.Tests` | Seed sweep, determinism, rain shadow, architecture rules |

## Pipeline

1. **Sketch** - two islands, mountain ranges, drainage corridors, biome
   regions and town landmarks; editable as `sketch.json`.
2. **Initial height** from the sketch.
3. **Provisional climate** - the rain pattern for erosion (scaled to `ErosionRainMm`, so climate tuning never reshapes terrain).
4. **Stream-power erosion** (implicit, Braun & Willett 2013).
5. **Final climate** - one maritime climate: temperature falls with height (steep game lapse rate, tundra up top), rain follows the wind with rain shadows; rates are per metre, so any resolution matches.
6. **Hydrology** - lakes by priority flood (pits under 1.5 ha or 1.5 m deep fill in), D8 routing, discharge, river
   reaches; terrain, water and rivers share one drainage graph.
7. **Biomes** classified from climate using `content/biomes.json`; a biome with height or distance-to-water/coast conditions wins where they hold.
8. **Authored regions** from the sketch applied on top.

## Baselines

`sweep --jobs 1` (one seed at a time; stages use all cores), Release, generator 32. CI (`.github/workflows/ci.yml`)
runs build, tests, lint, `sweep --count 100 --size 256` and `--count 10 --size 512` on every push.
256 is 20 seeds, 512 and 1024 are 5, 2048 is 3, 4096 is 1 with `--checks 0` (`Features` placement
currently throws at 4096). Total excludes checks. Peak working set is about 0.8 GB at 2048 and 2.7 GB at 4096.

| Stage | 256 (median ms / MB) | 512 (median ms / MB) | 1024 (median ms / MB) | 2048 (median ms / MB) | 4096 (ms / MB) |
|---|---|---|---|---|---|
| sketch | 14 / 2.8 | 55 / 11 | 207 / 45 | 829 / 180 | 3113 / 720 |
| erosion | 260 / 8 | 993 / 29 | 1329 / 117 | 3383 / 377 | 9420 / 1414 |
| climate | 1 / 1.8 | 6 / 7 | 46 / 28 | 456 / 112 | 4038 / 448 |
| hydrology | 4 / 4.3 | 17 / 17 | 67 / 66 | 325 / 262 | 1204 / 1043 |
| biomes | 5 / 0.9 | 19 / 3.5 | 76 / 14 | 306 / 56 | 1145 / 224 |
| checks | 69 / 4.5 | 1304 / 91 | 1173 / 113 | 6013 / 423 | - |
| total | 284 | 1089 | 1724 | 5306 | 18920 |

## Scenarios

`Scenarios.All` pins one seed per named case at 256; `ScenarioTests` fails if a
generator change makes one disappear. Rescan with `scenarios --scan 100`, repin,
and review the new sheets.

| Scenario | Seed | Seeds of 100 showing it |
|---|---|---|---|
| river-through-range | 49 | 1 |
| river-by-highland | 8 | 7 |
| lake-outlet-river | 3 | 100 |
| cliff-meets-beach-or-valley | 5 | 100 |
| narrow-channel | 4 | 51 |
| authored-dry-region | 11 + `content/scenarios/authored-dry-region.json` | authored |

## Sharing a world

A world is seed + generator version + sketch. Each peer generates it locally and
compares `WorldChunks.Checksums` (64-cell chunks, also in `metrics.json`) with the
host's; the host sends `WorldChunks.Pack` for each chunk that differs, the peer
calls `Unpack` for each and `Relink` once to rebuild lakes and rivers. Chunks carry
raw float bits, so peers need not be bitwise deterministic.

`WorldChecks` holds every invariant; tests and `sweep` both run it.
Changing output bumps `WorldConfig.GeneratorVersion`. Rules for contributors
are in `CLAUDE.md`; project skills live in `.claude/skills/`.

## Unity viewer

`unity/` is a Unity 6000.5.8f1 URP project that loads `src/Stabby.World` as a local
package and draws a generated world: 64-cell chunks with five screen-size LODs and
skirts, smoothed river ribbons at their generated widths, lakes and a sea plane.
This is full-world async preparation, not streaming: every chunk is meshed on
worker tasks, and the main thread applies terrain, water and river pieces within
`ApplyVertices` and `ApplyMs` per frame. Clearing or reloading cancels stale tasks.
Open `Assets/Scenes/Viewer.unity` and press Play (RMB look, WASD, Q/E, Shift, wheel);
Play loads the baked map file like the game does (and bakes it if missing).
Batch shots and stats always generate, since they read the sketch: `Unity -batchmode -projectPath unity -executeMethod
Stabby.Viewer.Editor.ViewerSetup.Capture [-stabbySeed N]` writes `out/unity/seedN/`.

## Unity game prototype

`Assets/Scenes/Game.unity` runs a host and two clients in one process with
Netcode for Entities. The host loads `out/maps/seed<N>-2048.stabbymap`
(`WorldFile`: raw chunks, rebuilt with `Relink`, about 25 ms) instead of generating
(about 4 s in Unity). If the file is missing, from another generator version or
older than `content/biomes.json`, the host generates and saves it, so run
`stabby bake` after generator changes or let the first run rebake. `Client1`
loads the same file, compares chunk checksums and streams the chunks that differ
(deflated); `Client2` starts from an empty world to exercise a full stream.
Players are predicted capsules walking on the shared terrain. The view client
draws terrain as Entities Graphics chunk entities (32 cells, six LODs chosen
by screen size), meshed by Burst jobs from the ground blob; water reuses the
viewer's geometry. The first Entities Graphics frame in the editor costs about
150 ms (seen with only the sea quad drawn), so `stats.txt` shows two frames
over 33 ms.
Batch run: `Unity -batchmode -projectPath unity -executeMethod
Stabby.Game.Editor.GameCapture.Run [-stabbySpikes [ms]]` writes
`out/unity/game/` (`stats.txt`, chase and overhead shots); `-stabbySpikes`
lists the top profiler markers for every frame over `ms` (default 33).
