# Stabby rules

1. `Stabby.World` never references UnityEngine, System.IO file APIs, Console or
   System.Text.Json. A test enforces it. It must compile in Unity unchanged
   (netstandard2.1, C# 9, no records, no `init`).
2. One representation per fact. Ownership flows heightfield -> drainage graph
   (`Drainage`: fill, receivers, stack) -> hydrology (water surface, lakes,
   rivers) -> biomes -> features. Each stage reads the one before it and never
   derives a second copy. World geometry is one solid field: the heightfield
   base plus an ordered list of sparse feature edits (overhangs, arches,
   caves) that add or carve solid only inside their bounds. One engine-free
   sample function reads that field at any point: the stored grid smoothly
   interpolated, plus river and shore profiles from hydrology and
   material-scaled detail. Meshes, collision and features all call it. Meshes
   sample on one global lattice, 1 m near the player and doubling per LOD
   ring, computed per chunk on demand; nothing finer than the stored grid is
   kept. Chunks no feature touches
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
10. Unity code never blocks the main thread on heavy work. Generation, sync,
    meshing and other bulk work run in tasks or jobs; the main thread only
    applies finished results within a per-frame budget. `tools/Stabby.Lint`
    rejects blocking waits and unchecked `Task.Result` in runtime Unity code
    unless `lint-allow.txt` gives a reason. Runtime game data is unmanaged
    (ECS components, blobs, native containers) so Burst and jobs can use it;
    managed code stays at load and network boundaries and converts once.
11. World objects have one definition in `content/objects.json`. Its kind
    (static, interactive, animated, structure) sets LOD, shadow, wind,
    animation and collision policy; parts may override traits. Placement is
    an engine-free stage after biomes with its own stream, stored per chunk.
    Large boulders are solid-field edits. Static collision is engine-free and
    shared with movement. Interactive objects keep stable ids and change only
    through host-authoritative deltas. Wind and animation run on the GPU and
    fade by screen size; structures merge their pieces per LOD. Assets are
    built in code: surface detail is shader-based (procedural normals, cheap
    parallax), and textures hold only heightfield data and shared noise.
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
