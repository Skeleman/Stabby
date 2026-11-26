using Stabby.World;

namespace Stabby.World.Tests;

public class TownTests
{
    static readonly ObjectLibrary Objects = TestWorlds.Objects;

    [Fact]
    public void TownsPlaceTheirSitesAndPassTheCheck()
    {
        var (w, _, edits, field) = TestWorlds.Get(TestWorlds.DefaultSeed);
        var plans = field.TownPlans();
        Assert.True(plans.Length <= field.Towns.Count, $"{plans.Length} towns from {field.Towns.Count} landmarks");
        var errors = new List<string>();
        WorldChecks.CheckTowns(w, new EditGrid(edits, w.Config.Size * w.Config.CellMeters), field, errors);
        Assert.True(errors.Count == 0, string.Join("; ", errors));
        foreach (var p in plans)
        {
            Assert.Contains(p.Members, m => Objects.Objects[m.Def].Id == "well");
            Assert.True(p.Members.Count(m => Objects.Objects[m.Def].Id == "house") >= 2, $"town at {p.X:F0},{p.Z:F0} has {p.Members.Count} sites");
        }
        Assert.True(plans.Length >= 4, $"{plans.Length} towns");
    }

    [Fact]
    public void HouseDoorsFaceTheLane()
    {
        var (_, _, _, field) = TestWorlds.Get(TestWorlds.DefaultSeed);
        int house = Objects.Objects.FindIndex(o => o.Id == "house"), seen = 0;
        foreach (var p in field.TownPlans())
            foreach (var m in p.Members)
            {
                if (m.Def != house) continue;
                seen++;
                float fx = 2 * (m.Qx * m.Qz + m.Qw * m.Qy), fz = 1 - 2 * (m.Qx * m.Qx + m.Qy * m.Qy);
                float side = -p.Uz * (m.X - p.X) + p.Ux * (m.Z - p.Z);
                float toward = -(-p.Uz * fx + p.Ux * fz) * Math.Sign(side);
                Assert.True(toward > 0.9f, $"house at {m.X:F0},{m.Z:F0} faces {fx:F2},{fz:F2}, lane {p.Ux:F2},{p.Uz:F2}");
            }
        Assert.True(seen > 0, "no house placed");
    }

    [Fact]
    public void TownsAreDeterministicAcrossFields()
    {
        var (w, detail, edits, field) = TestWorlds.Get(TestWorlds.DefaultSeed);
        var again = new ObjectField(w, detail, edits, Objects).TownPlans();
        var plans = field.TownPlans();
        Assert.Equal(plans.Length, again.Length);
        for (int k = 0; k < plans.Length; k++)
            Assert.Equal(plans[k].Members.Select(m => (m.Def, m.X, m.Z, m.Size, m.Variant)), again[k].Members.Select(m => (m.Def, m.X, m.Z, m.Size, m.Variant)));
    }
}
