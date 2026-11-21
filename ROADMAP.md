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

## Audit 2 (baseline ae55841)

| Item | Status |
|---|---|
| Background tasks in ServerMapSystem, ClientMapSystem and TerrainViewSystem own their native blobs: cancelled on system destroy, and a task finishing after destroy disposes its own result without blocking the main thread | done |
| Terrain chunk render bounds from the blob's height range over the chunk (plus detail and feature edit boxes), not the first coarse mesh; a check that every LOD fits | done |
| WorldFile.Read validates size, biome count, string lengths and file length before allocating; bad headers throw FormatException so LoadOrGenerate regenerates | done |
| Wireframe overhang capture (`GameCapture` writes overhang-wire.png) for mesh review | done |
| TerrainLodJob is scheduled, completed the next frame, and skipped while the camera is still; it reads an immutable per-chunk site array and writes its own wanted-LOD array | done |
| Runtime check: terrain LOD meshes two or more steps finer than wanted are unregistered and destroyed (were kept until exit) | done |
| Runtime check: the 4 Persistent allocations leaked at exit come from the Netcode package (GhostEntityMapping created by Netcode.Initialize via PrefabRegistryInitSystem), not Stabby; recheck on package updates | upstream |

## Now: ECS and unmanaged data

Gameplay scales on Burst and worker threads, so runtime data is unmanaged.
Managed code stays only at boundaries (generation, file load, network transfer)
and converts once into native data.

| Item | Status |
|---|---|
| Terrain heights + detail as a blob; PlayerMoveSystem and ServerPlayerSystem become Burst ISystem | done |
| Game terrain as chunk entities (Entities Graphics), meshed by Burst jobs with Unity.Mathematics; replaces WorldView in Game | done |
| Map sync streams from a blob and reassembles into native slots in Burst ISystems; managed only at load and unpack | done |
| Player capsules rendered by Entities Graphics; only the chase camera stays managed | done |
| Viewer scene stays a managed dev tool | idea |

## Next: unified world geometry

Overhangs, layers, vertical faces, caves and arches share one geometry model
with the heightfield: the same material library and one global vertex lattice,
so feature meshes meet ground meshes without seams. Features differ only in how
their geometry is built.

| Item | Status |
|---|---|
| Geometry contract (one solid field: heightfield base + sparse feature edits) in CLAUDE.md rule 2 | done |
| Step 1: material library (`content/materials.json`), strata by world height + regional offset, drawn per pixel by the terrain shader | done |
| Step 2: field sampler + surface-nets mesher on the global lattice, checked vertex for vertex against the heightfield path (`SurfaceNets`, `WorldChecks`) | done |
| Step 2b: game terrain meshes through the surface-nets fast path in a Burst job, chunk rims pinned to chunk edges with skirts between LODs | done |
| Step 3: overhangs and arches as feature edits on the solid field, meshed in game chunks (`Features`, `SurfaceNets.Edited`) | done |
| Step 3b: caves as carve chains with entrances on slopes, ending in a chamber (`Features.Cave`) | done |
| Cave and overhang interiors darken by depth below the column surface (vertex alpha scales light in `VertexLit`) | done |
| Sharp-feature vertex placement (QEF) in cells touching an edit box (`SurfaceNets.Sharpen`) | done |
| Chunks a feature edit touches mesh at a finer LOD0 (0.125 cells, 5.6 m), halving overhang lip bumps; other chunks start at 0.25. Skirts hang from the rim vertex nearest the ground, not the column bottom | done |
| Surfaces more than a few metres off the heightfield (arches, overhang roofs, cave walls) are rock, never biome coat; interior darkening softened | done |
| Lip dimples: QEF solve truncated to large eigenvalues, carve fillet widened to half the radius, and slope leveling applied to every lattice corner (it ran only inside edit boxes, so the box border on steep faces got a uniform row of shifted vertices) | done |
| LOD stitching: fine border vertices carry their coarse neighbour's vertex (`SurfaceNets.CoarseVertex`, checked bitwise in `WorldChecks`) and the shader collapses onto it when that side or corner is coarser; visible neighbours stay within one LOD | done |
| Cave rework: tunnels twice as wide, 1-3 main paths branching after the mouth on a gentle grade (no steps or pits, checked in `WorldChecks.CheckCavePaths`), steeper side chambers off the main path, rooms at path ends | done |
| LOD seam shading: stitched border vertices also take the coarse neighbour's normal, colour, darkening and rock mix (normal checked bitwise in `WorldChecks`), so lighting and texture match across LOD borders | done |
| Per-pixel terrain shading: every LOD reads one global slope texture (mipmapped) and biome texture, so normals, coat and slope rock match across LODs; the vertex rock lift applies only inside feature edit boxes | done |
| Per-cell refinement: subdivide only lattice cells that touch a feature edit, stitched to the coarse neighbours, instead of whole chunks at a finer step | idea |
| Player collision against the solid field: engine-free `Walk.Step` (step-up, head and radius probes, constant-speed fall, wall slide) shared by server, client prediction and `WorldChecks.CheckCaveWalk`, which walks every cave path | done |

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
- Dungeons about 4x the size of caves, built on the same carve edits (idea).
- Cave system expansion well beyond the current fundamentals (idea).

Not planned yet: a general job framework (Task.Run plus cancellation covers current needs).
