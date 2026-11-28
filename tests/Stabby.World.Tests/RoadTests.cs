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
        WorldChecks.CheckTrails(w, field, errors);
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

    [Fact]
    public void BridgesRefuseDryAndOverlongSpans()
    {
        var (_, _, _, field) = TestWorlds.Get(TestWorlds.DefaultSeed);
        var dry = field.Roads().Where(s => s.Kind == RoadSegment.Dirt).OrderByDescending(s => (s.X1 - s.X0) * (s.X1 - s.X0) + (s.Z1 - s.Z0) * (s.Z1 - s.Z0)).First();
        Assert.NotNull(field.BridgeFault(dry.X0, dry.Z0, dry.X1, dry.Z1, out _, out _, out _));
        float far = TestWorlds.Objects.Roads.SpanM + 10f;
        Assert.Equal("spans too far", field.BridgeFault(dry.X0, dry.Z0, dry.X0 + far, dry.Z0, out _, out _, out _));
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
