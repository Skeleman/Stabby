using Stabby.World;

namespace Stabby.World.Tests;

public class CameraBoomTests
{
    struct Flat : IGroundHeight
    {
        public float HeightAt(float x, float z) => 10;
    }

    struct OneRock : IColumnTable
    {
        public float CellM => 100;
        public float RegionM => 100;
        public int Across => 1;
        public bool Grid(int region, out RegionGrid g) { g = new RegionGrid { X0 = 0, Z0 = 0, W = 1, H = 1, Max = 3 }; return true; }
        public int Start(int region, int cell) => cell <= 0 ? 0 : 1;
        public ObjectColumn Column(int region, int k) => new ObjectColumn { X = 50, Z = 56, R = 3, Bottom = 10, Top = 20 };
    }

    static readonly EditArray Edits = new EditArray
    {
        Items = new[] { new FeatureEdit { Kind = FeatureEdit.Add, Ax = 40, Ay = 16, Az = 40, Bx = 60, By = 16, Bz = 40, R = 3 } }
    };

    static float Clear<TO>(TO o, EditArray e, float x, float y, float z, float bx, float by, float bz) where TO : struct, IColumnTable
        => CameraBoom.Clear(new Flat(), (_, _) => e, o, Features.LatticeStepM, x, y, z, bx, by, bz, 14f, 1.5f, 0.6f, 48);

    [Fact]
    public void OpenGroundGivesFullBoom()
        => Assert.Equal(14f, Clear(default(NoColumns), default, 50, 14, 40, 0, 0, 1));

    [Fact]
    public void PlacedObjectPullsBoomIn()
    {
        float b = Clear(new OneRock(), default, 50, 14, 40, 0, 0, 1);
        Assert.InRange(b, 10f, 13.1f);
    }

    [Fact]
    public void OverhangPullsBoomIn()
    {
        float b = Clear(default(NoColumns), Edits, 50, 12, 28, 0, 0.3f, 1);
        Assert.InRange(b, 1.5f, 11f);
        Assert.True(b < 14f);
    }
}
