# Roadmap

Status words: done, now, next, later, idea. Finished rows move to ROADMAP-DONE.md.

## Audit 2 (baseline ae55841)

| Item | Status |
|---|---|
| Runtime check: the 4 Persistent allocations leaked at exit come from the Netcode package (GhostEntityMapping created by Netcode.Initialize via PrefabRegistryInitSystem), not Stabby; recheck on package updates | upstream |

## Now: ECS and unmanaged data

Gameplay scales on Burst and worker threads, so runtime data is unmanaged.
Managed code stays only at boundaries (generation, file load, network transfer)
and converts once into native data.

| Item | Status |
|---|---|
| Viewer scene stays a managed dev tool | idea |

## Next: unified world geometry

- Scale proofs before more content (external audit 11-22, shared by Zach): each new system
  gets a measured envelope. (1) Creatures: bench 10/50/100/250/500 visible deer (anim CPU,
  entities and memory per creature, temp allocations, cost below the animation threshold);
  hoist the five per-creature temp arrays in CreatureAnimSystem, skip pose/IK solving once
  animation has faded, queue CreatureBuildSystem arrivals nearest-first under a per-frame
  budget with batched instantiation, split herd minds from a parallel Burst behave job,
  with Punchy's brain LOD (far creatures think every N frames on staggered phases, debt
  caught up), staggered searches and a per-frame neighbour grid, all species in one
  data-driven job; compare the per-part entity render path against Punchy's pattern (pose
  job into one buffer, one instanced draw per shape, no part entities) behind a flag, at
  10/100/500, worst arrival frame included; switching paths is Zach's call. The arrival
  budget becomes the shared spawn budget objects use too (b/creatures-scale merged: arrival queue on a shared FrameBudget, one parallel Burst brain, opt-in instanced path STABBY_CREATURE_PATH=instanced at ~2 entities per creature, N500 worst arrival frame 158 -> ~9 ms; instanced stays opt-in until Zach playtests;
  b/creatures-5 done; Zach 11-22: diverse wildlife must run nearly as cheaply as one herd).
  Then far herds (b/creatures-far): herds beyond the spawn reach drawn from plan data and
  the shared clock with no entities or ghosts, waking into live creatures through the
  crossfade (Punchy only does this for sky birds; Stabby's own design). b/creatures-far merged: plan-only herds wake 0.03 m from plan, 7k/35k planned creatures run with 0 frames over 33 ms, worst client build 1.7 ms. b/creature-footing merged (GV 78): per-species maxSlope in content steers placement and the brain, feet sample ground under fore and hind legs (worst hoof gap 0 cm), host +0.05 ms. Next: b/creatures-species (hare, boar/elk as data, mixed bench). (2) Full-world stress done (b/stress-full, measure only): density 1 already places 1.79M
  objects; ~30k resident entities, 1080p full 8.6/10.0 ms median/p99 standing, 12.2/14.1 walking,
  2160p full 18.0/19.5 ms; 2.5 GB peak. No hitch worth fixing. (3) Streaming spikes checked (b/stream-spikes, measure only, fastwalk capture phase 290): terrain and object streaming at 30 m/s stay under 33 ms at FOV 60 and 15; the 740 ms frames were screenshots taken inside a measured loop. For a later 16 ms target, start with the per-node StitchMask and the unbudgeted ObjectView despawn.
  (3) Loading: ~450 MB allocated after terrain build, mid-load heap growth, a 76 ms collect.
  (4) Light budgets below come before fires and torches. Towns wait until the site
  hierarchy's streaming and collision are measured.
- Light budgets (from Punchy `LightPolicy`, `LightBudget`, `FarLightPolicy`): caps on real and
  shadowed lights, then glow, far emissive and emissive-only fallbacks by screen
  size and importance. Before fires, torches or night effects.
- Water interaction: local wakes and bow waves around moving entities (Punchy had them).
- Weather, precipitation and volumetric clouds.

Overhangs, layers, vertical faces, caves and arches share one geometry model
with the heightfield: the same material library and one global vertex lattice,
so feature meshes meet ground meshes without seams. Features differ only in how
their geometry is built.

| Item | Status |
|---|---|
| Per-cell refinement: subdivide only lattice cells that touch a feature edit, stitched to the coarse neighbours, instead of whole chunks at a finer step | idea |

## Next: generator features

Steps marked **Zach** need his decision first: they add to or change the
architecture contract. The other steps fit the contract as it stands.

| Item | Status |
|---|---|
| Cross-runtime erosion drift: Unity (Mono) and .NET generate seed 3 up to ~60 m apart before strata, ~180 m with them. The host map is authoritative and chunk sync corrects clients, so bake shipped maps on one runtime | later |
| **Zach:** cave expansion beyond the current fundamentals (cave networks, a dungeon scale); after surface look and objects | later |

## Next: surface look and objects (before bigger caves)

Art direction (Zach): stylised natural realism. Real-world colour palettes (no saturated cartoon hues), shader surfaces that mimic real materials, and enough noise and imperfection to feel organic. Fidelity can stay modest; palette and material believability come first.

One visual identity (Zach): every shader follows the same look budget, with shared soft targets on noise contrast, detail scale, saturation and specular range, so terrain, water, objects and grass read as one pipeline. Ground colours are hue-locked and narrow; sand is flat and grainy; wet bands fade gradually.

| Item | Status |
|---|---|
| World objects contract (approved by Zach; rule 11 text awaiting his review): one object definition in `content/objects.json`; four kinds (static with collision, interactive and removable, animated, structures of pieces with one LOD) set LOD, shadow, wind and animation policy; per-part traits override; placement is an engine-free stage after biomes; objects are built in code | next |
| Structure kit (rule 11): standalone elements such as awnings, tables, crates and posts are objects of their own that stalls and camps reuse; buildings assemble reusable modules (walls, corners, floors, roofs, door and window frames) that join seamlessly and merge to one LOD element at distance; the dry-stone wall and cairn become kit pieces; blueprints nest (kit piece -> building blueprint -> site blueprint with surrounding decorative and functional elements -> town placing sites), each level a content definition expanded from its own stream Prove it on one complete inn first (modules, doors and windows, furniture, yard, merged far mesh, collision), then one site placing it beside other elements; towns only after that. Inn merged (b/inn, GV 57): shell from kit modules, site blueprint with yard, stall, well and fences, natural objects cleared around sites and walls (CheckSites), straw thatch with ragged hem and muted dyed stall cloth. b/thatch-mass reverted (Zach): the rework made the roof badly aliased and the courses blended into one mass; the original roof only needed its one unfrilled seam fixed. b/thatch-seam merged: the hem frill fades by its own scale, so slanted courses keep a ragged edge. Zach: the second-to-last course still has a straight edge, likely a separate plane mapped differently; fixing that row only (b/thatch-row2); towns | in progress (thatch eave course frilled at the drip edge: b/thatch-row2 done; Zach approved the thatch roofs; Zach: the step from parallax wall courses to real stones is too contrasty, so wall heads (stone columns, or solid borders such as render, concrete-like caps or dressed stone edging, whichever hides the seam best) go at run ends and joints to hide the seams, with an A/B bench of parallax against geometry walls (b/wall-slope)) |
| Landforms (b/landforms-2, paused, single lane): mesas and buttes move into the heightfield (caprock, cap cliff, talus apron, 150-600 m lobed mesas, library strata), with solid-field edits only for the overhanging lip. Zach: basalt columns and crystals are objects with their own mesh in the basalt (or crystal) material, not terrain edits. They are instanced hex-prism clusters from objects.json, with contact skirts per rule 11. This should also remove the 65 ms 2160p basalt cost | landforms-2 merged (GV 85: heightfield mesas/buttes, lip edits, basalt and crystal objects); mesas rare at 2048 (seed 4 has a pair, seed 3 none); basalt/crystal shot and bench pending |
| **Zach 11-22:** one liquid system. Every liquid uses the shared water path (surface, geometry, shader, hydrology bodies), parameterized per liquid from a library in `content/`: colour and depth tint, opacity, viscosity (wave speed and amplitude, flow), surface crust or bubbles, emission. Liquids: water, mud (fill mud pots with an opaque mud layer), peat bog pools in wetlands, quicksand patches, lava (with volcano landforms). Falls and flows share it too: the waterfall path (fall geometry, FallMist spray) reads the same liquid entry, so lava flows and falls (slow, crusted, emissive, heat shimmer instead of mist), sandfalls (granular sheet, dust instead of spray) and mud seeps (thick, slow, short runs) are parameters, not new systems. Zach 11-22: realistic liquids first; fantasy fluids wait, but the library stays open for them. Gameplay effects (sinking, slowing, burning) wait for the movement work. Zach 11-22: water colour may vary by biome (optional per-biome water entry in the liquid library, e.g. glacial milky, tannin bog-brown, clear tropical); rivers crossing biomes blend gradually, not at the border. Plan: tint mixed downstream along the one drainage graph, flow-weighted, so tributaries mix where they join; lakes take their basin's mix; carried to the water shader per vertex. Card b/liquids-3 after liquids-2. Card b/liquids-1: liquid library + mud pots filled + bog pools; lava and quicksand follow. Zach 11-22: all water is real water, no stand-ins or simplifications. Spring, geyser, mud pot and bog pools are still flat discs that only share the water shader and liquid library; card b/pools-real-water rebuilds them as small water bodies on the shared surface path (carved basin, shore profile, level from the basin, same geometry as lakes), and audits for any other stand-in. | liquids-1 merged (GV 84: library, mud pots, peat bogs); liquids-2 merged (GV 86: narrow damp peat margins, objects keep out of bogs, falls and spray per liquid); liquids-3 merged (GV 87: biome water tint mixed downstream along the drainage graph, lakes take their outlet mix). Zach 11-22: square edging where the river meets the lake in the river shot. Cause: lake water is one flat quad per grid cell with the whole lake's outlet tint, so the river ribbon's mix stops at cell edges. b/lake-inflow-blend merged (GV 91): shared-vertex lake surface, discharge-weighted inflow tint blend, and a hydrology LakeCover field that runs the lake surface up carved inflow channels below lake level so the edge follows the shore (check CheckLakeCover). b/pools-real-water merged (GV 92): spring, geyser, mud pot and bog pools are real water bodies (level from the basin's spill height, flood-filled wet area, shared lake sheet builder; check CheckPools); bogs reject basins that can't hold water; spring tint toned down (Zach 11-23). Audit: no other stand-ins. Still open: mud pot and geyser pool close-ups need tighter framing; the river capture pose should pick a reach with real discharge |

