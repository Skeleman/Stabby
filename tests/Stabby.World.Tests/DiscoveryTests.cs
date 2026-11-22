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
            var rings = field.Discoveries().Where(o => o.Def == d).ToArray();
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
    public void CampsitesAppearByForestsAndRiversAwayFromRings()
    {
        int camp = Objects.Objects.FindIndex(o => o.Id == "campsite"), total = 0, worlds = 0;
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var w = WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) }, Biomes, ContentLoader.LoadMaterials(Root), null, null, Objects);
            var detail = new TerrainDetail(w);
            var field = new ObjectField(w, detail, Features.Place(w, detail), Objects);
            var all = field.Discoveries();
            var camps = all.Where(o => o.Def == camp).ToArray();
            output.WriteLine($"seed {seed}: {camps.Length} camps " + string.Join(" ", camps.Select(c => $"{c.X:F0},{c.Z:F0}:{field.CampDraw(c.X, c.Z):F2}")));
            foreach (var c in camps)
            {
                Assert.True(field.CampDraw(c.X, c.Z) > 0);
                foreach (var o in all.Where(o => o.Def != camp))
                    Assert.True(Math.Sqrt((o.X - c.X) * (o.X - c.X) + (o.Z - c.Z) * (o.Z - c.Z)) >= ObjectField.DiscoveryGapM);
                int cx = (int)(c.X / field.ChunkMeters), cz = (int)(c.Z / field.ChunkMeters);
                Assert.Contains(field.Chunk(cx, cz), o => o.Def == camp && o.X == c.X && o.Z == c.Z);
            }
            total += camps.Length;
            if (camps.Length > 0) worlds++;
        }
        Assert.True(worlds >= 3, $"campsites in only {worlds} of 4 worlds ({total} total)");
    }

    [Fact]
    public void HermitagesAppearDeepInTheWoods()
    {
        int hermit = Objects.Objects.FindIndex(o => o.Id == "hermitage"), total = 0, worlds = 0;
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var w = WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) }, Biomes, ContentLoader.LoadMaterials(Root), null, null, Objects);
            var detail = new TerrainDetail(w);
            var field = new ObjectField(w, detail, Features.Place(w, detail), Objects);
            var all = field.Discoveries();
            var huts = all.Where(o => o.Def == hermit).ToArray();
            output.WriteLine($"seed {seed}: {huts.Length} hermitages " + string.Join(" ", huts.Select(c => $"{c.X:F0},{c.Z:F0}:{field.Woods(c.X, c.Z):F2}")));
            foreach (var c in huts)
            {
                Assert.True(field.Woods(c.X, c.Z) >= ObjectField.HermitWoods);
                foreach (var o in all.Where(o => o.Def != hermit))
                    Assert.True(Math.Sqrt((o.X - c.X) * (o.X - c.X) + (o.Z - c.Z) * (o.Z - c.Z)) >= ObjectField.DiscoveryGapM);
                int cx = (int)(c.X / field.ChunkMeters), cz = (int)(c.Z / field.ChunkMeters);
                Assert.Contains(field.Chunk(cx, cz), o => o.Def == hermit && o.X == c.X && o.Z == c.Z);
            }
            total += huts.Length;
            if (huts.Length > 0) worlds++;
        }
        Assert.True(worlds >= 3, $"hermitages in only {worlds} of 4 worlds ({total} total)");
    }

    [Fact]
    public void ShrinesAppearBesideRoads()
    {
        int shrine = Objects.Objects.FindIndex(o => o.Id == "wayside-shrine"), total = 0, worlds = 0;
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var w = WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) }, Biomes, ContentLoader.LoadMaterials(Root), null, null, Objects);
            var detail = new TerrainDetail(w);
            var field = new ObjectField(w, detail, Features.Place(w, detail), Objects);
            var all = field.Discoveries();
            var shrines = all.Where(o => o.Def == shrine).ToArray();
            output.WriteLine($"seed {seed}: {field.Routes.Length / 4} routes, {shrines.Length} shrines " + string.Join(" ", shrines.Select(c => $"{c.X:F0},{c.Z:F0}:{field.RoadSide(c.X, c.Z, out _, out _):F1}")));
            foreach (var c in shrines)
            {
                float d = field.RoadSide(c.X, c.Z, out float rx, out float rz);
                Assert.InRange(d, ObjectField.ShrineRoadMinM, ObjectField.ShrineRoadM);
                float yx = 2 * c.Qw * c.Qy, yz = 1 - 2 * c.Qy * c.Qy;
                Assert.True(yx * (rx - c.X) + yz * (rz - c.Z) > 0.5f * d, $"shrine at {c.X:F0},{c.Z:F0} faces away from the road");
                foreach (var o in all.Where(o => o.Def != shrine))
                    Assert.True(Math.Sqrt((o.X - c.X) * (o.X - c.X) + (o.Z - c.Z) * (o.Z - c.Z)) >= ObjectField.DiscoveryGapM);
                int cx = (int)(c.X / field.ChunkMeters), cz = (int)(c.Z / field.ChunkMeters);
                Assert.Contains(field.Chunk(cx, cz), o => o.Def == shrine && o.X == c.X && o.Z == c.Z);
            }
            total += shrines.Length;
            if (shrines.Length > 0) worlds++;
        }
        Assert.True(worlds >= 2, $"shrines in only {worlds} of 4 worlds ({total} total)");
    }

    [Fact]
    public void RuinsAppearOnSteadyGround()
    {
        int ruin = Objects.Objects.FindIndex(o => o.Id == "ruin"), total = 0, worlds = 0;
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var w = WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) }, Biomes, ContentLoader.LoadMaterials(Root), null, null, Objects);
            var detail = new TerrainDetail(w);
            var field = new ObjectField(w, detail, Features.Place(w, detail), Objects);
            var all = field.Discoveries();
            var ruins = all.Where(o => o.Def == ruin).ToArray();
            output.WriteLine($"seed {seed}: {ruins.Length} ruins " + string.Join(" ", ruins.Select(c => $"{c.X:F0},{c.Z:F0}:v{c.Variant}")));
            foreach (var c in ruins)
            {
                Assert.True(field.Fitness(ObjectField.Ruin, c.X, c.Z) > float.MinValue);
                foreach (var o in all.Where(o => o.Def != ruin))
                    Assert.True(Math.Sqrt((o.X - c.X) * (o.X - c.X) + (o.Z - c.Z) * (o.Z - c.Z)) >= ObjectField.DiscoveryGapM);
                int cx = (int)(c.X / field.ChunkMeters), cz = (int)(c.Z / field.ChunkMeters);
                Assert.Contains(field.Chunk(cx, cz), o => o.Def == ruin && o.X == c.X && o.Z == c.Z);
            }
            total += ruins.Length;
            if (ruins.Length > 0) worlds++;
        }
        Assert.True(worlds >= 2, $"ruins in only {worlds} of 4 worlds ({total} total)");
    }

    [Fact]
    public void FairyRingsAppearOnLevelGround()
    {
        int ring = Objects.Objects.FindIndex(o => o.Id == "fairy-ring"), total = 0, worlds = 0;
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var w = WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) }, Biomes, ContentLoader.LoadMaterials(Root), null, null, Objects);
            var detail = new TerrainDetail(w);
            var field = new ObjectField(w, detail, Features.Place(w, detail), Objects);
            var all = field.Discoveries();
            var rings = all.Where(o => o.Def == ring).ToArray();
            output.WriteLine($"seed {seed}: {rings.Length} rings " + string.Join(" ", rings.Select(c => $"{c.X:F0},{c.Z:F0}:v{c.Variant}")));
            foreach (var c in rings)
            {
                Assert.True(field.Fitness(ObjectField.FairyRing, c.X, c.Z) > float.MinValue);
                foreach (var o in all.Where(o => o.Def != ring))
                    Assert.True(Math.Sqrt((o.X - c.X) * (o.X - c.X) + (o.Z - c.Z) * (o.Z - c.Z)) >= ObjectField.DiscoveryGapM);
                int cx = (int)(c.X / field.ChunkMeters), cz = (int)(c.Z / field.ChunkMeters);
                Assert.Contains(field.Chunk(cx, cz), o => o.Def == ring && o.X == c.X && o.Z == c.Z);
            }
            total += rings.Length;
            if (rings.Length > 0) worlds++;
        }
        Assert.True(worlds >= 2, $"rings in only {worlds} of 4 worlds ({total} total)");
    }

    [Fact]
    public void HotSpringsSitNearRelief()
    {
        int spring = Objects.Objects.FindIndex(o => o.Id == "hot-spring"), total = 0, worlds = 0;
        var pools = new System.Collections.Generic.List<float>();
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var w = WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) }, Biomes, ContentLoader.LoadMaterials(Root), null, null, Objects);
            var detail = new TerrainDetail(w);
            var field = new ObjectField(w, detail, Features.Place(w, detail), Objects);
            var springs = field.Discoveries().Where(o => o.Def == spring).ToArray();
            output.WriteLine($"seed {seed}: {springs.Length} springs " + string.Join(" ", springs.Select(c => $"{c.X:F0},{c.Z:F0}:{field.SpringDraw(c.X, c.Z):F2}")));
            foreach (var c in springs)
            {
                Assert.True(field.SpringDraw(c.X, c.Z) > 0);
                Assert.True(field.RiverGap(c.X, c.Z) >= ObjectField.SpringRiverM);
                Assert.Null(field.PoolFault(c));
                field.SpringPoolsAt(c, pools);
                Assert.InRange(pools.Count / ObjectField.SpringStride, 3, 4);
            }
            total += springs.Length;
            if (springs.Length > 0) worlds++;
        }
        Assert.True(worlds >= 2, $"hot springs in only {worlds} of 4 worlds ({total} total)");
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
