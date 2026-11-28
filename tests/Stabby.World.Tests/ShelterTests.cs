using Stabby.World;

namespace Stabby.World.Tests;

public class ShelterTests
{
    struct Flat : IGroundHeight
    {
        public float HeightAt(float x, float z) => 10;
    }

    static readonly EditArray Edits = new EditArray
    {
        Items = new[]
        {
            new FeatureEdit { Kind = FeatureEdit.Add, Ax = 0, Ay = 16, Az = 0, Bx = 20, By = 16, Bz = 0, R = 2 },
            new FeatureEdit { Kind = FeatureEdit.Carve, Ax = 40, Ay = 4, Az = 0, Bx = 60, By = 4, Bz = 0, R = 3 },
        }
    };

    static bool Covered(float x, float y, float z) => Shelter.Covered(new Flat(), Edits, Features.LatticeStepM, x, y, z);

    [Fact]
    public void OverhangAndCaveAreShelteredOpenGroundIsNot()
    {
        Assert.True(Covered(10, 11, 0));
        Assert.True(Covered(50, 4, 0));
        Assert.False(Covered(10, 11, 20));
        Assert.False(Covered(-30, 11, 0));
        Assert.False(Covered(10, 20, 0));
    }

    [Fact]
    public void FloorIsTheTopmostSolidUnderTheReach()
    {
        float under = Shelter.Floor(new Flat(), Edits, Features.LatticeStepM, 10, 0, 40, -20);
        Assert.InRange(under, 17.5f, 18.5f);
        Assert.Equal(10f, Shelter.Floor(new Flat(), Edits, Features.LatticeStepM, 50, 0, 40, -20), 1);
        Assert.Equal(10f, Shelter.Floor(new Flat(), Edits, Features.LatticeStepM, 10, 20, 40, -20), 1);
    }
    static ObjectDef Def(int shape, float sx = 1) => new ObjectDef { Parts = { new ObjectPart { Shape = shape, ScaleX = sx, ScaleY = 1, ScaleZ = sx } } };

    static RoofArray Roofs(params Roof[] r) => new RoofArray { Items = r };

    [Fact]
    public void TreeCrownShelters()
    {
        var t = Shelter.Template(Def(ObjectPart.Broadleaf));
        Assert.True(t.Valid);
        var roofs = Roofs(Shelter.Place(t, new ObjectInstance { X = 100, Y = 10, Z = 0, Qw = 1, Size = 12 }));
        Assert.True(Shelter.Covered(new Flat(), Edits, roofs, Features.LatticeStepM, 101, 11.6f, 1));
        Assert.False(Shelter.Covered(new Flat(), Edits, roofs, Features.LatticeStepM, 110, 11.6f, 0));
        Assert.False(Shelter.Covered(new Flat(), Edits, roofs, Features.LatticeStepM, 101, 18f, 1));
        Assert.Equal(10f + 0.45f * 12, Shelter.Floor(new Flat(), Edits, roofs, Features.LatticeStepM, 100, 0, 40, -20), 3);
    }

    [Fact]
    public void HouseRoofFollowsYawAndOpenGroundStaysOpen()
    {
        var t = Shelter.Template(Def(ObjectPart.House));
        float h = 0.70710678f;
        var roofs = Roofs(Shelter.Place(t, new ObjectInstance { X = 200, Y = 10, Z = 0, Qy = h, Qw = h, Size = 8 }));
        Assert.True(Shelter.Covered(new Flat(), Edits, roofs, Features.LatticeStepM, 200, 11.6f, 3));
        Assert.False(Shelter.Covered(new Flat(), Edits, roofs, Features.LatticeStepM, 203, 11.6f, 0));
        Assert.False(Shelter.Covered(new Flat(), Edits, roofs, Features.LatticeStepM, 220, 11.6f, 0));
        Assert.False(Shelter.Template(Def(ObjectPart.Rock)).Valid);
    }
}