## Next

- Real streaming when gameplay needs a bigger world: residency around players,
  eviction, priority, memory budget. The server then packs chunks on demand
  instead of keeping every packed chunk (about 2 MB at 512, fine for now).
- CI (11-22, done): runs hit the 20-min timeout, then Actions stopped on billing.
  Repo is public now (free minutes). Two parallel jobs on push/PR, 25 min each:
  lint + filtered tests, and a 30x256 sweep (runner is ~4x slower than local).
  The local full suite at merge stays the real gate.
- Unity batch CI job (game capture on a runner) if a license setup allows it.
- Generator features resume after the audit: caves, cliffs and river overhaul
  carried over from Punchy, more biomes.

## Gameplay (Zach's three pillars; Punchy's gameplay is the reference)

- Exploration: a world dense with detail, secrets and one-off discoveries:
  landmarks, rare creatures, giant creatures, mysterious structures, scenic
  vistas. Built on the object, structure kit and blueprint stages (rule 11),
  each discovery type placed from its own stream. Step 1 (b/discoveries-1) done: discovery placement stage (own stream, 900 m cells, 400 m from towns, dry ground, hilltop climb; GV 60) and a weathered standing-stone ring site; 8 rings on the 2048 world. b/discoveries-2 merged (GV 68): campsite site (tent, fire pit with ash bed, log seats) from the shared kit. b/discoveries-3 merged (GV 70): hermitage site (hut blueprint + yard). b/discoveries-4 merged (GV 72): road shrine (niche or open variants, wayside seats on their own footprint, stepping-stone path); niche variant still needs a shot. b/discoveries-5 merged (GV 74): niche shot taken, wayside stones now grey, ruin site from cut coursed stone (broken walls, doorway, rubble, moss tone) on steady ground. b/discoveries-6 merged (GV 81): cut-stone kit blocks get chipped, rounded edges and dents; fairy ring site (mushroom kit object, oversized toadstool cluster, stepping stones to nowhere) on its own stream. b/contact-audit merged (GV 82): 30% buried skirts on every structure ground piece, a contact stamp per part (check 10), all object colours in materials.json, a lush fairy-ring band via ground cover, and draped stepping stones. Follow-up: Object.shader built-in colours (thatch, plaster, cloth, iron, fungus) move to the library. b/vents-1 merged (GV 79): hot-spring pools on the shared water path (warm tint from the library sinter entry), basins and stepped rims carved as solid-field edits with sinter-coloured terrain, steam kept (Zach 11-22). b/vents-2 (GV 83): geysers and mud pots, mist cost cut. Zach 11-22: geysers want more precision and cleaner shapes; queued b/vents-3: denser cone mesh with smooth normals, relief moved to the shader, puff halo removed, mud pot rim sampled finer through the near-ring feature path if 1 m shows. Zach 11-22: landmarks need real biome restrictions. Placement already enforces each object's biome list, but shrine, ruin, hot spring, geyser and mud pot list every biome; b/landmark-biomes merged (GV 88): geysers and mud pots in steppe, shrubland, boreal, tundra, alpine and cold desert, hot springs everywhere except wetland and desert, ruins and shrines kept broad until per-biome variants exist, and a check that every discovery stands in one of its listed biomes. Pooled vents were already rare (seeds 1-6 at 2048: 4 geysers, 2 mud pots, 3 hot springs after); more need a denser spring stage, not wider lists. b/vents-3 merged (GV 89): geyser cones mesh smoothly at 0.5 m through a general `FeatureEdit.Fine` opt-in (Zach 11-22: special features earn finer vertices, rule 2), sinter terraces in the shader, puff halo removed; the floating pool disc is left for b/pools-real-water. b/object-colours merged: structure kit colours come from materials.json through a kit map (36 new entries), and a lint rule fails colour constants in Object.shader Built(); colours outside Built() (bark, moss, crust on non-kit parts) are a possible follow-up. b/discoveries-7 (wolf den: rock pile with a cleft under a leaning slab, worn apron, bones) merging after a GV re-bump. Follow-up: shared Boulder edits mesh with hard flat facets; rock shading needs its own card. Next site types from category (a). Landforms step 1 merged (b/landforms-1, GV 73): Dartmoor-style tors (rounded, tapered, offset slabs with domed tops) and Bryce-style hoodoos (flared foot, bulge-and-pinch bands, lopsided boulder caps) as solid-field edits with organic noise. Zach 11-22: hoodoo colour is fine; landforms take the local rock, as real hoodoos are orange only in orange canyons. b/landforms-2 merged (GV 85): heightfield mesas and buttes, basalt and crystal objects. Zach: draw site ideas from Punchy's landmark list (Assets/Game/Landmarks: hermit huts, campsites, ruins, sunken temple, salt tower, ash ribcage, geysers, hot springs, fairy ring, wolf den, road shrine, lone inn and more), rebuilt on the object kit. Zach: recategorise them and deploy through the town blueprint hierarchy. Planned split: (a) sites = blueprints of kit pieces and lower blueprints (campsite = tent + fire pit + seats; hermit hut = hut blueprint + yard; lone inn, shrine, stone ring, camps); (b) landforms = solid-field or terrain edits, not objects (butte, volcano cone, hoodoos, tors, basalt, cave mouths); (c) water/vent features = hydrology plus effects (geyser, hot springs, mud pits, oasis); (d) Punchy entries that are really materials or ground cover (cobbles, flagstones, trodden earth, rock types) are dropped. Racial camps and villages fold into the town tiers. | now
