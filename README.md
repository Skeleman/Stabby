# Stabby

Successor to Punchy, built engine-independent first. Phase 0 is a standalone
world core: a seed produces an editable sketch (coast and mountain ranges),
then erosion, climate, lakes, rivers and biomes are derived from it.

```
dotnet test
dotnet run --project tools/Stabby.Cli -c Release -- gen --seed 4
dotnet run --project tools/Stabby.Cli -c Release -- gen --seed 4 --sketch out/seed4/sketch.json
dotnet run --project tools/Stabby.Cli -c Release -- sweep --count 100 --size 128
```

`gen` writes `out/seed<N>/`: `sheet.png` (sketch, terrain, precipitation,
biomes), the single maps, `metrics.json`, and `sketch.json`. Edit
`sketch.json` (range polylines, strength, width, land fraction) and pass it
back with `--sketch` to regenerate from the hand-adjusted sketch.

## Layout

| Path | Role |
|---|---|
| `src/Stabby.World` | Pure generation library (netstandard2.1, no engine, no file access) |
| `src/Stabby.Content` | Loads and validates `content/*.json` into World types |
| `content/` | Authored data (biomes) |
| `tools/Stabby.Cli` | `gen` and `sweep` commands, PNG output |
| `tests/Stabby.World.Tests` | Seed sweep, determinism, rain shadow, architecture rules |

## Pipeline

sketch → initial terrain → stream-power erosion (implicit, Braun & Willett
2013) with precipitation from a provisional climate → final climate → lakes by
priority flood, D8 routing, discharge, river reaches → biome classification.
