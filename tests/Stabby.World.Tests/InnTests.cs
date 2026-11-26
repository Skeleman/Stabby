using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class InnTests
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(Root);
    static readonly ObjectLibrary Objects = ContentLoader.LoadObjects(Root, Biomes.Select(b => b.Id).ToList());
    static readonly int[] Built = { ObjectPart.Inn, ObjectPart.InnYard, ObjectPart.House, ObjectPart.Table, ObjectPart.Bench, ObjectPart.Barrel, ObjectPart.Crate, ObjectPart.Stall, ObjectPart.Well, ObjectPart.StoneRing, ObjectPart.Tent, ObjectPart.FirePit, ObjectPart.LogSeat, ObjectPart.Campsite, ObjectPart.HermitHut, ObjectPart.Hermitage, ObjectPart.RoadShrine, ObjectPart.Wayside, ObjectPart.Ruin, ObjectPart.HotSpring, ObjectPart.Mushroom, ObjectPart.FairyRing, ObjectPart.Geyser, ObjectPart.MudPot, ObjectPart.WolfDen, ObjectPart.WallTorch };

    [Fact]
    public void BuiltMeshesAreDeterministicFallPerTierAndStayInBounds()
    {
        foreach (int shape in Built)
            for (int v = 0; v < 3; v++)
            {
                ulong k = ObjectMeshes.Key("inn", 0, v);
                int prev = int.MaxValue;
                for (int lod = 0; lod < Structures.Tiers; lod++)
                {
                    var m = ObjectMeshes.Build(shape, k, lod);
                    Assert.True(m.TriangleCount > 0 && m.TriangleCount < prev, $"shape {shape} lod {lod}: {m.TriangleCount} after {prev}");
                    Assert.True(m.VertexCount < 65536, $"shape {shape} lod {lod}: {m.VertexCount} vertices");
                    Assert.Equal(4 * m.VertexCount, m.Tones.Length);
                    for (int p = 0; p < m.Positions.Length; p += 3)
                    {
                        Assert.InRange(m.Positions[p], -0.56f, 0.56f);
                        Assert.InRange(m.Positions[p + 1], -0.05f - 0.5f * Structures.SkirtShare, 1.05f);
                        Assert.InRange(m.Positions[p + 2], -0.56f, 0.56f);
                    }
                    prev = m.TriangleCount;
                }
                Assert.Equal(ObjectMeshes.Build(shape, k, 0).Positions, ObjectMeshes.Build(shape, k, 0).Positions);
                Assert.NotEqual(ObjectMeshes.Build(shape, k, 0).Positions, ObjectMeshes.Build(shape, ObjectMeshes.Key("inn", 0, v + 8), 0).Positions);
            }
    }

    [Fact]
    public void InnFarTierIsOneMergedShellAndSeamsDarken()
    {
        var l = Structures.Layout(ObjectPart.Inn, ObjectMeshes.Key("inn", 0, 0));
        Assert.True(l.Pieces.Count(p => p.Lo == 2) <= 12, $"{l.Pieces.Count(p => p.Lo == 2)} far pieces");
        Assert.Contains(l.Pieces, p => p.M == Structures.Thatch);
        Assert.Contains(l.Pieces, p => p.M == Structures.Iron && p.Hi == 0);
        var m = ObjectMeshes.Build(ObjectPart.Inn, ObjectMeshes.Key("inn", 0, 0), 0);
        int shut = 0, n = m.VertexCount;
        for (int v = 0; v < n; v++)
        {
            float t = m.Tones[4 * v];
            float open = t - 2 * (float)Math.Floor(t * 0.5f);
            if (t > 1.75f && open < 0.85f) shut++;
        }
        Assert.True(shut > n / 20, $"{shut} of {n} built vertices darkened");
    }

    [Fact]
    public void SiteKeepsTheInnPartWhereTheYardExpectsIt()
    {
        var d = Objects.Objects.Single(o => o.Id == "inn");
        Assert.Equal(ObjectPart.Inn, d.Parts[0].Shape);
        Assert.Equal(ObjectPart.InnYard, d.Parts[1].Shape);
        Assert.Equal(Structures.SiteInnZ, d.Parts[0].OffsetZ);
        Assert.Equal(Structures.SiteInnScale, d.Parts[0].ScaleX);
        Assert.Equal(Structures.SiteInnScale, d.Parts[0].ScaleZ);
        foreach (var id in new[] { "table", "bench", "barrel", "crate", "market-stall", "well" })
            Assert.Contains(Objects.Objects, o => o.Id == id && o.Kind == "structure");
    }

    [Fact]
    public void InnWallsBlockWalkersButTheDoorLetsThemIn()
    {
        int d = Objects.Objects.FindIndex(o => o.Id == "inn");
        var shapes = PartShapes.Of(Objects);
        var o = new ObjectInstance { Def = d, Variant = 0, X = 0, Y = 0, Z = 0, Qw = 1, Size = 19 };
        var list = new List<ObjectColumn>();
        ObjectField.AddColumns(Objects, shapes, o, list);
        var cols = ColumnRegions.Whole(list);
        int entered = 0;
        for (float sx = -4.5f; sx <= 4.5f; sx += 0.25f)
        {
            float x = sx, y = 0, z = 5;
            for (int k = 0; k < 70; k++) Walk.Step(new Flat(), default(NoEdits), cols, 1, ref x, ref y, ref z, 0, -0.1f, 1);
            if (z < 0) entered++;
        }
        Assert.InRange(entered, 1, 8);
        for (float sx = -4.5f; sx <= 4.5f; sx += 0.5f)
        {
            float x = sx, y = 0, z = -9;
            for (int k = 0; k < 60; k++) Walk.Step(new Flat(), default(NoEdits), cols, 1, ref x, ref y, ref z, 0, 0.1f, 1);
            Assert.True(z < -5, $"walked from the back to {x:F2},{z:F2}");
        }
        for (float sz = -4.5f; sz <= 0.5f; sz += 0.5f)
        {
            float x = 10, y = 0, z = sz;
            for (int k = 0; k < 60; k++) Walk.Step(new Flat(), default(NoEdits), cols, 1, ref x, ref y, ref z, -0.1f, 0, 1);
            Assert.True(x > 5, $"walked from the side to {x:F2},{z:F2}");
        }
    }

    [Fact]
    public void InnsPlaceLevelInTheTestWorld()
    {
        var (w, detail, _, field) = TestWorlds.Get(4);
        int d = Objects.Objects.FindIndex(o => o.Id == "inn"), seen = 0;
        var seat = ObjectField.Seats(Objects.Objects[d]);
        for (int cz = 0; cz < field.ChunksAcross; cz++)
            for (int cx = 0; cx < field.ChunksAcross; cx++)
                foreach (var o in field.Place(cx, cz, true))
                {
                    if (o.Def != d) continue;
                    seen++;
                    ObjectField.SeatRange(detail, w.Config.CellMeters, seat, o, out float lo, out float hi);
                    Assert.True(hi - lo <= Structures.SeatM && o.Y <= lo, $"{o.X:F0},{o.Z:F0}: seats {lo:F2}..{hi:F2}, y {o.Y:F2}");
                }
        Assert.True(seen > 0, "no inn placed");
    }

    struct Flat : IGroundHeight
    {
        public float HeightAt(float x, float z) => 0;
    }
}
