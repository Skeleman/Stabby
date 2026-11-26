using Stabby.World;

namespace Stabby.World.Tests;

public class RoadTests
{
    [Fact]
    public void RoadsLinkTownsAndPassTheCheck()
    {
        int links = 0;
        foreach (ulong seed in new ulong[] { 2, 3, 6 })
        {
            var (w, _, edits, field) = TestWorlds.Get(seed);
            links += field.Roads().Count(s => s.Kind != RoadSegment.Lane);
            var errors = new List<string>();
            WorldChecks.CheckRoads(w, new EditGrid(edits, w.Config.Size * w.Config.CellMeters), field, errors);
            Assert.True(errors.Count == 0, $"seed {seed}: " + string.Join("; ", errors));
        }
        Assert.True(links > 0, "no road links on three seeds");
    }

    [Fact]
    public void RoadsAreDeterministicAndMatchThePlacedWorld()
    {
        var (w, detail, edits, field) = TestWorlds.Get(6);
        var again = new ObjectField(w, detail, edits, TestWorlds.Objects).Roads();
        Assert.Equal(field.Roads(), again);
        Features.Place(w, detail, out _, out _, out var placed);
        Assert.Equal(field.Roads(), placed);
    }

    [Fact]
    public void RoadTunnelsOnSeed2At1024AreWalkableAndMeetTheirRoads()
    {
        var (w, detail, _, _) = TestWorlds.Get(2, 1024);
        var edits = Features.Place(w, detail, out var caves, out _, out var roads);
        var errors = new List<string>();
        WorldChecks.CheckRoadTunnels(w, detail, edits, caves, roads, errors);
        Assert.True(errors.Count == 0, string.Join("; ", errors));
        Assert.True(roads.Any(s => s.Kind == RoadSegment.Tunnel), "seed 2 at 1024 should bore a tunnel; the default world and most 2048 seeds have none");
    }
}
