using Stabby.Content;
using Stabby.World;
using Xunit.Abstractions;

namespace Stabby.World.Tests;

public class DiscoveryTests
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(Root);
    static readonly ObjectLibrary Objects = ContentLoader.LoadObjects(Root, Biomes.Select(b => b.Id).ToList());
    readonly ITestOutputHelper output;

    public DiscoveryTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void StoneRingsAppearAndReachTheirChunks()
    {
        int d = Objects.Objects.FindIndex(o => o.Id == "stone-ring"), total = 0, worlds = 0;
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var w = WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) }, Biomes, ContentLoader.LoadMaterials(Root), null, null, Objects);
            var detail = new TerrainDetail(w);
            var field = new ObjectField(w, detail, Features.Place(w, detail), Objects);
            var rings = field.Discoveries();
            output.WriteLine($"seed {seed}: {rings.Length} rings " + string.Join(" ", rings.Select(r => $"{r.X:F0},{r.Z:F0}")));
            foreach (var r in rings)
            {
                Assert.Equal(d, r.Def);
                int cx = (int)(r.X / field.ChunkMeters), cz = (int)(r.Z / field.ChunkMeters);
                Assert.Contains(field.Chunk(cx, cz), o => o.Def == d && o.X == r.X && o.Z == r.Z);
            }
            total += rings.Length;
            if (rings.Length > 0) worlds++;
        }
        Assert.True(worlds >= 2, $"stone rings in only {worlds} of 4 worlds ({total} total)");
    }

    [Fact]
    public void LoadedWorldPlacesTheSameDiscoveries()
    {
        var materials = ContentLoader.LoadMaterials(Root);
        var w = WorldGenerator.Generate(new WorldConfig { Seed = 1, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) }, Biomes, materials, null, null, Objects);
        var loaded = WorldFile.Read(WorldFile.Write(w), materials);
        loaded.Objects = Objects;
        string Rings(GeneratedWorld g)
        {
            var detail = new TerrainDetail(g);
            return string.Join(" ", new ObjectField(g, detail, Features.Place(g, detail), Objects).Discoveries().Select(r => $"{r.X},{r.Z},{r.Size}"));
        }
        string expected = Rings(w);
        Assert.NotEmpty(expected);
        Assert.Equal(expected, Rings(loaded));
    }
}
