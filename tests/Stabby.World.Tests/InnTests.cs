using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class InnTests : IClassFixture<DefaultWorldFixture>
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(Root);
    static readonly ObjectLibrary Objects = ContentLoader.LoadObjects(Root, Biomes.Select(b => b.Id).ToList());
    static readonly int[] Built = { ObjectPart.Inn, ObjectPart.InnYard, ObjectPart.House, ObjectPart.Table, ObjectPart.Bench, ObjectPart.Barrel, ObjectPart.Crate, ObjectPart.Stall, ObjectPart.Well, ObjectPart.StoneRing, ObjectPart.Tent, ObjectPart.FirePit, ObjectPart.LogSeat, ObjectPart.Campsite, ObjectPart.HermitHut, ObjectPart.Hermitage, ObjectPart.RoadShrine, ObjectPart.Wayside, ObjectPart.Ruin, ObjectPart.HotSpring, ObjectPart.Mushroom, ObjectPart.FairyRing, ObjectPart.Geyser, ObjectPart.MudPot, ObjectPart.WolfDen, ObjectPart.Overlook, ObjectPart.SaltTower, ObjectPart.FallenTree, ObjectPart.Monument, ObjectPart.Shipwreck, ObjectPart.Watchtower, ObjectPart.SunkenTemple, ObjectPart.FumaroleField, ObjectPart.InnRoadside, ObjectPart.WallTorch };

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
    public void FallenTreeHollowLetsWalkersThroughButNotAcross()
    {
        int d = Objects.Objects.FindIndex(o => o.Id == "fallen-tree");
        var shapes = PartShapes.Of(Objects);
        var o = new ObjectInstance { Def = d, Variant = 0, X = 0, Y = 0, Z = 0, Qw = 1, Size = 18 };
        var list = new List<ObjectColumn>();
        ObjectField.AddColumns(Objects, shapes, o, list);
        var cols = ColumnRegions.Whole(list);
        float x = -14, y = 0, z = 0;
        for (int k = 0; k < 300; k++) Walk.Step(new Flat(), default(NoEdits), cols, 1, ref x, ref y, ref z, 0.1f, 0, 1);
        Assert.True(x > 10, $"stuck in the hollow at {x:F2},{z:F2}");
        for (float sx = -6f; sx <= 6f; sx += 1f)
        {
            x = sx; y = 0; z = 5;
            for (int k = 0; k < 100; k++) Walk.Step(new Flat(), default(NoEdits), cols, 1, ref x, ref y, ref z, 0, -0.1f, 1);
            Assert.True(z > 1, $"walked over the trunk to {x:F2},{z:F2}");
        }
    }

    [Fact]
    public void FallenTreeFloorAndTrunkHoldWalkersAtMeshHeight()
    {
        int d = Objects.Objects.FindIndex(o => o.Id == "fallen-tree");
        var def = Objects.Objects[d];
        var shapes = PartShapes.Of(Objects);
        for (int v = 0; v < def.Variants; v++)
        {
            var o = new ObjectInstance { Def = d, Variant = v, X = 0, Y = 0, Z = 0, Qw = 1, Size = 17 + v % 3 };
            var list = new List<ObjectColumn>();
            ObjectField.AddColumns(Objects, shapes, o, list);
            var cols = ColumnRegions.Whole(list);
            var mesh = ObjectMeshes.Build(def.Parts[0], ObjectMeshes.Key(def.Id, 0, o.Variant), 0);
            float u = o.Size * Structures.TreeU;
            float tip = 4;
            while (MeshTop(mesh, (tip + 0.25f) * u / o.Size, 0, float.MaxValue) > 0) tip += 0.25f;
            Assert.True(tip > 7, $"v{v}: trunk ends at {tip:F2}");
            foreach (float sx in new[] { -5f, -2f, 1f, 4f, tip - 0.5f, tip })
            {
                float floor = Math.Max(0, Under(mesh, sx * u, 1.2f, o.Size)), top = Under(mesh, sx * u, float.MaxValue, o.Size);
                if (sx <= 4) Assert.True(Math.Abs(MeshTop(mesh, sx * u / o.Size, 0, 1.2f / o.Size) * o.Size - Structures.TreeFloorM * u) <= 0.05f, $"hollow floor mesh v{v} at {sx}");
                float x = sx * u, y = 0.1f, z = 0;
                Walk.Step(new Flat(), default(NoEdits), cols, 1, ref x, ref y, ref z, 0, 0, 1);
                Assert.True(Math.Abs(y - floor) <= 0.05f, $"inside v{v} at {sx}: walker {y:F2}, floor {floor:F2}");
                x = sx * u; y = top + 0.5f; z = 0;
                Walk.Step(new Flat(), default(NoEdits), cols, 1, ref x, ref y, ref z, 0, 0, 1);
                Assert.True(Math.Abs(y - top) <= 0.05f, $"on top v{v} at {sx}: walker {y:F2}, trunk {top:F2}");
            }
        }
    }

    static float Under(ObjectMesh m, float x, float below, float size)
    {
        float best = float.NegativeInfinity;
        for (float dx = -Walk.Radius; dx <= Walk.Radius; dx += 0.1f)
            for (float dz = -Walk.Radius; dz <= Walk.Radius; dz += 0.1f)
                if (dx * dx + dz * dz <= Walk.Radius * Walk.Radius) best = Math.Max(best, MeshTop(m, (x + dx) / size, dz / size, below / size) * size);
        return best;
    }

    static float MeshTop(ObjectMesh m, float px, float pz, float below)
    {
        float best = float.NegativeInfinity;
        var p = m.Positions;
        for (int t = 0; t < m.Indices.Length; t += 3)
        {
            int a = 3 * m.Indices[t], b = 3 * m.Indices[t + 1], c = 3 * m.Indices[t + 2];
            float ax = p[a] - px, az = p[a + 2] - pz, bx = p[b] - px, bz = p[b + 2] - pz, cx = p[c] - px, cz = p[c + 2] - pz;
            float wa = bx * cz - bz * cx, wb = cx * az - cz * ax, wc = ax * bz - az * bx, sum = wa + wb + wc;
            if (Math.Abs(sum) < 1e-12f || (wa < 0 || wb < 0 || wc < 0) && (wa > 0 || wb > 0 || wc > 0)) continue;
            float y = (wa * p[a + 1] + wb * p[b + 1] + wc * p[c + 1]) / sum;
            if (y < below && y > best) best = y;
        }
        return best;
    }

    [Fact]
    public void InnsPlaceLevelInTheTestWorld()
    {
        var (w, detail, _, field) = TestWorlds.Get(TestWorlds.DefaultSeed);
        int d = Objects.Objects.FindIndex(o => o.Id == "inn"), seen = 0;
        var seat = ObjectField.Seats(Objects.Objects[d]);
        int across = field.ChunksAcross;
        for (int c = 0; c < across * across; c++)
            foreach (var o in field.Sites(c % across, c / across))
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