- Character interactivity: NPCs, animals and creatures act on their own
  desires and interact with each other and the player. One shared desire and
  behaviour system serves every agent; NPCs add ownership of items and
  structures, households and family ties, and dialogue about themselves,
  their background and culture. Theft and trespass follow from ownership.
  Burst ECS systems, host-authoritative. Step 1 (b/creatures-desires): animal drives (hunger, thirst, rest) pick graze, drink and bed-down targets inside the shared brain job. | now
- Creatures, NPCs and items follow Punchy closely (Punchy `Assets/Game/Scripts/Fauna`,
  `Assets/Game/Fauna/Limbs`, `Scripts/Items`): procedural bodies from simple
  hierarchical parts (arm, leg, tail, head types) that share definitions and
  animations and vary per entity; animation from a few keyframes plus inverse
  kinematics; all rendering through the shared material library. Part
  definitions live in `content/`, ported to ECS and Burst. Step 1 (b/creatures-1): part and body definitions in content, engine-free assembly, a code-built deer with idle and walk from keyframes plus leg IK. Step 1 done (b/creatures-1): deer wander with idle and stride, Burst animation, screen-size fade; b/creatures-2 done: shoulder hump, waist and haunch shaping with coat normal noise, and a low geometry tier by screen size through the shared crossfade. Zach: creatures are host-owned and synced; b/creatures-3 makes them Netcode for Entities ghosts, with spawn, wander and animation state decided on the host and clients only interpolating and animating. b/creatures-3 merged: host plans, spawns and wanders ghosts per-connection relevancy, clients agree within quantization; b/creatures-4 merged: herds go calm, alert, flee by reach-pixel thresholds with hysteresis, host-decided, client gap under 0.5 m while fleeing; b/creatures-5 merged: fleeing herds steer along shores and away from the map edge, alert when cornered; b/creatures-scale next. b/creature-footing merged (GV 78). b/creatures-species merged (GV 80): hare (darts, flees early) and boar as data only, coats named from new library materials, per-species biome and slope checks; mixed bench within about 10% of deer-only, client build cost lower. Follow-up: the boar reads as a tapir (tube body, long snout); b/creatures-boar merged: high humped shoulders, wedge head with tusks, dark spine crest, shorter legs; new rigid part role (no sway) and dusky-fur library entry. Next: b/creatures-desires, step 1 of the shared desire system for animals (graze, drink, rest drives as species data in the one brain job). | now
