# Stabby rules

1. `Stabby.World` never references UnityEngine, System.IO file APIs, Console or
   System.Text.Json. A test enforces it. It must compile in Unity unchanged
   (netstandard2.1, C# 9, no records, no `init`).
2. One representation per fact. Ownership flows heightfield -> drainage graph
   (`Drainage`: fill, receivers, stack) -> hydrology (water surface, lakes,
   rivers) -> biomes -> features. Each stage reads the one before it and never
   derives a second copy. World geometry is one solid field: the heightfield
   base plus an ordered list of sparse feature edits (overhangs, arches,
   caves) that add or carve solid only inside their bounds. An underground
   stage after hydrology turns cave graphs (chambers and tunnels) into
   sparse carve edits; underground water joins the one drainage graph
   through springs and sinks. One engine-free
   sample function reads that field at any point: the stored grid smoothly
   interpolated, plus river and shore profiles from hydrology and
   material-scaled detail. Meshes, collision and features all call it. Meshes
   sample on one global lattice, 1 m near the player and doubling per LOD
   ring, computed per chunk on demand; nothing finer than the stored grid is
   kept. Special features (geyser cones, vent rims, basins) earn finer
   vertices: near-ring nodes inside a feature edit that asks for it sample
   the same field at 0.5 m, stitched crack-free to their neighbours. Chunks
   no feature touches
   take the heightfield fast path, which must match the general mesher vertex
   for vertex. One material library serves every surface: rock strata by world
   height plus a regional offset, biome coats on upward faces.
3. Authored data lives in `content/` and is read only by `Stabby.Content`.
4. Every world invariant lives in `WorldChecks`. Tests and the CLI sweep both
   call it; add a check there before fixing the bug it catches.
5. Randomness only through `Rng.Stream(seed, "name")`, one named stream per
   purpose, so adding a stream never shifts another. No `System.Random`, no
   trig in generation paths; Pow, Exp and Log go through `FMath`. Same seed +
   generator version is bit-identical on the same runtime and CPU
   architecture; small float drift across platforms is allowed, and host
   chunk sync (`WorldChunks`) corrects it.
6. Changing generation output bumps `WorldConfig.GeneratorVersion`.
7. No code comments; reasons go in commit messages. `tools/Stabby.Lint` runs
   in the tests and also requires Unity Game systems to be Burst `ISystem`
   unless `lint-allow.txt` gives a one-line reason.
8. Files are LF (`.gitattributes`).
9. This file is the architecture contract. Foundational changes to it need
   Zach's review; a PreToolUse hook asks before any edit. Stage outputs stay
   separable: sketch, terrain + hydrology (`--rain`), then climate and biomes.
10. The main thread schedules jobs, owns engine resources and applies
    finished results within a per-frame budget; it does not transform data.
    Generation, sync, meshing and other data work run in jobs or tasks, and
    main-thread engine calls (mesh upload, GPU buffers, lights, input) take
    only data a job has already prepared. Work too small to schedule is
    batched into a job once it grows. `tools/Stabby.Lint` rejects blocking
    waits, unchecked `Task.Result`, job `Complete` calls and main-thread
    query loops in runtime Unity code unless `lint-allow.txt` gives a reason. Runtime game data is unmanaged
    (ECS components, blobs, native containers) so Burst and jobs can use it;
    managed code stays at load and network boundaries and converts once.
11. World objects have one definition in `content/objects.json`. Its kind
    (static, interactive, animated, structure) sets LOD, shadow, wind,
    animation and collision policy; parts may override traits. Placement is
    an engine-free stage after biomes with its own stream, stored per chunk.
    Large boulders are solid-field edits. Static collision is engine-free and
    shared with movement. Interactive objects keep stable ids and change only
    through host-authoritative deltas. Wind and animation run on the GPU and
    fade by screen size. Structures are built from a shared kit: standalone
    elements (an awning, a table) are objects of their own that any structure
    can reuse, and buildings join reusable structural modules (walls,
    corners, floors, roofs, frames) into one continuous-looking whole that
    merges into a single LOD element at distance. Blueprints nest: each
    places kit pieces or whole lower blueprints as single elements, so a
    building blueprint (an inn's shell) sits inside a site blueprint (its
    yard, stalls and signs), and a town places sites. Each level expands
    deterministically from its own stream. Dungeon and mine kits snap to
    one shared grid and place as nested blueprints whose volumes are carve
    edits. Assets are
    built in code. Material surfaces are shaders (procedural normals, cheap
    parallax) so files stay small, surfaces scale and materials share
    features. Every part names a material from the one library
    (`content/materials.json`). Use an existing material when it fits the
    role well. Otherwise add a new library entry that later objects can reuse,
    never an inline one-off colour. Textures hold only heightfield data, shared noise, and
    code-generated atlases for repeated, distinct patterns such as leaves and
    lettering. Where an object meets the ground, both the object and the
    ground darken toward one contact colour derived from the terrain colour.
    Each ground part extends a buried skirt below its base, darkened to that
    colour, so no placement looks loose. A check fails any ground part
    that leaves no contact stamp.
    Structures also darken downward faces, gaps between pieces and inner
    seams. Normal and relief terms always carry organic noise, never clean
    lines. Most surface detail comes from normals, not albedo. Every
    material's procedural detail uses one shared frequency and screen-size
    octave fade, so apparent density is uniform. Materials choose pattern and
    strength, not scale. Exempt: texture-backed patterns (lettering, symbols,
    leaves) and liquids.
12. Every detail threshold is a screen size (Punchy's DetailPolicy). One
    engine-free `Detail` gives `ScreenSize(sizeM, distanceM, ppm)`: an
    object's approximate size over its 3D distance from the camera, in
    reference pixels (1080 lines at the camera's FOV, so resolution never
    moves a boundary). Each kind's rule in `content/` holds separate
    thresholds per answer (geometry tier, fade, shadow, animation); tiers
    switch with hysteresis and blend through one shared crossfade. Graphics
    quality scales thresholds, never simulation; simulation ranges use a
    fixed reference FOV so every machine agrees. Grass reach, object
    spawn/stream ranges and terrain rings derive from it (`Reach` is the
    inverse); none is a fixed distance in metres.
13. Creatures scale as populations, not individuals. All species share one
    data-driven brain job (parallel Burst, a per-frame spatial grid,
    staggered think phases scaled by screen size) and one presentation path.
    Adding a species is data in `content/`, never a new system. Herds beyond
    spawn reach exist only as plan data drawn from the shared clock, and they
    wake through the crossfade. No creature work may spike a single frame:
    spawning and building run within the shared per-frame budget.
14. Checks scale with the project. Every test and sweep check group has a
    time budget in `tools/scripts/time-budget.txt`; new tests and checks fit
    it, or the change raises it with the reason in the commit message.
    Feature tests run on `TestWorlds.Default` unless they need another seed
    or size, and say why.
    Expensive invariants belong in sweep check groups, not per-seed tests.
    Going over budget warns and never blocks a merge; only a runaway (5x
    budget or 10 min) fails.
