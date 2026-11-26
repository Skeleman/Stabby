using System;
using System.Collections.Generic;
using System.Linq;
using Stabby.Content;
using Stabby.World;
using Xunit;
using Xunit.Abstractions;

namespace Stabby.World.Tests;

public class MineTests
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(Root);
    static readonly MaterialLibrary Materials = ContentLoader.LoadMaterials(Root);
    static readonly CaveLibrary Caves = ContentLoader.LoadCaves(Root);
    static readonly ObjectLibrary Objects = ContentLoader.LoadObjects(Root, Biomes.Select(b => b.Id).ToList());
    readonly ITestOutputHelper output;
    public MineTests(ITestOutputHelper o) { output = o; }

    static (GeneratedWorld w, TerrainDetail d, FeatureEdit[] edits, CaveGraph[] graphs) Make(ulong seed)
    {
        var w = WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = 512, CellMeters = WorldConfig.CellMetersFor(512) }, Biomes, Materials, null, null, Objects, Caves);
        var d = new TerrainDetail(w);
        var edits = Features.Place(w, d, out var graphs);
        return (w, d, edits.ToArray(), graphs.ToArray());
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(5UL)]
    public void MinesAreWalkableGridAlignedAndTimbered(ulong seed)
    {
        var (w, d, edits, graphs) = Make(seed);
        var mines = graphs.Where(Mines.IsMine).ToArray();
        Assert.InRange(mines.Length, 1, Mines.Most);
        Assert.All(mines, m => Assert.True(m.Nodes[0].Entrance));
        Assert.Contains(edits, e => e.Shape == FeatureEdit.Mine && Mines.Vertical(e));
        var e = new List<string>();
        WorldChecks.CheckCaves(w, d, edits, graphs, e);
        WorldChecks.CheckMines(w, d, edits, graphs, e);
        Assert.Empty(e);
        var field = new ObjectField(w, d, edits, w.Objects);
        var counts = new int[3];
        var contacts = new List<ObjectColumn>();
        var chunks = new HashSet<int>();
        float span = field.ChunkMeters;
        int across = field.ChunksAcross;
        foreach (var x in edits.Where(x => x.Shape == FeatureEdit.Mine))
            for (float t = 0; t <= 1f; t += 0.25f)
            {
                float px = x.Ax + (x.Bx - x.Ax) * t, pz = x.Az + (x.Bz - x.Az) * t;
                chunks.Add(Math.Min(across - 1, (int)(pz / span)) * across + Math.Min(across - 1, (int)(px / span)));
            }
        foreach (int c in chunks)
            foreach (var o in field.Chunk(c % across, c / across))
            {
                var def = w.Objects.Objects[o.Def];
                if (!ObjectField.IsMineKind(def.Placement.Cave)) continue;
                counts[def.Placement.Cave - ObjectField.CaveMineTimber]++;
                Assert.Null(field.CaveFault(o));
                Assert.Null(field.ContactFault(o, contacts));
            }
        output.WriteLine($"seed {seed}: {mines.Length} mines, {counts[0]} timbers, {counts[1]} rails, {counts[2]} carts");
        Assert.True(counts[0] > 0 && counts[1] > 0, "mines need timbers and rails");
    }

    [Fact]
    public void FloatingTimberIsCaught()
    {
        var (w, d, edits, graphs) = Make(1);
        var field = new ObjectField(w, d, edits, w.Objects);
        var run = edits.First(x => x.Shape == FeatureEdit.Mine && !Mines.Vertical(x) && Math.Abs(x.Bx - x.Ax) + Math.Abs(x.Bz - x.Az) > 6f);
        int def = w.Objects.Objects.FindIndex(o => o.Placement.Cave == ObjectField.CaveMineTimber);
        var o = new ObjectInstance { Def = def, X = 0.5f * (run.Ax + run.Bx), Y = run.Ay + 0.5f, Z = 0.5f * (run.Az + run.Bz), Size = 4f, Qw = 1f };
        Assert.NotNull(field.CaveFault(o));
    }
}
