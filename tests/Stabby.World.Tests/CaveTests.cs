using System.Text.Json;
using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class CaveTests
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(Root);
    static readonly MaterialLibrary Materials = ContentLoader.LoadMaterials(Root);
    static readonly CaveLibrary Caves = ContentLoader.LoadCaves(Root);

    sealed class Sample
    {
        public GeneratedWorld W = null!;
        public TerrainDetail Detail = null!;
        public FeatureEdit[] Edits = null!;
        public CaveGraph[] Graphs = null!;
    }

    static readonly Dictionary<ulong, Sample> Cache = new();

    static Sample At(ulong seed)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(seed, out var s)) return s;
            var w = WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = 512, CellMeters = WorldConfig.CellMetersFor(512) }, Biomes, Materials, null, null, null, Caves);
            var d = new TerrainDetail(w);
            var edits = Features.Place(w, d, out var graphs);
            return Cache[seed] = new Sample { W = w, Detail = d, Edits = edits, Graphs = graphs };
        }
    }

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
            foreach (var g in At(seed).Graphs)
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
        var (s, g) = Find(g => g.Edges.Count >= 2);
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
        var kept = s.Edits.Where(x => !((x.Shape == FeatureEdit.Cave || x.Shape == FeatureEdit.CaveRoom) && Near(x, mx, my, mz))).ToArray();
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
