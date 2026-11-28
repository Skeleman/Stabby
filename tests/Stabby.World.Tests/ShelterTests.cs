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
}
