using Stabby.Content;
using Stabby.Game;
using Stabby.World;
using Xunit.Abstractions;

namespace Stabby.World.Tests;

public class ColumnBakeTests
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(Root);
    static readonly ObjectLibrary Objects = ContentLoader.LoadObjects(Root, Biomes.Select(b => b.Id).ToList());
    static readonly CaveLibrary Caves = ContentLoader.LoadCaves(Root);

    readonly ITestOutputHelper output;

    public ColumnBakeTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void PooledBakeMatchesRegionBuildWithLessGarbage()
    {
        const int seed = 4;
        var w = WorldGenerator.Generate(new WorldConfig { Seed = (ulong)seed, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) }, Biomes, ContentLoader.LoadMaterials(Root), null, null, Objects, Caves);
        var detail = new TerrainDetail(w);
        var field = new ObjectField(w, detail, Features.Place(w, detail), Objects);
        int chunks = field.ChunksAcross * field.ChunksAcross, filled = 0;
        var bake = ColumnBake.Local;
        for (int c = 0; c < chunks; c++)
        {
            ColumnRegion.Build(field, c);
            bake.Run(field, c);
        }
        long before = 0, after = 0;
        for (int c = 0; c < chunks; c++)
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            var r = ColumnRegion.Build(field, c);
            long a1 = GC.GetAllocatedBytesForCurrentThread();
            bake.Run(field, c);
            long a2 = GC.GetAllocatedBytesForCurrentThread();
            before += a1 - a0;
            after += a2 - a1;
            if (r.Count > 0) filled++;
            Assert.Equal(r.Count, bake.Count);
            Assert.Equal(r.Grid, bake.Grid);
            Assert.Equal(r.Starts.Length, bake.Cells + 1);
            Assert.Equal(r.Starts, bake.Starts.Take(bake.Cells + 1));
            Assert.Equal(r.Items, bake.Items.Take(bake.Count));
            Assert.Equal(r.Owners, bake.Owners.Take(bake.Count));
        }
        Assert.True(filled > 1);
        Assert.True(after * 100 < before);
        output.WriteLine($"seed {seed}: {chunks} chunks, {filled} with columns; bytes per bake {before / chunks} before, {after / chunks} after");
    }
}
