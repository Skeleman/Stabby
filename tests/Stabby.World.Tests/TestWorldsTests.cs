using Stabby.World;

namespace Stabby.World.Tests;

public class TestWorldsTests
{
    [Fact]
    public void DefaultHasCavesRiversTownInnDungeonAndMine()
    {
        var p = TestWorlds.Default;
        var w = p.W;
        var f = p.Field;
        int inn = TestWorlds.Objects.Objects.FindIndex(o => o.Id == "inn");
        int inns = Enumerable.Range(0, f.ChunksAcross * f.ChunksAcross).Sum(c => f.Chunk(c % f.ChunksAcross, c / f.ChunksAcross).Count(o => o.Def == inn));
        Assert.True(p.Graphs.Any(g => !Mines.IsMine(g) && !DungeonLinks.IsLink(g)), "no natural cave");
        Assert.True(p.Graphs.Any(g => g.Class == "labyrinth"), "no labyrinth for CarvedMeshTests");
        Assert.True(w.Water.Rivers.Count > 0, "no river");
        Assert.True(f.TownPlans().Length > 0, "no town");
        Assert.True(inns > 0, "no inn");
        Assert.True(p.Sites.Any(o => ObjectField.IsDungeon(w.Objects, o)), "no dungeon");
        Assert.True(p.Graphs.Any(Mines.IsMine), "no mine");
        Assert.True(p.Graphs.Any(DungeonLinks.IsLink), "no dungeon link");
    }
}
