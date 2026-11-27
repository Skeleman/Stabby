using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class DuneTests
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(Root);
    static readonly MaterialLibrary Materials = ContentLoader.LoadMaterials(Root);
    static readonly CaveLibrary Caves = ContentLoader.LoadCaves(Root);

    [Fact]
    public void DesertGrowsDuneFieldsThatPassChecks()
    {
        var c = new WorldConfig { Seed = 11, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) };
        var s = WorldGenerator.Generate(c, Biomes, Materials).Sketch;
        s.Regions.Clear();
        s.Regions.Add(new RegionSketch { Biome = "desert", X = 88, Y = 191, Radius = 20 });
        s.Build(c);
        var w = WorldGenerator.Generate(c, Biomes, Materials, s, null, TestWorlds.Objects, Caves);
        var d = new TerrainDetail(w);
        Assert.True(d.HasDunes);
        float top = 0;
        for (int i = 0; i < c.Size * c.Size; i++)
            if ((d.DunePack(i) & 255) >= 128)
                for (int k = 0; k < 81; k++)
                    top = Math.Max(top, d.DuneAt((i % c.Size + k % 9 / 9f) * c.CellMeters, (i / c.Size + k / 9 / 9f) * c.CellMeters));
        Assert.InRange(top, 1f, Dunes.HeightMax);
        Assert.Empty(WorldChecks.Run(w));
    }
}
