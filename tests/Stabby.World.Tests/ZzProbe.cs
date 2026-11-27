using Xunit.Abstractions;

namespace Stabby.World.Tests;

public class ZzProbe
{
    readonly ITestOutputHelper output;
    static readonly string R = Stabby.Content.ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<BiomeDef> B = Stabby.Content.ContentLoader.LoadBiomes(R);
    static readonly MaterialLibrary M = Stabby.Content.ContentLoader.LoadMaterials(R);
    static readonly CaveLibrary C = Stabby.Content.ContentLoader.LoadCaves(R);
    public ZzProbe(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Defaults()
    {
        var lines = new string[24];
        Parallel.For(1, 25, new ParallelOptions { MaxDegreeOfParallelism = 6 }, s =>
        {
            var p = TestWorlds.Place((ulong)s, 256);
            var w = p.W; var f = p.Field;
            int inn = TestWorlds.Objects.Objects.FindIndex(o => o.Id == "inn");
            int inns = Enumerable.Range(0, f.ChunksAcross * f.ChunksAcross).Sum(c => f.Chunk(c % f.ChunksAcross, c / f.ChunksAcross).Count(o => o.Def == inn));
            bool cave = p.Graphs.Any(g => !Mines.IsMine(g) && !DungeonLinks.IsLink(g)), lab = p.Graphs.Any(g => g.Class == "labyrinth"), river = w.Water.Rivers.Count > 0, town = f.TownPlans().Length > 0;
            bool dun = p.Sites.Any(o => ObjectField.IsDungeon(w.Objects, o)), mine = p.Graphs.Any(Mines.IsMine), link = p.Graphs.Any(DungeonLinks.IsLink);
            bool all = cave && lab && river && town && inns > 0 && dun && mine && link;
            lines[s - 1] = $"seed {s}: ALL {all} cave {cave} lab {lab} river {river} town {town} inn {inns} dun {dun} mine {mine} link {link} volc {w.Sketch.Volcanoes.Count}";
        });
        foreach (var l in lines) if (l != null) output.WriteLine(l);
    }

    [Fact]
    public void Halls()
    {
        var lines = new string[130];
        Parallel.For(49, 121, new ParallelOptions { MaxDegreeOfParallelism = 5 }, s =>
        {
            var w = WorldGenerator.Generate(new WorldConfig { Seed = (ulong)s, Size = 2048, CellMeters = WorldConfig.CellMetersFor(2048) }, B, M, null, null, TestWorlds.Objects, C);
            var d = new TerrainDetail(w);
            Features.Place(w, d, out var graphs);
            var halls = graphs.Where(g => g.Class == "hall").ToList();
            lines[s - 1] = $"halls seed {s}: {halls.Count} ok {halls.All(h => h.Nodes.Count(n => n.Entrance) >= 2)} " + string.Join(",", graphs.GroupBy(g => g.Class).Select(g => g.Key + "=" + g.Count()));
        });
        foreach (var l in lines) if (l != null) output.WriteLine(l);
    }
}