- Player control (Zach 11-22, for playtest builds; b/controls-1 merged, gamepad and mouse lock untested on hardware; boom ignores objects and overhangs):
  mouse or right stick orbits the view and movement follows the camera;
  L3 toggles sprint, which ends on a second press or when movement stops;
  triangle or space jumps. The camera stops short of terrain between it and
  the player instead of clipping through mountains. Gamepad support likely via the Input System
  package; sprint and jump travel in PlayerInput and run on the host.
- Playtest bugs (Zach 11-22; b/terrain-fix-1 merged: casters swept toward the sun, split nodes keep a fallback mesh so merges never collapse to the root (the blink), skirt seam lift, one-sided skirts; follow-up: raise tier 4-6 terrain mesh pools (156 misses), bench the larger caster set): hill shadows shift
  as the player approaches (shadow distance is 60 m with one cascade, terrain
  casters exist only inside that range and geomorph with camera distance); the
  island blinks out for a frame while moving, leaving objects behind (suspect
  terrain LOD swaps or culling bounds on pooled chunk entities; capture first); a strongly
  stretched layer at LOD seams (stitch or skirt geometry). Skirts are drawn
  double-sided (about 12% extra terrain triangles); make them one-sided, outward.
- Rock beds (Zach 11-22; b/rockbeds-1 merged: beds 0.035 m deep, varied thickness, broken runs; if raking still shows on sharp scarps, bed depth 0): cliff walls show
  evenly spaced, straight bed grooves (bedSpacingM 0.8, bedDepthM 0.1, TmRockRelief)
  that read as raked. Make them much shallower and irregular: varied bed
  thickness, broken and fading runs, no continuous stripes; remove if that
  still reads as stripes.
- Combat: not defined yet, waits until the other two take shape. | later

## Later

- Map publishing: the baked `.stabbymap` stays in `out/` until builds are released.
- Map editing on top of `sketch.json`.
- Material viewer (Zach 11-22, long-term): every library material on test shapes
  with an orbit camera and a name list, so Zach can audit surfaces and point at
  entries by name.
- Dungeons about 4x the size of caves, built on the same carve edits (idea).
- Cave system expansion well beyond the current fundamentals (idea).

Not planned yet: a general job framework (Task.Run plus cancellation covers current needs).
