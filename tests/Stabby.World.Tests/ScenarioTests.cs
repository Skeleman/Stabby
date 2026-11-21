using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class ScenarioTests
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(Root);
    static readonly MaterialLibrary Materials = ContentLoader.LoadMaterials(Root);

    public static TheoryData<string> Names()
    {
        var d = new TheoryData<string>();
        foreach (var s in Scenarios.All) d.Add(s.Name);
        return d;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void ScenarioHolds(string name)
    {
        var s = Scenarios.All.Single(x => x.Name == name);
        var c = new WorldConfig { Seed = s.Seed, Size = Scenarios.Size, CellMeters = WorldConfig.CellMetersFor(Scenarios.Size) };
        var sketch = s.Sketch == null ? null : ContentLoader.LoadSketch(Root, s.Sketch, c);
        var w = WorldGenerator.Generate(c, Biomes, Materials, sketch);
        Assert.Empty(WorldChecks.Run(w));
        Assert.True(s.Find(w) >= 0, $"{name}: seed {s.Seed} no longer shows the scenario; rescan with `stabby scenarios --scan 100` and repin");
    }

    [Fact]
    public void ScenarioNamesAreUnique() =>
        Assert.Equal(Scenarios.All.Length, Scenarios.All.Select(s => s.Name).Distinct().Count());
}
