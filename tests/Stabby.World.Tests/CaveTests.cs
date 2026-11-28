using System.Collections.Concurrent;
using Xunit.Abstractions;
using System.Text.Json;
using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class CaveTests
{
    readonly ITestOutputHelper output;

    public CaveTests(ITestOutputHelper output) => this.output = output;

    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(Root);
    static readonly MaterialLibrary Materials = ContentLoader.LoadMaterials(Root);
    static readonly CaveLibrary Caves = ContentLoader.LoadCaves(Root);
    static readonly ObjectLibrary Objects = ContentLoader.LoadObjects(Root, Biomes.Select(b => b.Id).ToList());

    sealed class Sample
    {
        public GeneratedWorld W = null!;
        public TerrainDetail Detail = null!;
        public FeatureEdit[] Edits = null!;
        public CaveGraph[] Graphs = null!;
        ObjectField? placed;
        public ObjectField Field => LazyInitializer.EnsureInitialized(ref placed, () => new ObjectField(W, Detail, Edits, W.Objects));
    }

    static readonly ConcurrentDictionary<ulong, Lazy<Sample>> Cache = new();
    static readonly Lazy<bool> Warm = new(() => { Parallel.For(1, 9, seed => Built((ulong)seed)); return true; });

    static readonly Lazy<Sample> Gem = new(() =>
    {
        var lib = Gems();
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var s = Make(seed, lib);
            if (s.Edits.Any(x => x.Shape == FeatureEdit.CrystalCell)) return s;
        }
        throw new Xunit.Sdk.XunitException("no crystal cell in seeds 1..8");
    });

    static CaveLibrary Gems()
    {
        var lib = ContentLoader.LoadCaves(Root);
        lib.CrystalChance = 1f;
        return lib;
    }

    static Sample Make(ulong seed, CaveLibrary caves)
    {
        var w = WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = 512, CellMeters = WorldConfig.CellMetersFor(512) }, Biomes, Materials, null, null, Objects, caves);
        var d = new TerrainDetail(w);
        var edits = Features.Place(w, d, out var graphs);
        return new Sample { W = w, Detail = d, Edits = edits, Graphs = graphs };
    }

    static (Sample, ObjectField, ObjectInstance) FindCrystal()
    {
        var s = Gem.Value;
        var field = s.Field;
        foreach (var o in CaveObjects(s, field)) if (s.W.Objects.Objects[o.Def].Placement.Cave == ObjectField.CaveCrystal) return (s, field, o);
        throw new Xunit.Sdk.XunitException("no crystal");
    }

    static Sample At(ulong seed)
    {
        if (seed <= 8) _ = Warm.Value;
        return Built(seed);
    }

    static Sample Built(ulong seed) => Cache.GetOrAdd(seed, k => new Lazy<Sample>(() => Make(k, Caves))).Value;

    static (Sample, CaveGraph) Find(Func<CaveGraph, bool> want)
    {
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var s = At(seed);
            foreach (var g in s.Graphs) if (want(g)) return (s, g);
        }
        throw new Xunit.Sdk.XunitException("no cave graph matched");
    }

    static List<string> Check(Sample s, FeatureEdit[] edits, CaveGraph[] graphs)
    {
        var e = new List<string>();
        WorldChecks.CheckCaves(s.W, s.Detail, edits, graphs, e);
        return e;
    }

    [Fact]
    public void SeedsGrowEveryClassAndPass()
    {
        var seen = new HashSet<string>();
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var s = At(seed);
            Assert.Empty(Check(s, s.Edits, s.Graphs));
            foreach (var g in s.Graphs) seen.Add(g.Class);
        }
        foreach (var c in Caves.Classes) Assert.Contains(c.Id, seen);
        var (h, hall) = Find(g => g.Class == "hall");
        Assert.True(hall.Nodes.Count(n => n.Entrance) >= 3);
        Assert.Contains(h.Edits, x => x.Shape == FeatureEdit.Bridge && Features.TileOf(x, h.W.Config.CellMeters) == hall.Tile);
    }

    [Fact]
    public void LabyrinthDarkensAwayFromMouths()
    {
        var (s, g) = Find(g => g.Class == "labyrinth");
        var reach = CaveLight.Distances(s.Edits, s.Graphs);
        float cm = s.W.Config.CellMeters, far = 0f, r = 0f;
        bool mouth = false;
        for (int q = 0; q < s.Edits.Length; q++)
        {
            var e = s.Edits[q];
            if (!CaveLight.Lit(e) || Features.TileOf(e, cm) != g.Tile) { if (!CaveLight.Lit(e)) Assert.Equal(0f, reach[2 * q] + reach[2 * q + 1]); continue; }
            Assert.True(reach[2 * q] < CaveLight.Unlit && reach[2 * q + 1] < CaveLight.Unlit);
            mouth |= reach[2 * q] == 0f || reach[2 * q + 1] == 0f;
            if (Math.Max(reach[2 * q], reach[2 * q + 1]) > far) { far = Math.Max(reach[2 * q], reach[2 * q + 1]); r = e.R; }
        }
        Assert.True(mouth);
        Assert.True(far > 4f * r, $"deepest point {far:F0} m from a mouth, tunnel R {r:F0} m");
    }

    [Fact]
    public void HallsBuildAtFullSize()
    {
        var w = WorldGenerator.Generate(new WorldConfig { Seed = 57, Size = 2048, CellMeters = WorldConfig.CellMetersFor(2048) }, Biomes, Materials, null, null, Objects, Caves);
        var d = new TerrainDetail(w);
        var edits = Features.Place(w, d, out var graphs);
        var halls = graphs.Where(g => g.Class == "hall").ToList();
        Assert.NotEmpty(halls);
        Assert.All(halls, hall => Assert.True(hall.Nodes.Count(n => n.Entrance) >= 2));
        var e = new List<string>();
        WorldChecks.CheckCaves(w, d, edits, graphs, e);
        Assert.Empty(e);
    }

    [Fact]
    public void HallWallNormalsFollowTheField()
    {
        var (s, hall) = Find(g => g.Class == "hall");
        var e = new List<string>();
        WorldChecks.CheckCaveNormals(s.W, s.Detail, new EditGrid(s.Edits, s.W.Config.Size * s.W.Config.CellMeters), new[] { hall }, e);
        Assert.Empty(e);
    }

    [Fact]
    public void TunnelsAreNarrowFineAndVaried()
    {
        var widths = new HashSet<float>();
        for (ulong seed = 1; seed <= 8; seed++)
            foreach (var g in At(seed).Graphs.Where(g => !Mines.IsMine(g) && !DungeonLinks.IsLink(g) && !ObjectField.IsTunnel(g)))
                foreach (var edge in g.Edges)
                {
                    var c = Caves.Find(g.Class)!;
                    Assert.InRange(edge.R, c.RadiusMin, c.RadiusMax);
                    Assert.InRange(2f * edge.R * Features.CaveHollow, 4f, 6f);
                    widths.Add(edge.R);
                }
        Assert.True(widths.Count > 10);
        Assert.All(At(1).Edits.Where(x => x.Shape == FeatureEdit.Cave), x => Assert.True(x.Fine));
    }

    [Fact]
    public void PinchedTunnelIsTooNarrow()
    {
        var (s, g) = Find(g => g.Edges.Count >= 2 && !Mines.IsMine(g) && !DungeonLinks.IsLink(g) && !ObjectField.IsTunnel(g));
        var edge = g.Edges[g.Edges.Count - 1];
        var p = edge.Points;
        int m = (p.Length / 6) * 3;
        float mx = p[m], my = p[m + 1] + edge.R * 0.3f, mz = p[m + 2];
        var pinched = s.Edits.Select(x => x.Shape == FeatureEdit.Cave && Near(x, mx, my, mz) ? Pinch(x) : x).ToArray();
        Assert.Contains(Check(s, pinched, s.Graphs), v => v.Contains("too narrow"));
    }

    static FeatureEdit Pinch(FeatureEdit x)
    {
        x.R *= 0.3f;
        return x;
    }

    [Fact]
    public void FilledTunnelLeavesChambersUnreachable()
    {
        var (s, g) = Find(g => g.Edges.Count >= 2 && !g.Nodes.TrueForAll(n => n.Entrance));
        var edge = g.Edges.Find(x => !(g.Nodes[x.A].Entrance && g.Nodes[x.B].Entrance))!;
        var p = edge.Points;
        int m = (p.Length / 6) * 3;
        float mx = p[m], my = p[m + 1] + edge.R * 0.3f, mz = p[m + 2];
        var kept = s.Edits.Where(x => !(FeatureEdit.IsCaveCarve(x.Shape) && Near(x, mx, my, mz))).ToArray();
        Assert.True(kept.Length < s.Edits.Length);
        Assert.Contains(Check(s, kept, s.Graphs), v => v.Contains("not walkable from an entrance"));
    }

    static bool Near(FeatureEdit e, float x, float y, float z)
    {
        float ex = e.Bx - e.Ax, ey = e.By - e.Ay, ez = e.Bz - e.Az, px = x - e.Ax, py = y - e.Ay, pz = z - e.Az;
        float ll = ex * ex + ey * ey + ez * ez, t = ll > 0 ? Math.Clamp((px * ex + py * ey + pz * ez) / ll, 0f, 1f) : 0f;
        float qx = px - t * ex, qy = py - t * ey, qz = pz - t * ez;
        return qx * qx + qy * qy + qz * qz < (e.R + 4f) * (e.R + 4f);
    }

    [Fact]
    public void BridgeSunkInRockFails()
    {
        var (s, g) = Find(g => g.Class == "hall");
        var edits = (FeatureEdit[])s.Edits.Clone();
        int k = Array.FindIndex(edits, x => x.Shape == FeatureEdit.Bridge && Features.TileOf(x, s.W.Config.CellMeters) == g.Tile);
        edits[k].Ay -= 200f;
        edits[k].By -= 200f;
        Assert.Contains(Check(s, edits, s.Graphs), v => v.StartsWith("rock bridge", StringComparison.Ordinal));
    }

    [Fact]
    public void CarveOverTheSeaFails()
    {
        var s = At(1);
        int n = s.W.Config.Size, c = Array.FindIndex(s.W.Sea, x => x);
        float cm = s.W.Config.CellMeters, x = c % n * cm, z = c / n * cm;
        var edits = (FeatureEdit[])s.Edits.Clone();
        int k = Array.FindIndex(edits, e => e.Shape == FeatureEdit.Cave);
        edits[k].Bx += x - edits[k].Ax;
        edits[k].Bz += z - edits[k].Az;
        edits[k].Ax = x;
        edits[k].Az = z;
        Assert.Contains(Check(s, edits, s.Graphs), v => v.Contains("breaks into the sea or a lake"));
    }

    static List<ObjectInstance> CaveObjects(Sample s, ObjectField field)
    {
        float span = field.ChunkMeters;
        int across = field.ChunksAcross;
        var seen = new HashSet<int>();
        var found = new List<ObjectInstance>();
        foreach (var x in s.Edits)
        {
            if (!x.CaveCarve) continue;
            for (float t = 0f; t <= 1f; t += 0.25f)
            {
                float px = x.Ax + (x.Bx - x.Ax) * t, pz = x.Az + (x.Bz - x.Az) * t;
                int c = Math.Min(across - 1, Math.Max(0, (int)(pz / span))) * across + Math.Min(across - 1, Math.Max(0, (int)(px / span)));
                if (!seen.Add(c)) continue;
                foreach (var o in field.Chunk(c % across, c / across)) if (ObjectField.IsCave(s.W.Objects.Objects[o.Def])) found.Add(o);
            }
        }
        return found;
    }

    static (Sample, ObjectField, ObjectInstance) FindCave(int kind)
    {
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var s = At(seed);
            var field = s.Field;
            foreach (var o in CaveObjects(s, field)) if (s.W.Objects.Objects[o.Def].Placement.Cave == kind) return (s, field, o);
        }
        throw new Xunit.Sdk.XunitException("no cave object of kind " + kind);
    }

    [Fact]
    public void StreamsRunDownhillToSpringsPoolsOrSinks()
    {
        var ends = new int[4];
        int graphs = 0;
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var s = At(seed);
            var streams = CaveBrooks.Build(s.W, s.Detail, s.Edits, s.Graphs);
            graphs += s.Graphs.Count(g => !DungeonLinks.IsLink(g) && !ObjectField.IsTunnel(g));
            var e = new List<string>();
            WorldChecks.CheckCaveBrooks(s.W, s.Graphs, s.Edits, streams, null, e);
            Assert.True(e.Count == 0, string.Join("; ", e));
            foreach (var x in streams)
            {
                ends[x.End]++;
                Assert.True(x.Flow > 0f);
                var g = s.Graphs[x.Graph];
                for (int k = 1; k < x.Nodes.Length; k++) Assert.True(g.Nodes[x.Nodes[k]].Y < g.Nodes[x.Nodes[k - 1]].Y);
            }
        }
        Console.WriteLine($"streams in {graphs} graphs: springs {ends[0]}, pools {ends[1]}, sinks {ends[2]}, falls {ends[3]}");
        Assert.True(ends.Sum() >= graphs / 6, $"only {ends.Sum()} streams in {graphs} cave graphs");
        Assert.True(ends[0] > 0 && ends[2] > 0);
    }

    [Fact]
    public void UphillOrStrandedStreamFails()
    {
        Sample s = null!;
        var streams = Array.Empty<CaveBrook>();
        for (ulong seed = 1; seed <= 8 && streams.Length == 0; seed++) streams = CaveBrooks.Build((s = At(seed)).W, s.Detail, s.Edits, s.Graphs);
        Assert.NotEmpty(streams);
        var x = streams[0];
        var up = new CaveBrook { Graph = x.Graph, End = x.End, Outlet = x.Outlet, Flow = x.Flow, Nodes = x.Nodes, Points = (float[])x.Points.Clone() };
        up.Points[up.Points.Length - CaveBrook.Stride + 1] = up.Points[1] + 1f;
        var e = new List<string>();
        WorldChecks.CheckCaveBrooks(s.W, s.Graphs, s.Edits, new[] { up }, null, e);
        Assert.Contains(e, m => m.Contains("flows uphill"));
        var lost = new CaveBrook { Graph = x.Graph, End = 7, Outlet = -1, Flow = x.Flow, Nodes = x.Nodes, Points = x.Points };
        e.Clear();
        WorldChecks.CheckCaveBrooks(s.W, s.Graphs, s.Edits, new[] { lost }, null, e);
        Assert.Contains(e, m => m.Contains("no spring, pool or sink"));
    }

    [Fact]
    public void SinkWithoutItsHoleFails()
    {
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var s = At(seed);
            foreach (var x in CaveBrooks.Build(s.W, s.Detail, s.Edits, s.Graphs))
            {
                if (x.End != CaveBrook.Sink) continue;
                var bare = Array.FindAll(s.Edits, f => f.Shape != FeatureEdit.Sink);
                var e = new List<string>();
                WorldChecks.CheckCaveBrooks(s.W, s.Graphs, bare, new[] { x }, null, e);
                Assert.Contains(e, m => m.Contains("into no hole"));
                return;
            }
        }
        throw new Xunit.Sdk.XunitException("no sink brook in seeds 1-8");
    }

    [Fact]
    public void CaveFallsDropIntoTheirShaftPools()
    {
        int falls = 0, brooks = 0;
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var s = At(seed);
            var all = CaveBrooks.Build(s.W, s.Detail, s.Edits, s.Graphs);
            brooks += all.Length;
            var pools = s.Field.CavePools();
            var e = new List<string>();
            WorldChecks.CheckCaveFalls(s.Detail, s.Edits, all, pools, e);
            Assert.True(e.Count == 0, string.Join("; ", e));
            foreach (var x in all)
            {
                if (x.End != CaveBrook.Plunge) continue;
                falls++;
                var bare = Array.FindAll(s.Edits, f => f.Shape != FeatureEdit.Sink);
                e.Clear();
                WorldChecks.CheckCaveFalls(s.Detail, bare, new[] { x }, null, e);
                Assert.Contains(e, m => m.Contains("leaves its carve") || m.Contains("no pool or basin"));
                e.Clear();
                WorldChecks.CheckCaveFalls(s.Detail, s.Edits, new[] { x }, Array.Empty<LiquidPool>(), e);
                Assert.Contains(e, m => m.Contains("drops into no water"));
            }
        }
        Console.WriteLine($"cave falls {falls} of {brooks} brooks");
        Assert.True(falls > 0 && falls * 3 <= brooks, $"{falls} falls in {brooks} brooks");
    }

    [Fact]
    public void ChamberPoolsSitInTheirCarveAndHallsHoldTheLargest()
    {
        int pools = 0, halls = 0;
        float hall = 0f, chamber = 0f;
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var s = At(seed);
            var field = s.Field;
            var e = new List<string>();
            var found = field.CavePools();
            WorldChecks.CheckCavePools(s.W, s.Detail, s.Edits, found, e);
            Assert.True(e.Count == 0, string.Join("; ", e));
            foreach (var p in found)
            {
                Assert.Equal(ObjectField.CavePool, p.Kind);
                pools++;
                bool big = s.Graphs.Any(g => g.Nodes.Any(n => n.Hall && (n.X - p.X) * (n.X - p.X) + (n.Z - p.Z) * (n.Z - p.Z) < 60f * 60f));
                if (big) { halls++; hall = Math.Max(hall, p.Reach); }
                else chamber = Math.Max(chamber, p.Reach);
            }
            Assert.Equal(found.Length, field.LiquidPools().Count(p => p.Kind == ObjectField.CavePool));
        }
        Assert.True(pools >= 2, $"{pools} cave pools over eight worlds");
        Assert.True(halls > 0 && hall > chamber, $"{halls} hall pools, largest hall pool {hall:F1} m, largest chamber pool {chamber:F1} m");
    }

    [Fact]
    public void CavePoolOutsideItsChamberOrLeakingFails()
    {
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var s = At(seed);
            var found = s.Field.CavePools();
            if (found.Length == 0) continue;
            var p = found[0];
            var leak = new LiquidPool { X = p.X, Z = p.Z, Reach = p.Reach, Level = p.Level, Floor = p.Floor, Kind = p.Kind, I0 = p.I0, J0 = p.J0, Nx = p.Nx, Nz = p.Nz, WetCount = p.WetCount, Leaks = true, Ground = p.Ground, Wet = p.Wet };
            var moved = new LiquidPool { X = p.X, Z = p.Z, Reach = p.Reach, Level = p.Level + 40f, Floor = p.Floor, Kind = p.Kind, I0 = p.I0, J0 = p.J0, Nx = p.Nx, Nz = p.Nz, WetCount = p.WetCount, Ground = p.Ground, Wet = p.Wet };
            var e = new List<string>();
            WorldChecks.CheckCavePools(s.W, s.Detail, s.Edits, new[] { leak }, e);
            Assert.Contains(e, m => m.Contains("leaks out"));
            e.Clear();
            WorldChecks.CheckCavePools(s.W, s.Detail, s.Edits, new[] { moved }, e);
            Assert.Contains(e, m => m.Contains("outside its carved chamber"));
            return;
        }
        throw new Xunit.Sdk.XunitException("no cave pool in eight worlds");
    }

    [Fact]
    public void CaveObjectsAreCommonAndPass()
    {
        int tiles = 0, drips = 0, mites = 0, rubble = 0, columns = 0, flows = 0;
        var contacts = new List<ObjectColumn>();
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var s = At(seed);
            var field = s.Field;
            foreach (var g in s.Graphs) if (!Mines.IsMine(g) && !DungeonLinks.IsLink(g)) tiles++;
            foreach (var x in s.Edits) if (x.Shape == FeatureEdit.Speleothem && x.T == 0f) flows++;
            foreach (var o in CaveObjects(s, field))
            {
                int kind = s.W.Objects.Objects[o.Def].Placement.Cave;
                if (kind == ObjectField.CaveStalactite) drips++;
                else if (kind == ObjectField.CaveStalagmite) mites++;
                else if (kind == ObjectField.CaveColumn) columns++;
                else if (kind == ObjectField.CaveRubble) rubble++;
                string fault = field.CaveFault(o) ?? field.ContactFault(o, contacts);
                Assert.True(fault == null, fault);
            }
        }
        Assert.True(drips >= 8 * tiles, $"{drips} stalactites over {tiles} cave tiles");
        Assert.True(mites >= drips / 8, $"{mites} stalagmites for {drips} stalactites");
        Assert.True(rubble >= drips, $"{rubble} rubble stones for {drips} stalactites");
        Assert.True(columns >= tiles, $"{columns} columns over {tiles} cave tiles");
        Assert.True(flows >= tiles, $"{flows} flowstones over {tiles} cave tiles");
    }

    [Fact]
    public void CaveLightsAreSporadicAndPass()
    {
        int tiles = 0, lit = 0, lights = 0, placed = 0, sky = 0, emitted = 0;
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var s = At(seed);
            var field = s.Field;
            tiles += s.Graphs.Length;
            var e = new List<string>();
            WorldChecks.CheckCaveLights(s.W, s.Edits, field, e);
            Assert.True(e.Count == 0, string.Join("; ", e));
            var litTiles = new HashSet<int>();
            for (int i = 0; i < field.CaveLightCount; i++)
            {
                var p = field.CaveLight(i);
                lights++;
                litTiles.Add(p.Tile);
                if (p.Kind == ObjectField.CaveSkylight) sky++;
                if (!field.CaveLightAt(i, out var o)) continue;
                if (placed++ > 0) continue;
                int cx = Math.Min(field.ChunksAcross - 1, (int)(o.X / field.ChunkMeters)), cz = Math.Min(field.ChunksAcross - 1, (int)(o.Z / field.ChunkMeters));
                foreach (var c in field.Place(cx, cz)) if (ObjectField.IsCaveLight(s.W.Objects.Objects[c.Def].Placement.Cave)) emitted++;
            }
            lit += litTiles.Count;
        }
        output.WriteLine($"cave lights: {lights} over {tiles} caves, {lit} lit ({100f * lit / Math.Max(1, tiles):F0}%), {sky} skylights, {placed} placed, {emitted} emitted near carves");
        Assert.True(lights <= Caves.LightMost * Math.Max(1, lit), $"{lights} lights over {lit} lit caves");
        Assert.True(lit > 0 && lit < tiles, $"{lit} of {tiles} caves lit");
        Assert.True(placed * 2 >= lights, $"{placed} of {lights} cave lights found a floor");
        Assert.True(emitted > 0, "no cave light reached a chunk");
    }

    [Fact]
    public void CaveLightOffTheFloorFails()
    {
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var s = At(seed);
            var field = s.Field;
            for (int i = 0; i < field.CaveLightCount; i++)
            {
                if (!field.CaveLightAt(i, out var o)) continue;
                Assert.Null(field.CaveFault(o));
                o.Y += 1.5f;
                Assert.Contains("floats above the cave floor", field.CaveFault(o) ?? "");
                return;
            }
        }
        throw new Xunit.Sdk.XunitException("no cave light placed");
    }

    [Fact]
    public void CaveObjectsMatchAcrossRuns()
    {
        var (s, field, _) = FindCave(ObjectField.CaveStalactite);
        var again = new ObjectField(s.W, s.Detail, s.Edits, s.W.Objects);
        var a = CaveObjects(s, field);
        var b = CaveObjects(s, again);
        Assert.Equal(a.Count, b.Count);
        for (int k = 0; k < a.Count; k++) Assert.True(a[k].Id == b[k].Id && a[k].X == b[k].X && a[k].Y == b[k].Y && a[k].Size == b[k].Size);
    }

    [Fact]
    public void StalagmiteInAPassageFails()
    {
        var (s, field, o) = FindCave(ObjectField.CaveStalagmite);
        var p = s.Edits.Where(x => x.Kind == FeatureEdit.Carve && x.Shape == FeatureEdit.Cave).OrderBy(x => (0.5f * (x.Ax + x.Bx) - o.X) * (0.5f * (x.Ax + x.Bx) - o.X) + (0.5f * (x.Az + x.Bz) - o.Z) * (0.5f * (x.Az + x.Bz) - o.Z)).First();
        o.X = 0.5f * (p.Ax + p.Bx); o.Z = 0.5f * (p.Az + p.Bz);
        var rock = new EditArray { Items = s.Edits };
        float ay = 0.5f * (p.Ay + p.By);
        o.Y = Walk.Surface(new DetailGround { Detail = s.Detail }, rock, Features.LatticeStepM, o.X, o.Z, ay, ay - 20f) - 0.1f * o.Size;
        Assert.Contains("blocks a cave passage", field.CaveFault(o) ?? "");
    }

    [Fact]
    public void LowStalactiteFails()
    {
        var (s, field, o) = FindCave(ObjectField.CaveStalactite);
        var rock = new EditArray { Items = s.Edits };
        float floor = Walk.Surface(new DetailGround { Detail = s.Detail }, rock, Features.LatticeStepM, o.X, o.Z, o.Y - o.Size, o.Y - o.Size - 40f);
        o.Size = o.Y - floor - 1f;
        Assert.Contains("hangs into the walking headroom", field.CaveFault(o) ?? "");
    }

    [Fact]
    public void CaveObjectsOffTheirRockLeaveNoStamp()
    {
        var kinds = new[] { ObjectField.CaveStalactite, ObjectField.CaveStalagmite, ObjectField.CaveColumn };
        var hits = new Dictionary<int, (Sample, ObjectInstance)>();
        for (ulong seed = 1; seed <= 4 && hits.Count < kinds.Length; seed++)
        {
            var s = At(seed);
            foreach (var o in CaveObjects(s, s.Field))
            {
                int k = s.W.Objects.Objects[o.Def].Placement.Cave;
                if (Array.IndexOf(kinds, k) >= 0 && !hits.ContainsKey(k)) hits[k] = (s, o);
            }
        }
        var lib = ContentLoader.LoadObjects(Root, Biomes.Select(b => b.Id).ToList());
        foreach (var (_, o) in hits.Values) lib.Objects[o.Def].Parts[0].OffsetY = 1.5f;
        var moved = new Dictionary<Sample, ObjectField>();
        foreach (int kind in kinds)
        {
            if (!hits.TryGetValue(kind, out var hit)) throw new Xunit.Sdk.XunitException("no cave object of kind " + kind);
            var (s, o) = hit;
            if (!moved.TryGetValue(s, out var field)) moved[s] = field = new ObjectField(s.W, s.Detail, s.Edits, lib);
            Assert.EndsWith(kind == ObjectField.CaveStalactite ? "leaves no ceiling contact stamp" : "leaves no contact stamp", field.ContactFault(o, new List<ObjectColumn>()) ?? "");
        }
    }

    [Fact]
    public void FloatingStalactiteFails()
    {
        var (_, field, o) = FindCave(ObjectField.CaveStalactite);
        o.Y -= 0.6f * o.Size;
        Assert.Contains("floats free of the ceiling", field.CaveFault(o) ?? "");
    }

    [Fact]
    public void IceCavesFormOnlyInColdGroundWithIcicles()
    {
        int ice = 0, icicles = 0, mites = 0;
        for (ulong seed = 1; seed <= 8 && (icicles == 0 || mites == 0); seed++)
        {
            var s = At(seed);
            var e = new List<string>();
            WorldChecks.CheckCaveIce(s.W, s.Edits, s.Graphs, e);
            Assert.Empty(e);
            foreach (var g in s.Graphs.Where(g => g.Ice))
            {
                ice++;
                Assert.False(g.Crystal);
                Assert.True(Underground.EntranceTempC(s.W, g) <= Caves.IceMaxTempC);
            }
            if (!s.Graphs.Any(g => g.Ice)) continue;
            var field = s.Field;
            foreach (var o in CaveObjects(s, field))
            {
                var pl = s.W.Objects.Objects[o.Def].Placement;
                if (pl.Cave != ObjectField.CaveStalactite && pl.Cave != ObjectField.CaveStalagmite) continue;
                float span = Features.TileCells * s.W.Config.CellMeters;
                int tile = (int)Math.Floor(o.Z / span) * 65536 + (int)Math.Floor(o.X / span);
                Assert.Equal(s.Graphs.Any(x => x.Tile == tile && x.Ice), pl.CaveIce);
                if (pl.CaveIce && pl.Cave == ObjectField.CaveStalactite) icicles++;
                else if (pl.CaveIce) mites++;
            }
        }
        output.WriteLine($"{ice} ice caves, {icicles} icicles, {mites} ice stalagmites");
        Assert.True(ice > 0 && icicles > 0 && mites > 0, $"{ice} ice caves, {icicles} icicles, {mites} ice stalagmites in seeds 1..8");
    }

    [Fact]
    public void WarmWorldsGrowNoIceCaves()
    {
        var lib = ContentLoader.LoadCaves(Root);
        lib.IceChance = 1f;
        lib.IceMaxTempC = -60f;
        var s = Make(1, lib);
        Assert.DoesNotContain(s.Graphs, g => g.Ice);
        Assert.DoesNotContain(s.Edits, x => x.Ice);
    }

    const ulong LavaSeed = 13;
    static readonly Lazy<Sample> Lava = new(() => At(LavaSeed));

    [Fact]
    public void ObsidianCavesFormInHotDryGroundWithLavaPools()
    {
        var s = Lava.Value;
        Assert.True(s.Graphs.Any(g => g.Obsidian), $"seed {LavaSeed} no longer grows an obsidian cave; pin a new LavaSeed");
        var e = new List<string>();
        WorldChecks.CheckCaveObsidian(s.W, s.Edits, s.Graphs, e);
        Assert.Empty(e);
        var field = s.Field;
        float span = Features.TileCells * s.W.Config.CellMeters;
        int lava = 0, glow = 0, drips = 0;
        foreach (var p in field.CavePools())
        {
            int tile = (int)Math.Floor(p.Z / span) * 65536 + (int)Math.Floor(p.X / span);
            Assert.Equal(s.Graphs.Any(x => x.Tile == tile && x.Obsidian), field.LavaPool(p));
            if (field.LavaPool(p)) lava++;
        }
        Assert.True(lava > 0, $"seed {LavaSeed} no longer holds a lava pool; pin a new LavaSeed");
        for (int i = 0; i < field.CaveLightCount; i++)
            if (field.CaveLight(i).Kind == ObjectField.CaveLava && field.CaveLightAt(i, out _)) glow++;
        Assert.True(glow > 0, $"seed {LavaSeed}: {lava} lava pools but no lava glow");
        WorldChecks.CheckCaveLights(s.W, s.Edits, field, e);
        Assert.Empty(e);
        float cs = field.ChunkMeters;
        var seen = new HashSet<int>();
        foreach (var x in s.Edits)
        {
            if (!x.Obsidian || !x.CaveCarve) continue;
            int c = (int)(0.5f * (x.Az + x.Bz) / cs) * field.ChunksAcross + (int)(0.5f * (x.Ax + x.Bx) / cs);
            if (!seen.Add(c)) continue;
            foreach (var o in field.Chunk(c % field.ChunksAcross, c / field.ChunksAcross))
            {
                int kind = s.W.Objects.Objects[o.Def].Placement.Cave;
                if (kind != ObjectField.CaveStalactite && kind != ObjectField.CaveStalagmite && kind != ObjectField.CaveColumn) continue;
                string fault = field.CaveFault(o);
                Assert.True(fault == null || !fault.Contains("obsidian"), fault);
                if (fault == null) drips++;
            }
        }
        output.WriteLine($"{lava} lava pools, {glow} lava glows, {drips} dripstones beside obsidian");
    }

    [Fact]
    public void LavaPoolOnAPassageFails()
    {
        var s = Lava.Value;
        var rooms = s.Edits.Where(x => x.Kind == FeatureEdit.Carve && FeatureEdit.IsRoom(x.Shape)).ToList();
        int i = Array.FindIndex(s.Edits, x => x.Obsidian && Underground.PoolBasin(rooms, x));
        Assert.True(i >= 0, $"seed {LavaSeed} no longer holds a lava pool; pin a new LavaSeed");
        var room = rooms.First(r => Underground.InChamber(new[] { r }, s.Edits[i].Ax, Underground.BasinTop(s.Edits[i]), s.Edits[i].Az));
        var tunnel = s.Edits.First(x => x.Shape == FeatureEdit.Cave && (Near(x.Ax, x.Az, room.Ax, room.Az) || Near(x.Bx, x.Bz, room.Ax, room.Az)));
        float tx = Near(tunnel.Ax, tunnel.Az, room.Ax, room.Az) ? tunnel.Ax : tunnel.Bx, tz = tx == tunnel.Ax ? tunnel.Az : tunnel.Bz;
        var moved = (FeatureEdit[])s.Edits.Clone();
        moved[i].Ax = tx;
        moved[i].Az = tz;
        moved[i].Bx = moved[i].Ax;
        moved[i].Bz = moved[i].Az;
        var e = new List<string>();
        WorldChecks.CheckCaveObsidian(s.W, moved, s.Graphs, e);
        Assert.Contains(e, m => m.Contains("blocks a cave passage"));
    }

    static bool Near(float ax, float az, float bx, float bz) => Math.Abs(ax - bx) < 1e-2f && Math.Abs(az - bz) < 1e-2f;

    [Fact]
    public void WetWorldsGrowNoObsidianCaves()
    {
        var lib = ContentLoader.LoadCaves(Root);
        lib.ObsidianChance = 1f;
        lib.ObsidianMaxRainMm = 0f;
        var s = Make(1, lib);
        Assert.DoesNotContain(s.Graphs, g => g.Obsidian);
        Assert.DoesNotContain(s.Edits, x => x.Obsidian);
    }

    [Fact]
    public void CrystalCavesCarveFacetedCellsLinedWithCrystals()
    {
        var s = Gem.Value;
        var cells = s.Edits.Where(x => x.Kind == FeatureEdit.Carve && x.Shape == FeatureEdit.CrystalCell).ToArray();
        Assert.NotEmpty(cells);
        Assert.Empty(Check(s, s.Edits, s.Graphs));
        var field = s.Field;
        int floor = 0, wall = 0, ceiling = 0;
        var contacts = new List<ObjectColumn>();
        foreach (var o in CaveObjects(s, field))
        {
            if (s.W.Objects.Objects[o.Def].Placement.Cave != ObjectField.CaveCrystal) continue;
            float up = 1f - 2f * (o.Qx * o.Qx + o.Qz * o.Qz);
            if (up > 0.7f) floor++; else if (up < -0.7f) ceiling++; else wall++;
            string fault = field.CaveFault(o) ?? field.ContactFault(o, contacts);
            Assert.True(fault == null, fault);
        }
        Assert.True(floor > 0 && wall > 0 && ceiling > 0, $"crystals: {floor} floor, {wall} wall, {ceiling} ceiling");
        Assert.Contains(s.Graphs, x => x.Crystal);
    }

    [Fact]
    public void CrystalCellWallsAreFaceted()
    {
        var s = Gem.Value;
        var c = s.Edits.First(x => x.Kind == FeatureEdit.Carve && x.Shape == FeatureEdit.CrystalCell);
        var round = c;
        round.Shape = FeatureEdit.CaveRoom;
        float most = 0f;
        for (int k = 0; k < 64; k++)
        {
            float a = k * 0.0981748f, x = 0.6f * c.R * Fold(a), z = 0.6f * c.R * Fold(a + 1.5707963f);
            c.Reach(c.Ax + x, c.Ay + 0.3f * c.R, c.Az + z, out float df);
            round.Reach(c.Ax + x, c.Ay + 0.3f * c.R, c.Az + z, out float dr);
            most = Math.Max(most, Math.Abs(df - dr));
        }
        Assert.True(most > 0.05f * c.R, $"faceted cell differs from a round room by only {most:F2} m");
    }

    static float Fold(float a) => MathF.Cos(a);

    [Fact]
    public void CrystalOffItsRockOrOutsideItsCellFails()
    {
        var (_, field, o) = FindCrystal();
        float ax = 2f * (o.Qx * o.Qy - o.Qw * o.Qz), ay = 1f - 2f * (o.Qx * o.Qx + o.Qz * o.Qz), az = 2f * (o.Qy * o.Qz + o.Qw * o.Qx);
        var free = o;
        free.X += ax * 1.5f; free.Y += ay * 1.5f; free.Z += az * 1.5f;
        Assert.Contains("floats free of the rock", field.CaveFault(free) ?? "");
        var buried = o;
        buried.X -= ax * 3f; buried.Y -= ay * 3f; buried.Z -= az * 3f;
        Assert.Contains("is buried in rock", field.CaveFault(buried) ?? "");
    }

    [Fact]
    public void SchemaMatchesContent()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "schemas", "caves.schema.json")));
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "caves.json")));
        void Same(JsonElement sc, JsonElement d)
        {
            var names = d.EnumerateObject().Select(p => p.Name).ToList();
            Assert.Empty(sc.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).Except(names));
            Assert.Empty(names.Except(sc.GetProperty("properties").EnumerateObject().Select(p => p.Name)));
        }
        var top = schema.RootElement;
        Same(top, data.RootElement);
        Assert.Equal(ContentLoader.CaveSchemaVersion, top.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32());
        foreach (var c in data.RootElement.GetProperty("classes").EnumerateArray()) Same(top.GetProperty("$defs").GetProperty("class"), c);
    }

    [Fact]
    public void FallbackMustNameAPlainClass()
    {
        var json = "{\"schemaVersion\": 1, \"chance\": 0.3, \"classes\": [" +
            "{\"id\": \"a\", \"fallback\": \"b\", \"weight\": 1, \"nodes\": [1, 1], \"radius\": [30, 34], \"edge\": [60, 90], \"turn\": 0.5, \"warp\": 0.1, \"lumps\": 0.03, \"roomLumps\": 0.06}," +
            "{\"id\": \"b\", \"fallback\": \"a\", \"weight\": 1, \"nodes\": [1, 1], \"radius\": [30, 34], \"edge\": [60, 90], \"turn\": 0.5, \"warp\": 0.1, \"lumps\": 0.03, \"roomLumps\": 0.06}]}";
        Assert.Throws<InvalidDataException>(() => ContentLoader.ParseCaves(json, "caves.json"));
    }
}
