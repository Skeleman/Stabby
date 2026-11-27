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

    static (GeneratedWorld, TerrainDetail, FeatureEdit[], ObjectField) World(ulong seed) => TestWorlds.Get(seed);

    [Fact]
    public void FeaturePassDiscoveriesMatchTheFinalEdits()
    {
        int seen = 0;
        var kinds = new HashSet<int>();
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var p = TestWorlds.Place(seed);
            var shared = p.Sites;
            var fresh = p.Field.Discoveries();
            Assert.NotNull(shared);
            Assert.Equal(fresh.Length, shared.Length);
            for (int i = 0; i < fresh.Length; i++)
            {
                Assert.Equal(fresh[i].Id, shared[i].Id);
                Assert.Equal(fresh[i].Def, shared[i].Def);
                Assert.Equal(fresh[i].Variant, shared[i].Variant);
                Assert.Equal((fresh[i].X, fresh[i].Y, fresh[i].Z, fresh[i].Size), (shared[i].X, shared[i].Y, shared[i].Z, shared[i].Size));
                Assert.Equal((fresh[i].Qx, fresh[i].Qy, fresh[i].Qz, fresh[i].Qw), (shared[i].Qx, shared[i].Qy, shared[i].Qz, shared[i].Qw));
            }
            seen += fresh.Length;
            kinds.UnionWith(fresh.Select(o => o.Def));
        }
        Assert.True(seen > 0);
        var missing = Enumerable.Range(0, Objects.Objects.Count).Where(d => Objects.Objects[d].Placement.Discovery > 0 && !kinds.Contains(d)).Select(d => Objects.Objects[d].Id).ToList();
        Assert.True(missing.Count == 0, $"seeds 1..4 place no {string.Join(", ", missing)}");
    }

    [Fact]
    public void StoneRingsAppearAndReachTheirChunks()
    {
        int d = Objects.Objects.FindIndex(o => o.Id == "stone-ring"), total = 0, worlds = 0;
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var (w, detail, _, field) = World(seed);
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
            var (w, detail, _, field) = World(seed);
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
            var (w, detail, _, field) = World(seed);
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
            var (w, detail, _, field) = World(seed);
            var all = field.Discoveries();
            var shrines = all.Where(o => o.Def == shrine).ToArray();
            output.WriteLine($"seed {seed}: {field.Roads().Length} road segments, {shrines.Length} shrines " + string.Join(" ", shrines.Select(c => $"{c.X:F0},{c.Z:F0}:{field.RoadSide(c.X, c.Z, out _, out _):F1}")));
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
            var (w, detail, _, field) = World(seed);
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
            var (w, detail, _, field) = World(seed);
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
    public void WolfDensAppearWithTheirRocks()
    {
        int den = Objects.Objects.FindIndex(o => o.Id == "wolf-den"), total = 0, worlds = 0;
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var (w, detail, edits, field) = World(seed);
            var dens = field.Discoveries().Where(o => o.Def == den).ToArray();
            output.WriteLine($"seed {seed}: {dens.Length} dens " + string.Join(" ", dens.Select(c => $"{c.X:F0},{c.Z:F0}:v{c.Variant}")));
            var rocks = new List<FeatureEdit>();
            foreach (var c in dens)
            {
                Assert.True(field.Fitness(ObjectField.WolfDen, c.X, c.Z) > float.MinValue);
                Assert.Null(field.DenFault(c));
                rocks.Clear();
                field.DenEditsOf(c, rocks);
                Assert.InRange(rocks.Count, 4, 5);
                foreach (var r in rocks) Assert.Contains(edits, f => f.Shape == FeatureEdit.Boulder && f.Ax == r.Ax && f.Az == r.Az && f.R == r.R);
            }
            total += dens.Length;
            if (dens.Length > 0) worlds++;
        }
        Assert.True(worlds >= 2, $"dens in only {worlds} of 4 worlds ({total} total)");
    }

    [Fact]
    public void OverlooksAppearOnOpenRims()
    {
        int look = Objects.Objects.FindIndex(o => o.Id == "overlook");
        var (_, _, _, field) = World(TestWorlds.DefaultSeed);
        var all = field.Discoveries();
        var looks = all.Where(o => o.Def == look).ToArray();
        output.WriteLine($"{looks.Length} overlooks " + string.Join(" ", looks.Select(c => $"{c.X:F0},{c.Z:F0}:v{c.Variant}")));
        Assert.NotEmpty(looks);
        foreach (var c in looks)
        {
            Assert.True(field.Fitness(ObjectField.Overlook, c.X, c.Z) > float.MinValue);
            Assert.Null(field.OverlookFault(c));
            Assert.False(field.OnRoad(c.X, c.Z, field.SiteRadius(c)));
            foreach (var o in all.Where(o => o.Def != look))
                Assert.True(Math.Sqrt((o.X - c.X) * (o.X - c.X) + (o.Z - c.Z) * (o.Z - c.Z)) >= ObjectField.DiscoveryGapM);
        }
    }

    [Fact]
    public void LoneInnsStandOnRoadShouldersFarFromTowns()
    {
        int inn = Objects.Objects.FindIndex(o => o.Id == "lone-inn");
        var (w, _, _, field) = World(TestWorlds.DefaultSeed);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var all = field.Discoveries();
        var inns = all.Where(o => o.Def == inn).ToArray();
        output.WriteLine($"budget: {inns.Length} lone inns, discoveries {sw.ElapsedMilliseconds} ms " + string.Join(" ", inns.Select(c => $"{c.X:F0},{c.Z:F0}:v{c.Variant}:road {field.RoadSide(c.X, c.Z, out _, out _):F1}")));
        Assert.NotEmpty(inns);
        foreach (var c in inns)
        {
            Assert.Null(field.LoneInnFault(c));
            Assert.False(field.OnRoad(c.X, c.Z, field.SiteRadius(c)));
            foreach (var t in field.Towns)
                Assert.True(Math.Sqrt((t.X * w.Config.CellMeters - c.X) * (t.X * w.Config.CellMeters - c.X) + (t.Y * w.Config.CellMeters - c.Z) * (t.Y * w.Config.CellMeters - c.Z)) >= ObjectField.TownClearM);
            foreach (var o in all.Where(o => o.Def != inn))
                Assert.True(Math.Sqrt((o.X - c.X) * (o.X - c.X) + (o.Z - c.Z) * (o.Z - c.Z)) >= ObjectField.DiscoveryGapM);
        }
    }

    [Fact]
    public void SaltTowersStandOnDryBasinFloors()
    {
        int tower = Objects.Objects.FindIndex(o => o.Id == "salt-tower");
        var (_, _, _, field) = World(TestWorlds.DefaultSeed);
        var all = field.Discoveries();
        var towers = all.Where(o => o.Def == tower).ToArray();
        output.WriteLine($"{towers.Length} salt towers " + string.Join(" ", towers.Select(c => $"{c.X:F0},{c.Z:F0}:v{c.Variant}")));
        Assert.NotEmpty(towers);
        foreach (var c in towers)
        {
            Assert.True(field.Fitness(ObjectField.SaltTower, c.X, c.Z) > float.MinValue);
            Assert.Null(field.SaltFault(c));
            Assert.False(field.OnRoad(c.X, c.Z, field.SiteRadius(c)));
            foreach (var o in all.Where(o => o.Def != tower))
                Assert.True(Math.Sqrt((o.X - c.X) * (o.X - c.X) + (o.Z - c.Z) * (o.Z - c.Z)) >= ObjectField.DiscoveryGapM);
        }
    }

    [Fact]
    public void BonesLieOnSteadyDryGround()
    {
        int bones = Objects.Objects.FindIndex(o => o.Id == "bones");
        var (_, _, _, field) = World(TestWorlds.DefaultSeed);
        var all = field.Discoveries();
        var found = all.Where(o => o.Def == bones).ToArray();
        output.WriteLine($"default: {found.Length} bones " + string.Join(" ", found.Select(c => $"{c.X:F0},{c.Z:F0}:v{c.Variant}")));
        Assert.True(found.Length > 0, "TestWorlds.Default placed no bones");
        foreach (var c in found)
        {
            Assert.True(field.Fitness(ObjectField.Bones, c.X, c.Z) > float.MinValue);
            Assert.Null(field.BonesFault(c));
            Assert.False(field.OnRoad(c.X, c.Z, field.SiteRadius(c)));
            foreach (var o in all.Where(o => o.Def != bones))
                Assert.True(Math.Sqrt((o.X - c.X) * (o.X - c.X) + (o.Z - c.Z) * (o.Z - c.Z)) >= ObjectField.DiscoveryGapM);
        }
    }

    [Fact]
    public void HotSpringsSitInHotspots()
    {
        int spring = Objects.Objects.FindIndex(o => o.Id == "hot-spring"), total = 0, worlds = 0;
        var pools = new System.Collections.Generic.List<float>();
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var (w, detail, _, field) = World(seed);
            var springs = field.Discoveries().Where(o => o.Def == spring).ToArray();
            output.WriteLine($"seed {seed}: {springs.Length} springs " + string.Join(" ", springs.Select(c => $"{c.X:F0},{c.Z:F0}:{field.Hotspots.Heat(c.X, c.Z):F2}")));
            foreach (var c in springs)
            {
                Assert.True(field.Hotspots.Heat(c.X, c.Z) > 0);
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
    public void GeysersAndMudPotsSitOnSteadyGround()
    {
        int geyser = Objects.Objects.FindIndex(o => o.Id == "geyser"), mud = Objects.Objects.FindIndex(o => o.Id == "mud-pot"), geysers = 0, muds = 0;
        var pools = new System.Collections.Generic.List<float>();
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var (w, detail, _, field) = World(seed);
            foreach (var c in field.Discoveries().Where(o => o.Def == geyser || o.Def == mud))
            {
                Assert.Null(field.VentFault(c));
                field.SpringPoolsAt(c, pools);
                if (c.Def == geyser) { geysers++; Assert.Equal(1, pools.Count / ObjectField.SpringStride); Assert.True(pools[5] > pools[4]); }
                else { muds++; Assert.InRange(pools.Count / ObjectField.SpringStride, 1, 3); Assert.True(pools[5] - pools[4] < pools[4] - pools[6]); }
            }
        }
        output.WriteLine($"{geysers} geysers, {muds} mud pots");
        Assert.True(geysers > 0 && muds > 0, $"{geysers} geysers, {muds} mud pots in 4 worlds");
    }

    [Fact]
    public void BogPoolsSitShallowInWetland()
    {
        int total = 0;
        var materials = ContentLoader.LoadMaterials(Root);
        Assert.True(materials.Liquid("peat") > 0);
        foreach (var o in Objects.Objects)
            if (o.Placement.Liquid != null) Assert.True(materials.Liquid(o.Placement.Liquid) >= 0, o.Id + " names an unknown liquid");
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var (w, detail, _, field) = World(seed);
            var bogs = field.BogPools();
            int wet = Array.IndexOf(w.BiomeIds, "wetland"), n = w.Config.Size;
            for (int k = 0; k < bogs.Length; k += ObjectField.BogStride)
            {
                total++;
                int c = Math.Min(n - 1, (int)(bogs[k + 1] / w.Config.CellMeters + 0.5f)) * n + Math.Min(n - 1, (int)(bogs[k] / w.Config.CellMeters + 0.5f));
                Assert.Equal(wet, w.Biome[c]);
                Assert.InRange(bogs[k + 3] - bogs[k + 4], ObjectField.BogMinDepthM, ObjectField.BogMaxDepthM);
            }
        }
        output.WriteLine($"{total} bog pools");
        Assert.True(total > 0, "no bog pools in 6 worlds");
    }

    [Fact]
    public void ObjectsKeepOutOfBogMargins()
    {
        int pools = 0, near = 0;
        ulong seed = TestWorlds.DefaultSeed;
        var field = TestWorlds.Default.Field;
        var bogs = field.BogPools();
        float span = field.ChunkMeters;
        var seen = new HashSet<int>();
        for (int k = 0; k < bogs.Length && k < 3 * ObjectField.BogStride; k += ObjectField.BogStride)
        {
            pools++;
            float edge = ObjectField.BogMargin(bogs[k + 2]) + 4f;
            for (int cz = (int)((bogs[k + 1] - edge) / span); cz <= (int)((bogs[k + 1] + edge) / span); cz++)
                for (int cx = (int)((bogs[k] - edge) / span); cx <= (int)((bogs[k] + edge) / span); cx++)
                {
                    if (cx < 0 || cz < 0 || cx >= field.ChunksAcross || cz >= field.ChunksAcross || !seen.Add(cz * field.ChunksAcross + cx)) continue;
                    foreach (var o in field.Chunk(cx, cz))
                    {
                        float r = 0.5f * field.Footprint(o.Def) * o.Size;
                        for (int j = 0; j < bogs.Length; j += ObjectField.BogStride)
                        {
                            float dx = o.X - bogs[j], dz = o.Z - bogs[j + 1], reach = ObjectField.BogMargin(bogs[j + 2]) + r;
                            if (dx * dx + dz * dz < (reach + 4f) * (reach + 4f)) near++;
                            Assert.False(dx * dx + dz * dz < reach * reach, $"seed {seed}: {Objects.Objects[o.Def].Id} at {o.X:F1},{o.Z:F1} stands in the bog margin at {bogs[j]:F0},{bogs[j + 1]:F0}");
                        }
                    }
                }
        }
        output.WriteLine($"{pools} bog pools, {near} objects within 4 m of a margin");
        Assert.True(pools > 0);
    }

    [Fact]
    public void QuicksandPatchesSitOnWetSand()
    {
        var placed = TestWorlds.Default;
        var w = placed.W;
        var field = placed.Field;
        var sands = field.QuicksandPools();
        int liquid = w.Materials.Liquid("quicksand"), pooled = 0, near = 0;
        Assert.True(liquid > 0);
        foreach (var p in field.LiquidPools())
            if (p.Kind == ObjectField.QuicksandPool) { pooled++; Assert.Equal(liquid, p.Liquid); Assert.False(p.Leaks); }
        Assert.Equal(sands.Length / ObjectField.BogStride, pooled);
        float span = field.ChunkMeters;
        var seen = new HashSet<int>();
        for (int k = 0; k < sands.Length; k += ObjectField.BogStride)
        {
            float x = sands[k], z = sands[k + 1], r = sands[k + 2];
            Assert.True(field.SandAt(x, z) && field.DampAt(x, z), $"quicksand at {x:F0},{z:F0} is not on wet sand");
            Assert.InRange(sands[k + 3] - sands[k + 4], ObjectField.QuicksandMinDepthM, ObjectField.QuicksandMaxDepthM);
            Assert.InRange(r, ObjectField.QuicksandMinR, ObjectField.QuicksandMaxR);
            Assert.InRange(field.RimSpan(x, z, r), 0f, ObjectField.QuicksandSteadyM);
            if (k >= 2 * ObjectField.BogStride) continue;
            float edge = ObjectField.BogMargin(r) + 4f;
            for (int cz = (int)((z - edge) / span); cz <= (int)((z + edge) / span); cz++)
                for (int cx = (int)((x - edge) / span); cx <= (int)((x + edge) / span); cx++)
                {
                    if (cx < 0 || cz < 0 || cx >= field.ChunksAcross || cz >= field.ChunksAcross || !seen.Add(cz * field.ChunksAcross + cx)) continue;
                    foreach (var o in field.Chunk(cx, cz))
                    {
                        float dx = o.X - x, dz = o.Z - z, reach = ObjectField.BogMargin(r) + 0.5f * field.Footprint(o.Def) * o.Size;
                        if (dx * dx + dz * dz < (reach + 4f) * (reach + 4f)) near++;
                        Assert.False(dx * dx + dz * dz < reach * reach, $"{Objects.Objects[o.Def].Id} at {o.X:F1},{o.Z:F1} stands in the quicksand margin at {x:F0},{z:F0}");
                    }
                }
        }
        output.WriteLine($"{pooled} quicksand patches, {near} objects within 4 m of a margin");
        Assert.True(pooled > 0, "no quicksand on the default world");
    }

    [Fact]
    public void GeyserCyclesDifferPerSite()
    {
        Structures.GeyserCycle(1, out float i1, out float d1, out float o1);
        Structures.GeyserCycle(2, out float i2, out float d2, out _);
        Assert.InRange(d1, 1, i1);
        Assert.InRange(o1, 0, i1);
        Assert.False(i1 == i2 && d1 == d2);
    }

    [Fact]
    public void LoadedWorldPlacesTheSameDiscoveries()
    {
        var materials = ContentLoader.LoadMaterials(Root);
        var (w, _, _, field) = TestWorlds.Get(TestWorlds.DefaultSeed);
        var loaded = WorldFile.Read(WorldFile.Write(w), materials);
        loaded.Objects = Objects;
        string Rings(ObjectField f) => string.Join(" ", f.Discoveries().Select(r => $"{r.X},{r.Z},{r.Size}"));
        var loadedDetail = new TerrainDetail(loaded);
        string expected = Rings(field);
        Assert.NotEmpty(expected);
        Assert.Equal(expected, Rings(new ObjectField(loaded, loadedDetail, Features.Place(loaded, loadedDetail), Objects)));
    }
}
