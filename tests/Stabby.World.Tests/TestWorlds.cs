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
    static readonly ConcurrentDictionary<(ulong, int), Lazy<Placed>> Full = new();
    public const ulong DefaultSeed = 6;
    public const int DefaultSize = 256;

    public sealed class Placed
    {
        public required GeneratedWorld W;
        public required TerrainDetail Detail;
        public required FeatureEdit[] Edits;
        public required CaveGraph[] Graphs;
        public required ObjectInstance[] Sites;
        public required RoadSegment[] Roads;
        public required ObjectField Field;
        public ObjectField? Pass;
    }

    public static GeneratedWorld Fresh(ulong seed, int size) =>
        WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = size, CellMeters = WorldConfig.CellMetersFor(size) }, Biomes, Materials, null, null, Objects, Caves);

    public static GeneratedWorld World(ulong seed, int size = 256) =>
        Worlds.GetOrAdd((seed, size), k => new Lazy<GeneratedWorld>(() => Fresh(k.Item1, k.Item2))).Value;

    public static Placed Default => Place(DefaultSeed, DefaultSize);

    public static Placed Place(ulong seed, int size = 256) =>
        Full.GetOrAdd((seed, size), k => new Lazy<Placed>(() =>
        {
            var w = World(k.Item1, k.Item2);
            var detail = new TerrainDetail(w);
            var edits = Features.Place(w, detail, out var graphs, out var sites, out var roads, out var pass);
            return new Placed { W = w, Detail = detail, Edits = edits, Graphs = graphs, Sites = sites, Roads = roads, Field = new ObjectField(w, detail, edits, Objects), Pass = pass };
        })).Value;

    public static (GeneratedWorld, TerrainDetail, FeatureEdit[], ObjectField) Get(ulong seed, int size = 256)
    {
        var p = Place(seed, size);
        return (p.W, p.Detail, p.Edits, p.Field);
    }
}

public sealed class DefaultWorldFixture
{
    public DefaultWorldFixture() { _ = TestWorlds.Default; }
}
