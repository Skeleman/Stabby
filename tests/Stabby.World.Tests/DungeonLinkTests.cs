using System;
using System.Collections.Generic;
using System.Linq;
using Stabby.Content;
using Stabby.World;
using Xunit;
using Xunit.Abstractions;

namespace Stabby.World.Tests;

public class DungeonLinkTests
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(Root);
    static readonly MaterialLibrary Materials = ContentLoader.LoadMaterials(Root);
    static readonly CaveLibrary Caves = ContentLoader.LoadCaves(Root);
    static readonly ObjectLibrary Objects = ContentLoader.LoadObjects(Root, Biomes.Select(b => b.Id).ToList());
    readonly ITestOutputHelper output;
    public DungeonLinkTests(ITestOutputHelper o) { output = o; }

    static (GeneratedWorld w, TerrainDetail d, FeatureEdit[] edits, CaveGraph[] graphs, ObjectInstance[] sites) Make(ulong seed)
    {
        var w = WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = 512, CellMeters = WorldConfig.CellMetersFor(512) }, Biomes, Materials, null, null, Objects, Caves);
        var d = new TerrainDetail(w);
        var edits = Features.Place(w, d, out var graphs, out var sites);
        return (w, d, edits, graphs, sites);
    }

    [Fact]
    public void BreachesPickAStubAndPointOutOfIt()
    {
        int breached = 0;
        for (ulong key = 1; key <= 200; key++)
        {
            var plan = Structures.DungeonLayout(key);
            var breaches = plan.Where(p => p.Breach).ToList();
            Assert.True(breaches.Count <= 1);
            Assert.All(breaches, p => Assert.True(p.Stub && p.Shape == ObjectPart.DungeonCollapse));
            Assert.Equal(breaches.Count == 1, Structures.DungeonBreach(key, out _, out _, out _, out _, out _));
            breached += breaches.Count;
        }
        Assert.InRange(breached, 20, 199);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(3UL)]
    public void LinksJoinDungeonsToWalkableDarkCaves(ulong seed)
    {
        var (w, d, edits, graphs, sites) = Make(seed);
        var links = graphs.Where(DungeonLinks.IsLink).ToArray();
        int dungeons = sites.Count(o => ObjectField.IsDungeon(w.Objects, o)), sockets = sites.Count(o => ObjectField.IsDungeon(w.Objects, o) && ObjectField.BreachOf(w.Objects, o, out _));
        output.WriteLine($"seed {seed}: {dungeons} dungeons, {sockets} breach sockets, {links.Length} links, {edits.Count(e => e.Link)} link carves");
        Assert.NotEmpty(links);
        var e = new List<string>();
        WorldChecks.CheckDungeonLinks(w, d, edits, graphs, sites, e);
        WorldChecks.CheckCaves(w, d, edits, graphs, e);
        Assert.Empty(e);
        var dark = CaveLight.Distances(edits, graphs);
        var bored = graphs.Where(ObjectField.IsTunnel).Select(g => g.Tile).ToHashSet();
        for (int q = 0; q < edits.Length; q++)
            if (edits[q].Link && !bored.Contains(Features.TileOf(edits[q], w.Config.CellMeters)) && !edits.Any(x => !x.Link && x.CaveCarve && Features.TileOf(x, w.Config.CellMeters) == Features.TileOf(edits[q], w.Config.CellMeters)))
                Assert.Equal(CaveLight.Unlit, Math.Min(dark[2 * q], dark[2 * q + 1]));
        Assert.DoesNotContain(new ObjectField(w, d, edits, w.Objects).Discoveries(), o => !sites.Any(s => s.Def == o.Def && s.X == o.X && s.Z == o.Z));
    }

    [Fact]
    public void SurfacedOrStrayLinksAreCaught()
    {
        var (w, d, edits, graphs, sites) = Make(1);
        var link = graphs.First(DungeonLinks.IsLink);
        var bored = graphs.Where(ObjectField.IsTunnel).Select(g => g.Tile).ToHashSet();
        int i = Array.FindIndex(edits, x => x.Link && !bored.Contains(Features.TileOf(x, w.Config.CellMeters)));
        var raised = (FeatureEdit[])edits.Clone();
        raised[i].Ay += 200f;
        raised[i].By += 200f;
        var e = new List<string>();
        WorldChecks.CheckDungeonLinks(w, d, raised, graphs, sites, e);
        Assert.Contains(e, m => m.Contains("opens to the surface"));
        var moved = graphs.Select(g => g == link ? Shift(g) : g).ToArray();
        e.Clear();
        WorldChecks.CheckDungeonLinks(w, d, edits, moved, sites, e);
        Assert.Contains(e, m => m.Contains("breach socket"));
    }

    static CaveGraph Shift(CaveGraph g)
    {
        var c = new CaveGraph { Class = g.Class, Tile = g.Tile };
        c.Nodes.AddRange(g.Nodes.Select(n => new CaveNode { X = n.X + 3f, Y = n.Y, Z = n.Z, Hx = n.Hx, Hz = n.Hz, Entrance = n.Entrance }));
        c.Edges.AddRange(g.Edges);
        return c;
    }
}
