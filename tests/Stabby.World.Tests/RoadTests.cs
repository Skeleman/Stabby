using Stabby.World;

namespace Stabby.World.Tests;

public class RoadTests
{
    [Fact]
    public void RoadsLinkTownsAndPassTheCheck()
    {
        var (w, _, edits, field) = TestWorlds.Get(TestWorlds.DefaultSeed);
        int links = field.Roads().Count(s => s.Kind != RoadSegment.Lane);
        var errors = new List<string>();
        WorldChecks.CheckRoads(w, new EditGrid(edits, w.Config.Size * w.Config.CellMeters), field, errors);
        Assert.True(errors.Count == 0, string.Join("; ", errors));
        Assert.True(links > 0, "no road links");
    }

    [Fact]
    public void RoadsAreDeterministicAndMatchThePlacedWorld()
    {
        var (w, detail, edits, field) = TestWorlds.Get(TestWorlds.DefaultSeed);
        var again = new ObjectField(w, detail, edits, TestWorlds.Objects).Roads();
        Assert.Equal(field.Roads(), again);
        Features.Place(w, detail, out _, out _, out var placed);
        Assert.Equal(field.Roads(), placed);
    }
}
