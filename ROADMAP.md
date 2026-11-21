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

Art direction (Zach): stylised natural realism. Real-world colour palettes (no saturated cartoon hues), shader surfaces that mimic real materials, and enough noise and imperfection to feel organic. Fidelity can stay modest; palette and material believability come first.

One visual identity (Zach): every shader follows the same look budget, with shared soft targets on noise contrast, detail scale, saturation and specular range, so terrain, water, objects and grass read as one pipeline. Ground colours are hue-locked and narrow; sand is flat and grainy; wet bands fade gradually.

| Item | Status |
|---|---|
| Fine near field (approved by Zach; rule 2 text awaiting his review): keep the stored grid, add one engine-free sample function (smooth base, river and shore profiles from hydrology centrelines, material-scaled detail) that meshes, collision and features all read; 1 m near, doubling per LOD ring, computed per chunk on demand; replaces the levee (`NearField.Sample`, 64-quad nodes over 9 levels with 2:1 balance; GeneratorVersion 35) | done |
| Erosion speed: coarse-to-fine erosion (512 grid for all but the last 8 steps) and parallel row loops; 2048 generates in ~5 s (erosion 3 s), 4096 in ~19 s but its checks crash in Features.Dry/Arch (arch endpoints unclamped) | done (e7d12e3) |
| Base grid 2048 (11.25 m cells) opt-in, GeneratorVersion 38; Features scale drop thresholds and arch endpoints with cell size (4096 checks no longer crash); default stays 512 until 2048 loads in under 10 s (35.7 s now) | done (b/grid-2048) |
| 2048 load to under 10 s playable, no GC pause over 33 ms (Client2 sync, object columns, pre-terrain allocation), then default 2048 | in progress (b/grid-load) |
| Water look: one water shader for sea, lakes and rivers (depth tint, shoreline, waves, fresnel) | done (2227ad1) |
| Water pass 2 (Zach): square pattern along the sea edge; sea, lakes and rivers share one water definition, differing only in flow and swell (`materials.json` `water`, one shader keyed by vertex colour; soft gradient-noise foam, lake shore ring) | done |
| Water cost: fine wave octave skipped past its fade distance, foam noise skipped away from shores; GPU profile of water and terrain shaders at several resolutions and distances before more effects (water coast 0.9/1.0 ms at 1080p, 1.3/1.8 at 2160p) | done |
| Water realism: bed refraction through the opaque texture (URP copy on, 2x downsample), per-channel absorption along the view ray so shallows show the bed and depth shifts to the deep tint, sky gradient reflection (horizon brighter), sun glitter tight near and broad far, muted blue-green palette | done (b/grounding) |
| Strata realism: no ledge sawtooth (per-parting depth, joints faded where they cross partings at a shallow angle, so no triangle lines on cliffs), muted grey-brown/buff strata palette, shader-only irregular bed thickness and regional dip/warp, distance weathering towards a dusty mean; capture waits for object streaming before lake/river shots | done (b/strata) |
| Terrain surface (Zach: assets in code): shader-only materials per library entry, detail through procedural normals, cheap parallax where it pays; textures only for heightfield data and shared noise samples (`TerrainMaterials.hlsl`; detail branched on pixel footprint, +0.1 ms at 1080p mid view, up to +1 ms in a 4K rock close-up) | done |
| Terrain surface pass 2: grass less saturated with crisp near grain instead of smeared bumps, rock sharper up close, dark shale band lighter, sand blotches, camera-relative coordinates for detail precision far from origin | done |
| Terrain surface pass 3: gradient noise instead of value noise (blocky midground grass), no swirls in near grass, sand ripple moire, grass blade pop at 1024 m tile lines, far-origin offset test | done |
| Close grass reads as saturated green fur: real blades now cover it within 45 m (objects step 2 grass). Sand crest dashes replaced by a soft ~1 m cosine ripple | done |
| Flat untextured green wall in chase and overhead shots (mesh, not material): gone with the near-field lattice; chase and overhead clean in every capture since | done (7fa335e) |
| Chunk-sized holes at chunk edges: gone with the near-field lattice; thin green streaks were steep edge-row facets (edge vertices snapped half a cell), fixed by sampling the continuous surface on node edges | done |
| Captures pick a free UDP port (`STABBY_PORT`) so parallel lane captures never collide; overhead shot framed from 800 m | done |
| Far shoreline: sea and lake depth past 120-240 m read a smooth bed-height field (`_StabbyBed`, 11 m texels, mipped) instead of far terrain facets; +0.1 ms at 1080p, `coast-far` capture shot | done |
| Stepped sand rim at ~1 km along the coast: not LOD (4x finer shoreline LOD left it pixel-identical); it is the coastline shape of the 45 m world grid through height interpolation and the sea mask, so the fix belongs in generation or the sample function | next |
| Sawtooth and faint diagonal line on shallow water at node edges: sea-only terrain nodes were never drawn, so the translucent water showed nothing past them; every node is drawn now and the shader sand floor is gone | done (b/terrain-artifacts) |
| Deep-sea split cap: terrain nodes entirely deeper than the depth where water alpha peaks (max of clarity and shore depth from materials.json) stop splitting; far coast 911k -> 827k vertices, no visible change | done (b/terrain-artifacts) |
| World objects contract (approved by Zach; rule 11 text awaiting his review): one object definition in `content/objects.json`; four kinds (static with collision, interactive and removable, animated, structures of pieces with one LOD) set LOD, shadow, wind and animation policy; per-part traits override; placement is an engine-free stage after biomes; objects are built in code | next |
| Objects step 1: static rocks and debris, collision shared by `Walk.Step` (`Objects`, `ObjectChecks`, instanced view with screen-size LOD) | done |
| Objects step 1b: rocks use the strata material library, denser and varied placement (clusters at scarp feet, scree on slopes, logs under forest), spawn data unloaded with chunks, instance counts in capture stats; GeneratorVersion 34 | done |
| Rock strata tint (rocks share the terrain's camera-relative origin and detail fade); object column build and chunk packing moved off the main thread (17 ms main-thread copy gone); 33 ms worst frame was noise | done |
| Rocks inside one grey strata band read plain grey up close (shared rock material, changes cliffs too) | done |
| Large boulders as solid-field edits (rule 11): draws at or above `editMinSizeM` (3.6 m) become two Add capsule lumps on their own streams, checked in WorldChecks (bounds, no water, mesher match, collision within a cell of the mesh); GeneratorVersion 36 | done |
| Boulder follow-ups: coarse-LOD inflate (`FeatureEdit.AtStep`), per-cell edit index (`EditGrid`), 12-boulder mesh check; also fixed large boulders never reaching the game (Unity loader dropped `editMinSizeM`, map had no object library) | done |
| Screen-size detail (Zach, rule 12): port Punchy's DetailPolicy as engine-free `Detail` (ScreenSize in reference pixels, Reach, per-kind rules with tiers, hysteresis, crossfade, quality scale, fixed-FOV simulation reach); move grass lod1M/lod2M/nearM/reachM, object tile reach/despawn/unload and terrain ring reach onto screen-size thresholds in content; a lint or check so new metre thresholds can't creep back | done |
| River water tiling (Zach): float hash at world coordinates made wave and foam cells disagree at edges; integer hash, gentler rapids from content rapidsSlope, smoother ribbon slope | done |
| Boulder coat: edit solid above the heightfield takes the full biome coat, so big boulders read as grassy lumps; steep coated faces show flat untextured olive bands with jagged cutoffs. Fade coat by height above ground and soften the slope cutoff in the material library | done |
| Boulder rock: boulders read as layer-cake barrels (world-height strata bands across a 3 m rock, grey stratum ring at the crown); one stratum per boulder picked at its base, weathered noise, cracks and lichen instead of bands | done |
| Ground look batch (Zach): Linear colour space, gradual wet band, flat grainy sand with faint ripple, continuous foam rim, one muted grass colour and one noise scale shared by grass and sand | done |
| Look budget (Zach): one shared block of soft target values in content/materials.json (noise contrast, detail scale, saturation, specular/roughness) that every shader reads as defaults through one HLSL include; coats may still deviate. Judge by before/after shots of terrain, water, grass and objects; no hard limits or rule until Zach has seen results (b/punchy-ground). The `look` block sets coat grain, grain scale and albedo defaults; Look.hlsl gives a soft saturation knee (0.65 linear) and a matte sheen (roughness 0.85, specular 0.04) to terrain, water, grass and objects | done |
| Weathered rock palette (Zach): boulders and cliff strata from saturated orange to weathered grey-brown and buff stone, lichen variety, water streaks, dust in hollows, roughness variation (step 6 of b/punchy-ground). Boulders done: one stone path for object and terrain boulders, grey-brown base, grey and green lichen, dust, streaks; cliff strata muted to grey-brown and buff with 15 percent chroma kept on stone; roughness from the look block per stone hardness, moss, lichen and per coat (`roughnessVar`, coat `roughness`) | done |
| Wet band at lakes and rivers: the sea's wet band reads height above a water-level texture (sea 0, lake surfaces, river stages) built from hydrology in `TerrainLook.WaterLevel`, so lake rims and river banks darken and go glossy like the coast; boulders skip it | done |
| Punchy ground look (Zach): bring Punchy's terrain style into the one material library: three-octave soft micro-relief each fading by its own screen size, grain flecks on bare ground, ground under blades takes the grass colour, far grass carries the blades' gust bands, patchy wet shore band, object grounding (contact darkening plus a lit ground-colour bounce on downward object faces); Punchy is Zach's code, PunchyReferences NC-SA files ideas only. Puddles in hollows wait for weather | done (b/grounding) |
| Near-field follow-ups: frustum-cull node selection (offscreen nodes split at 2x distance), teardown waits for jobs before freeing ground data, terrain status string cached; 294 ms spike not seen since | done |
| Near-field follow-ups 2: terrain node entities pooled and frames string lazy (done); capture spill frames excluded (done, 10 frames); left: pre-fill the terrain pool; SurfaceNets filler trim dropped (index layout is the fast-path contract; Terrain.cs already compacts) | now |
| Objects step 2a: GPU grass blades near the camera (`GrassScatter.compute` scatters per near terrain mesh from the biome coat, one indirect draw, frustum cull, distance fade into the terrain colour); shared wind globals `_StabbyWind`/`_StabbyGust` from `objects.json` foliage; +0.24 ms at 1080p in the grass close-up | done |
| Objects step 2b: animated foliage (bushes, trees) on the shared wind. Zach: realistic, not Punchy's cartoon style; real leaf detail on cutout leaf cards (alpha-tested, alpha-to-coverage) on code-built branches, leaf atlas generated in code at load, runtime-baked billboard impostors far off; rule 11 now allows code-generated atlases for repeated patterns | done (impostors: 8-azimuth runtime bake, dithered crossfade from the impostorPixels screen size; foliage 0.99 ms vs 1.00 ms in the tree-filled bench, gains at distance) |
| Grass blanket (Zach): far denser blades so meadows read as a continuous carpet; cheaper blades with distance, blades out to chase-camera distance, budget about 1 ms at 1080p | done |
| Grass follow-ups: no grass on submerged beds (done), sun shadows at 60 m (1 cascade, 2048) received by terrain and blades, +0.2-0.4 ms; terrain boulders and cliffs do not cast yet, blades never cast | done |
| Grass realism (art direction): muted, slightly warm base green with narrow hue-locked per-clump shifts; 0.55 m clumps vary height, width and lean with blades splaying outward; dry tan tips and rare seed heads; density thins with the ground coats' noise (strata wobble, dirt patches, coat clumps); blades restored after terrain meshes lost their Raw buffer targets; cost within noise of before, about 10% fewer blades | done (b/grass-realism) |
| Terrain shadows: cliffs and terrain boulders cast sun shadows; VertexLit gains a ShadowCaster pass sharing the stitch morph (`TerrainStitch.hlsl`), only drawn terrain nodes within the shadow distance cast (6-8 nodes, 30-65k vertices), cost within bench noise (+-0.3 ms); no acne or peter-panning at 14 degree sun with the light's default bias. Field textures (slope, bed, biome) upload in 4 MB slices over frames: terrain worst update 93 -> 25 ms, load-frame Gfx.UploadTextureData spike gone. Sunk sea nodes (whole range below -Deep, opaque water) stay in the drawn set for stitching but skip drawing: 22-26 nodes, about 110k vertices hidden; no holes or seams in coast-high or coast-far; bench within noise at 1080p, far at 4K about 0.2 ms faster | done |
| Terrain mesh spread: node meshes written into `Mesh.MeshDataArray` in a Burst job and applied with `ApplyAndDisposeWritableMeshData` inside the per-frame budget (no main-thread SetVertexBufferData); cliff-band triangle lines and the close-rock parting sawtooth traced to strata bed relief and parallax in `TmRockRelief` (not shadow bias, resolution, joints or mesh; caster already uses the stitched normal), fix left to the material lane; `close-rock` shots with cast/nocast/detail-off/no-joints variants; grass blades checked in shadow (`grass-low` shot) | done (b/terrain-mesh-spread) |
| Terrain load split: the one-time terrain load runs one step per frame (look blob, strata bands/coats/water level, terrain and water material registration, natives, first field slice next frame); terrain evictions capped per frame inside the apply budget, deferred nodes hidden until evicted; lod worst-update line breaks down requests/draw/evict/shadow | done |
| Objects step 3: interactive objects (loose stone, brushwood) with stable chunk+index ids (GeneratorVersion 37); host-authoritative removal deltas per chunk, replicated to clients and late joiners, collision columns cleared in place; E to take/cut with a status line; `ObjectDeltaCheck` two-client check | done |
| Objects perf: the ~43 ms spike when objects first stream in was the editor compiling the `Stabby/Object` shader variant inside `new Material` and `RegisterMaterial` (46 + 58 ms cold); both now run one per frame during load, before terrain is built. Column buffers already reuse one scratch list per chunk | done |
| Play spikes: capsule materials and mesh now load one step per frame from frame 11 (was one 170 ms batch with RegisterMaterial on first spawn); what is left on the first-spawn frame is Entities Graphics init (70-85 ms cold, 22 ms warm), load phase only. NetworkReceive Malloc/Free not reproduced in 9 runs, load phase only. Worst frame after terrain built 16-22 ms on a quiet machine; runs alongside other editors show 30-80 ms descheduling stalls. `SpikeProbe` tags spikes after terrain built | done |
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
