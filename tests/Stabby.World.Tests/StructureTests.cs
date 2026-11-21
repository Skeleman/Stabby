using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class StructureTests
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(Root);
    static readonly ObjectLibrary Objects = ContentLoader.LoadObjects(Root, Biomes.Select(b => b.Id).ToList());
    static readonly int[] Shapes = { ObjectPart.Wall, ObjectPart.Cairn };

    [Fact]
    public void StructureMeshesMergePiecesPerTierAndStayInBounds()
    {
        foreach (int shape in Shapes)
            for (int v = 0; v < 4; v++)
            {
                ulong k = ObjectMeshes.Key("structure", 0, v);
                int prev = int.MaxValue;
                for (int lod = 0; lod < Structures.Tiers; lod++)
                {
                    var m = ObjectMeshes.Build(shape, k, lod);
                    Assert.True(m.TriangleCount > 0 && m.TriangleCount < prev, $"shape {shape} lod {lod}: {m.TriangleCount} after {prev}");
                    Assert.Equal(m.Positions.Length, m.Normals.Length);
                    Assert.All(m.Indices, i => Assert.InRange(i, 0, m.VertexCount - 1));
                    for (int p = 0; p < m.Positions.Length; p += 3)
                    {
                        Assert.InRange(m.Positions[p], -0.56f, 0.56f);
                        Assert.InRange(m.Positions[p + 1], -0.05f, 0.75f);
                        Assert.InRange(m.Positions[p + 2], -0.56f, 0.56f);
                    }
                    prev = m.TriangleCount;
                }
                Assert.Equal(ObjectMeshes.Build(shape, k, 0).Positions, ObjectMeshes.Build(shape, k, 0).Positions);
                Assert.NotEqual(ObjectMeshes.Build(shape, k, 0).Positions, ObjectMeshes.Build(shape, ObjectMeshes.Key("structure", 0, v + 8), 0).Positions);
            }
    }

    [Fact]
    public void StructureColumnsCoverThePiecesInsideTheFootprint()
    {
        foreach (int shape in Shapes)
            for (int v = 0; v < 6; v++)
            {
                var l = Structures.Layout(shape, ObjectMeshes.Key("structure", 0, v));
                Assert.NotEmpty(l.Columns);
                Assert.All(l.Columns, c =>
                {
                    Assert.True(Math.Abs(c.X) + c.R <= 0.56f && Math.Abs(c.Z) + c.R <= 0.56f, $"column at {c.X:F2},{c.Z:F2} r {c.R:F2}");
                    Assert.True(c.Top > c.Bottom);
                });
                Assert.Contains(l.Pieces, p => p.Hi == 0);
                Assert.Contains(l.Pieces, p => p.Lo == 2);
            }
    }

    [Fact]
    public void WallsBlockWalkersThroughSharedColumns()
    {
        int d = Objects.Objects.FindIndex(o => o.Id == "dry-stone-wall");
        var shapes = PartShapes.Of(Objects);
        var o = new ObjectInstance { Def = d, Variant = 0, X = 10, Y = 0, Z = 0, Qy = 0.70710678f, Qw = 0.70710678f, Size = 12 };
        var list = new List<ObjectColumn>();
        ObjectField.AddColumns(Objects, shapes, o, list);
        Assert.True(list.Count > 10);
        Assert.All(list, c => Assert.True(Math.Abs(c.X - 10) < 0.6f * 12 * Structures.WallBase && Math.Abs(c.Z) <= 6.1f, $"{c.X:F2},{c.Z:F2}"));
        Assert.True(list.Max(c => c.Top) > Walk.StepUp);
        var cols = ColumnRegions.Whole(list);
        float x = 6, y = 0, z = 1;
        for (int k = 0; k < 80; k++) Walk.Step(new Flat(), default(NoEdits), cols, 1, ref x, ref y, ref z, 0.1f, 0, 1);
        Assert.True(x < 10 && y < Walk.StepUp, $"walked to {x:F2},{y:F2}");
    }

    [Fact]
    public void StructureRulesAreValidated()
    {
        var bad = new ObjectLibrary();
        float[] lods = { 10f, 50f };
        bad.Kinds.Add(new ObjectKind { Id = "static", Lods = 3, LodPixels = lods });
        bad.Kinds.Add(new ObjectKind { Id = "structure", Lods = 3, LodPixels = lods });
        bad.Objects.Add(new ObjectDef { Id = "a", Kind = "static", Parts = { new ObjectPart { Shape = ObjectPart.Wall } } });
        bad.Objects.Add(new ObjectDef { Id = "b", Kind = "structure", Parts = { new ObjectPart { Shape = ObjectPart.Rock } } });
        var tilted = new ObjectDef { Id = "c", Kind = "structure", Parts = { new ObjectPart { Shape = ObjectPart.Cairn } } };
        tilted.Placement.AlignToSlope = 0.5f;
        bad.Objects.Add(tilted);
        var chainedCairn = new ObjectDef { Id = "d", Kind = "structure", Parts = { new ObjectPart { Shape = ObjectPart.Cairn } } };
        chainedCairn.Placement.ChainMax = 4;
        bad.Objects.Add(chainedCairn);
        var backwards = new ObjectDef { Id = "e", Kind = "structure", Parts = { new ObjectPart { Shape = ObjectPart.Wall } } };
        backwards.Placement.ChainMin = 6;
        backwards.Placement.ChainMax = 3;
        backwards.Placement.ChainGate = 1.5f;
        bad.Objects.Add(backwards);
        var errors = ObjectLibrary.Validate(bad);
        Assert.Contains(errors, e => e.StartsWith("d: chains belong only", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("e: chainMin/chainMax", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("e: chainCorner and chainGate", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("a: structure shapes", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("b: structure shapes", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("c: structures stand level", StringComparison.Ordinal));
        Assert.True(ObjectPart.IsStone(ObjectPart.Cairn) && !ObjectPart.IsFoliage(ObjectPart.Wall));
    }

    [Fact]
    public void PlacedStructuresSitLevelOnTheirSeats()
    {
        var w = WorldGenerator.Generate(new WorldConfig { Seed = 4, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) }, Biomes, ContentLoader.LoadMaterials(Root), null, null, Objects);
        var detail = new TerrainDetail(w);
        var field = new ObjectField(w, detail, Features.Place(w, detail), Objects);
        int seen = 0;
        var walls = new List<ObjectInstance>();
        for (int cz = 0; cz < field.ChunksAcross; cz++)
            for (int cx = 0; cx < field.ChunksAcross; cx++)
                foreach (var o in field.Chunk(cx, cz))
                {
                    var seat = ObjectField.Seats(Objects.Objects[o.Def]);
                    if (seat == null) continue;
                    seen++;
                    if (Objects.Objects[o.Def].Parts[0].Shape == ObjectPart.Wall) walls.Add(o);
                    ObjectField.SeatRange(detail, w.Config.CellMeters, seat, o, out float lo, out float hi);
                    Assert.True(hi - lo <= Structures.SeatM && o.Y <= lo, $"{o.X:F0},{o.Z:F0}: seats {lo:F2}..{hi:F2}, y {o.Y:F2}");
                    Assert.Equal(0f, o.Qx);
                    Assert.Equal(0f, o.Qz);
                }
        Assert.True(seen > 20, $"{seen} structures");
        int linked = walls.Count(a => walls.Count(b => a.Id != b.Id && ObjectIds.Chunk(a.Id) == ObjectIds.Chunk(b.Id)
            && (a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z) <= Math.Pow(Structures.WallSpan * Math.Max(a.Size, b.Size), 2)) >= 2);
        Assert.True(linked > 5, $"{linked} of {walls.Count} wall sections sit inside a run of three");
    }

    struct Flat : IGroundHeight
    {
        public float HeightAt(float x, float z) => 0;
    }
}
