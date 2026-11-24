using System.Diagnostics;
using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class ObjectRegionTests
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(Root);
    static readonly ObjectLibrary Objects = ContentLoader.LoadObjects(Root, Biomes.Select(b => b.Id).ToList());
    static readonly CaveLibrary Caves = ContentLoader.LoadCaves(ContentLoader.FindContentRoot(AppContext.BaseDirectory));

    [Fact]
    public void RegionsHoldTheirChunkColumnsAndCollideOnlyWhileResident()
    {
        var w = WorldGenerator.Generate(new WorldConfig { Seed = 4, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) }, Biomes, ContentLoader.LoadMaterials(Root), null, null, Objects, Caves);
        var detail = new TerrainDetail(w);
        var field = new ObjectField(w, detail, Features.Place(w, detail), Objects);
        int across = field.ChunksAcross;
        var regions = ColumnRegions.For(field);
        ColumnRegion? best = null;
        var clock = Stopwatch.StartNew();
        for (int c = 0; c < across * across; c++)
        {
            var r = ColumnRegion.Build(field, c);
            Assert.All(r.Owners, id => Assert.Equal(c, ObjectIds.Chunk(id)));
            Assert.Equal(r.Count, r.Starts[r.Grid.Cells]);
            if (best == null || r.Count > best.Count) best = r;
        }
        Console.WriteLine($"object regions: {across * across} built in {clock.ElapsedMilliseconds} ms, largest {best!.Count} columns");
        Assert.True(best.Count > 0);

        int tall = Math.Max(0, Array.FindIndex(best.Items, c => c.Top - c.Bottom > Walk.StepUp));
        var col = best.Items[tall];
        ulong owner = best.Owners[tall];
        Assert.Equal(float.NegativeInfinity, Obstacles.Top(regions, col.X, col.Z, 0.05f, col.Bottom, col.Top));
        regions.Put(best);
        Assert.True(regions.Resident(best.Index));
        Assert.True(Obstacles.Top(regions, col.X, col.Z, 0.05f, col.Bottom, col.Top) >= col.Top);
        var g = best.Grid;
        float gx0 = g.X0 * ColumnRegion.CellM, gz0 = g.Z0 * ColumnRegion.CellM, gx1 = (g.X0 + g.W) * ColumnRegion.CellM, gz1 = (g.Z0 + g.H) * ColumnRegion.CellM;
        float off = g.Max + 3 * ColumnRegion.CellM;
        foreach (float ox in new[] { gx0 - off, gx0 + 1, gx1 - 1, gx1 + off })
            foreach (float oz in new[] { gz0 - off, gz0 + 1, gz1 - 1, gz1 + off })
                if (ox < gx0 || ox > gx1 || oz < gz0 || oz > gz1)
                    Assert.Equal(float.NegativeInfinity, Obstacles.Top(regions, ox, oz, 0.05f, float.NegativeInfinity, float.PositiveInfinity));
        int alive = best.Alive(owner);
        Assert.True(alive > 0);
        Assert.Equal(alive, regions.Kill(owner));
        Assert.Equal(0, regions.Kill(owner));
        regions.Free(best.Index);
        Assert.False(regions.Resident(best.Index));
        Assert.Equal(float.NegativeInfinity, Obstacles.Top(regions, col.X, col.Z, 0.05f, col.Bottom, col.Top));
    }

    [Fact]
    public void RegionReachComesFromScreenSizeAndFreesWithHysteresis()
    {
        float reach = ObjectRegions.Reach(Objects);
        Assert.True(reach > Walk.Reach && reach < 5000, $"reach {reach:F0} m");
        float pixels = Objects.Objects.Where(d => d.Parts.Any(p => Objects.PartSolid(d, p))).Min(d => Objects.KindOf(d).FadePixels);
        Assert.Equal(Detail.SimulationReach(Walk.Height, pixels), reach, 3);
        float free = ObjectRegions.FreeReach(reach);
        Assert.True(free > reach);

        const int across = 8;
        const float size = 720;
        float x = 3 * size + 10, z = 3 * size + 300;
        var near = Near(across, size, x, z, reach);
        Assert.Contains(3 * across + 3, near);
        for (int r = 0; r < across * across; r++)
            Assert.Equal(near.Contains(r), ObjectRegions.Distance(across, size, r, x, z) <= reach);
        float px = 2 * size - 0.5f * (reach + free), pz = 2 * size + 10;
        Assert.DoesNotContain(2 * across + 2, Near(across, size, px, pz, reach));
        Assert.Contains(2 * across + 2, Near(across, size, px, pz, free));
    }

    static List<int> Near(int across, float size, float x, float z, float reach)
    {
        var list = new List<int>();
        ObjectRegions.Near(across, size, x, z, reach, list);
        return list;
    }
}
