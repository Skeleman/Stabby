# Roadmap

Status words: done, now, next, later, idea.

## Audit (baseline 250a49b)

| Item | Status |
|---|---|
| Viewer tasks cancelled on reload; outstanding count per task | done |
| Main-thread apply budgeted by vertices + ms; terrain, water, river pieces separate | done |
| Fingerprint covers full sketch and BiomeIds; OutputFingerprint for file round trips | done |
| 1024 baseline (time, allocation, checks) in README | done |
| Lint bans native engine helpers in runtime code (worker safety) | done |
| ChunkZip round trip, malformed payloads, inflate size cap | done |
| Viewer described as full-world async prep, not streaming | done |

## Next

- Real streaming when gameplay needs a bigger world: residency around players,
  eviction, priority, memory budget. The server then packs chunks on demand
  instead of keeping every packed chunk (about 2 MB at 512, fine for now).
- Unity batch CI job (game capture on a runner) if a license setup allows it.
- Generator features resume after the audit: caves, cliffs and river overhaul
  carried over from Punchy, more biomes.

## Later

- ECS gameplay on Netcode: interaction, creatures, settlements, as Burst systems.
- Map publishing: the baked `.stabbymap` stays in `out/` until builds are released.
- Map editing on top of `sketch.json`.

Not planned yet: a general job framework (Task.Run plus cancellation covers current needs).
