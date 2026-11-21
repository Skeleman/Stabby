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
| LOD geomorph: every vertex blends to its coarse vertex by camera distance and is fully coarse at the switch distance, so LODs neither pop nor step at borders; borders next to a finer chunk stay fine | done |
| Surface detail: world-space value noise per pixel (coat patches and grain, rock grain, strata band wobble) with its numbers in `materials.json` `surface`, faded by pixel footprint so it matches across LODs | done |
| Per-cell refinement: subdivide only lattice cells that touch a feature edit, stitched to the coarse neighbours, instead of whole chunks at a finer step | idea |
| Player collision against the solid field: engine-free `Walk.Step` (step-up, head and radius probes, constant-speed fall, wall slide) shared by server, client prediction and `WorldChecks.CheckCaveWalk`, which walks every cave path | done |

## Next: generator features

Steps marked **Zach** need his decision first: they add to or change the
architecture contract. The other steps fit the contract as it stands.

| Item | Status |
|---|---|
| More biomes (alpine, wetland, mangrove, cold desert): optional height, distance-to-water and distance-to-coast conditions per biome in `biomes.json`; distances derived inside `Biomes` from hydrology and sea | done |
| River overhaul inside `Drainage` and hydrology: meanders, wider lowland beds, carved banks; centreline per segment, no second river array | done |
| Cliffs (approved by Zach): hard strata resist erosion, which makes scarps. Generation reads rock hardness per stratum from the material library, so material changes bump `GeneratorVersion`. Caves share the same strata for wall faces and can later prefer softer layers | done |
| Undercut cliff faces: overhang-shaped carves along scarps at the softest band under a hard cap (`Features.Undercut`, `StrataField` shared with erosion) | done |
| Cross-runtime erosion drift: Unity (Mono) and .NET generate seed 3 up to ~60 m apart before strata, ~180 m with them. The host map is authoritative and chunk sync corrects clients, so bake shipped maps on one runtime | later |
| **Zach:** cave expansion beyond the current fundamentals (cave networks, a dungeon scale); after surface look and objects | later |

## Next: surface look and objects (before bigger caves)

