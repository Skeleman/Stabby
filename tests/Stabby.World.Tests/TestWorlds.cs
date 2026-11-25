using System.Collections.Concurrent;
using Stabby.Content;

namespace Stabby.World.Tests;

static class TestWorlds
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(Root);
    static readonly MaterialLibrary Materials = ContentLoader.LoadMaterials(Root);
    public static readonly ObjectLibrary Objects = ContentLoader.LoadObjects(Root, Biomes.Select(b => b.Id).ToList());
    static readonly CaveLibrary Caves = ContentLoader.LoadCaves(Root);
    static readonly ConcurrentDictionary<(ulong, int), Lazy<GeneratedWorld>> Worlds = new();
    static readonly ConcurrentDictionary<(ulong, int), Lazy<(GeneratedWorld, TerrainDetail, FeatureEdit[], ObjectField)>> Placed = new();

    public static GeneratedWorld Fresh(ulong seed, int size) =>
        WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = size, CellMeters = WorldConfig.CellMetersFor(size) }, Biomes, Materials, null, null, Objects, Caves);

    public static GeneratedWorld World(ulong seed, int size = 256) =>
        Worlds.GetOrAdd((seed, size), k => new Lazy<GeneratedWorld>(() => Fresh(k.Item1, k.Item2))).Value;

    public static (GeneratedWorld, TerrainDetail, FeatureEdit[], ObjectField) Get(ulong seed, int size = 256) =>
        Placed.GetOrAdd((seed, size), k => new Lazy<(GeneratedWorld, TerrainDetail, FeatureEdit[], ObjectField)>(() =>
        {
            var w = World(k.Item1, k.Item2);
            var detail = new TerrainDetail(w);
            var edits = Features.Place(w, detail);
            return (w, detail, edits, new ObjectField(w, detail, edits, Objects));
        })).Value;
}