| Item | Status |
|---|---|
| Fine near field (approved by Zach; rule 2 text awaiting his review): keep the stored grid, add one engine-free sample function (smooth base, river and shore profiles from hydrology centrelines, material-scaled detail) that meshes, collision and features all read; 1 m near, doubling per LOD ring, computed per chunk on demand; replaces the levee (`NearField.Sample`, 64-quad nodes over 9 levels with 2:1 balance; GeneratorVersion 35) | done |
| Erosion speed: coarse-to-fine erosion (512 grid for all but the last 8 steps) and parallel row loops; 2048 generates in ~5 s (erosion 3 s), 4096 in ~19 s but its checks crash in Features.Dry/Arch (arch endpoints unclamped) | done (e7d12e3) |
| Base grid to 2048 (11 m cells) once near field lands; 4096 after the Features clamp fix | next |
| Water look: one water shader for sea, lakes and rivers (depth tint, shoreline, waves, fresnel) | done (2227ad1) |
| Water pass 2 (Zach): square pattern along the sea edge; sea, lakes and rivers share one water definition, differing only in flow and swell (`materials.json` `water`, one shader keyed by vertex colour; soft gradient-noise foam, lake shore ring) | done |
| Water cost: fine wave octave skipped past its fade distance, foam noise skipped away from shores; GPU profile of water and terrain shaders at several resolutions and distances before more effects (water coast 0.9/1.0 ms at 1080p, 1.3/1.8 at 2160p) | done |
| Terrain surface (Zach: assets in code): shader-only materials per library entry, detail through procedural normals, cheap parallax where it pays; textures only for heightfield data and shared noise samples (`TerrainMaterials.hlsl`; detail branched on pixel footprint, +0.1 ms at 1080p mid view, up to +1 ms in a 4K rock close-up) | done |
| Terrain surface pass 2: grass less saturated with crisp near grain instead of smeared bumps, rock sharper up close, dark shale band lighter, sand blotches, camera-relative coordinates for detail precision far from origin | done |
| Terrain surface pass 3: gradient noise instead of value noise (blocky midground grass), no swirls in near grass, sand ripple moire, grass blade pop at 1024 m tile lines, far-origin offset test | done |
| Close grass reads as saturated green fur: real blades now cover it within 45 m (objects step 2 grass). Faint sand crest dashes at 4-8 m remain | later |
| Flat untextured green wall in chase and overhead shots (mesh, not material; check after near field) | next |
| Chunk-sized holes at chunk edges: gone with the near-field lattice; thin green streaks were steep edge-row facets (edge vertices snapped half a cell), fixed by sampling the continuous surface on node edges | done |
| Captures pick a free UDP port (`STABBY_PORT`) so parallel lane captures never collide; overhead shot framed from 800 m | done |
| Far shoreline: sea and lake depth past 120-240 m read a smooth bed-height field (`_StabbyBed`, 11 m texels, mipped) instead of far terrain facets; +0.1 ms at 1080p, `coast-far` capture shot | done |
| Stepped sand rim at ~1 km where coarse far-LOD terrain crosses the flat water plane (geometry, not foam): finer far LOD along shorelines | later |
| World objects contract (approved by Zach; rule 11 text awaiting his review): one object definition in `content/objects.json`; four kinds (static with collision, interactive and removable, animated, structures of pieces with one LOD) set LOD, shadow, wind and animation policy; per-part traits override; placement is an engine-free stage after biomes; objects are built in code | next |
| Objects step 1: static rocks and debris, collision shared by `Walk.Step` (`Objects`, `ObjectChecks`, instanced view with screen-size LOD) | done |
| Objects step 1b: rocks use the strata material library, denser and varied placement (clusters at scarp feet, scree on slopes, logs under forest), spawn data unloaded with chunks, instance counts in capture stats; GeneratorVersion 34 | done |
| Rock strata tint (rocks share the terrain's camera-relative origin and detail fade); object column build and chunk packing moved off the main thread (17 ms main-thread copy gone); 33 ms worst frame was noise | done |
| Rocks inside one grey strata band read plain grey up close (shared rock material, changes cliffs too) | later |
| Large boulders as solid-field edits (rule 11): draws at or above `editMinSizeM` (3.6 m) become two Add capsule lumps on their own streams, checked in WorldChecks (bounds, no water, mesher match, collision within a cell of the mesh); GeneratorVersion 36 | done |
| Boulder follow-ups: boulders vanish at coarse LOD rings (needs a screen-size fallback); per-chunk edit index for Walk and Features.Edit; boulder tops take the biome coat; mesh check covers only the first boulder | next |
| Near-field follow-ups: frustum-cull node selection (offscreen nodes split at 2x distance), teardown waits for jobs before freeing ground data, terrain status string cached; 294 ms spike not seen since | done |
| Near-field follow-ups 2: trim SurfaceNets filler vertices at the source; worst frame 75 ms with a 19.6 ms terrain LOD update; Presentation builds its frames string every frame | next |
| Objects step 2a: GPU grass blades near the camera (`GrassScatter.compute` scatters per near terrain mesh from the biome coat, one indirect draw, frustum cull, distance fade into the terrain colour); shared wind globals `_StabbyWind`/`_StabbyGust` from `objects.json` foliage; +0.24 ms at 1080p in the grass close-up | done |
| Objects step 2b: animated foliage (bushes, trees) on the shared wind | later |
| Grass follow-ups: no grass on submerged beds (coat test ignores water surface), blade shadows, profiler to confirm chase/far cost with blades on and off | later |
| Objects step 3: interactive objects with host-authoritative removal | later |
| Objects step 4: structures from hierarchical pieces, merged LOD | later |

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
