using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class DungeonTests
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(Root);
    static readonly ObjectLibrary Objects = ContentLoader.LoadObjects(Root, Biomes.Select(b => b.Id).ToList());
    static readonly int[] Shapes = { ObjectPart.DungeonCorridor, ObjectPart.DungeonRoom, ObjectPart.DungeonStairs, ObjectPart.DungeonDoor, ObjectPart.DungeonPillar, ObjectPart.DungeonCollapse };

    static (float e, float y)[] Portals(int shape)
    {
        float c = 0.5f * Structures.DungeonSizeM(shape);
        if (shape == ObjectPart.DungeonPillar) return Array.Empty<(float, float)>();
        if (shape == ObjectPart.DungeonStairs) return new[] { (-c, 0f), (c, Structures.KitStoreyM) };
        return new[] { (-c, 0f), (c, 0f) };
    }

    static bool Solid(StructureLayout l, float s, float x, float y, float z)
    {
        const float pad = 0.02f;
        foreach (var p in l.Pieces)
        {
            if (p.Lo != 0 || p.L != 0) continue;
            float dx = x - p.X * s, dz = z - p.Z * s;
            float lx = p.C * dx + p.S * dz, lz = -p.S * dx + p.C * dz;
            if (Math.Abs(lx) <= 0.5f * p.Sx * s + pad && Math.Abs(lz) <= 0.5f * p.Sz * s + pad && y >= p.Y * s - pad && y <= (p.Y + p.Sy) * s + pad) return true;
        }
        return false;
    }

    [Fact]
    public void EveryPortalHasTheSameProfileSoModulesJoinSeamlessly()
    {
        float[] xs = { -1.9f, -1.6f, -1f, -0.5f, 0, 0.5f, 1f, 1.6f, 1.9f }, ys = { -0.3f, -0.1f, 0.2f, 1, 2, 2.6f, 3f, 3.1f };
        float h = 0.5f * Structures.PortalWidthM;
        foreach (int shape in Shapes)
            for (int v = 0; v < 2; v++)
            {
                var l = Structures.Layout(shape, ObjectMeshes.Key("crypt", 0, v), Structures.Iron);
                float s = Structures.DungeonSizeM(shape);
                foreach (var (e, y0) in Portals(shape))
                    foreach (float d in new[] { 0.05f, 0.25f })
                    {
                        float z = e - Math.Sign(e) * d;
                        foreach (float x in xs)
                            foreach (float y in ys)
                            {
                                bool want = Math.Abs(x) > h || y < 0 || y > Structures.PortalHeightM;
                                Assert.True(want == Solid(l, s, x, y0 + y, z), $"shape {shape} v{v} portal {e}: at x {x} y {y} z {z} solid should be {want}");
                            }
                    }
            }
    }

    static ColumnRegions Columns(params (string id, float x, float y, float z)[] at)
    {
        var shapes = PartShapes.Of(Objects);
        var list = new List<ObjectColumn>();
        foreach (var (id, x, y, z) in at)
        {
            int d = Objects.Objects.FindIndex(o => o.Id == id);
            Assert.True(d >= 0, id);
            ObjectField.AddColumns(Objects, shapes, new ObjectInstance { Def = d, Variant = 0, X = x, Y = y, Z = z, Qw = 1, Size = Objects.Objects[d].Placement.SizeMinM }, list);
        }
        return ColumnRegions.Whole(list);
    }

    static (float x, float y, float z) Walk(ColumnRegions cols, float x, float y, float z, float dx, float dz, int steps)
    {
        for (int k = 0; k < steps; k++) Stabby.World.Walk.Step(new Flat(), default(NoEdits), cols, 1, ref x, ref y, ref z, dx, dz, 1);
        return (x, y, z);
    }

    [Fact]
    public void WallsBlockWalkersAndPortalsLetThemThrough()
    {
        float h = 0.5f * Structures.PortalWidthM;
        foreach (var id in new[] { "crypt-corridor", "crypt-door", "crypt-collapse" })
        {
            var cols = Columns((id, 0, 0, 0));
            for (int sg = -1; sg <= 1; sg += 2)
            {
                var side = Walk(cols, 0, 0, sg * 1.5f, sg * 0.1f, 0, 40);
                Assert.True(Math.Abs(side.x) < h, $"{id}: walked through the wall to {side.x:F2}");
            }
            var through = Walk(cols, 0, 0, -1.9f, 0, 0.1f, 80);
            Assert.True(through.z > 3, $"{id}: stopped at {through.z:F2}");
        }
        var room = Columns(("crypt-room", 0, 0, 0));
        foreach (var (dx, dz) in new[] { (1f, 0f), (-1f, 0f), (0.7f, 0.7f), (0.3f, 1f) })
        {
            var p = Walk(room, 0, 0, 0, 0.1f * dx, 0.1f * dz, 120);
            Assert.True(Math.Abs(p.x) < 5.2f && Math.Abs(p.z) < 5.2f, $"room: walked out to {p.x:F2},{p.z:F2}");
        }
        var exit = Walk(room, 0, 0, 0, 0, 0.1f, 120);
        Assert.True(exit.z > 7, $"room: portal stopped the walker at {exit.z:F2}");
    }

    [Fact]
    public void StairsClimbOneStoreyBetweenPortals()
    {
        float c = 0.5f * Structures.StairsM;
        var cols = Columns(("crypt-stairs", 0, 0, 0), ("crypt-corridor", 0, Structures.KitStoreyM, c + 0.5f * Structures.KitCellM));
        float x = 0, y = 0, z = -c + 0.1f;
        for (int k = 0; k < 140; k++) Stabby.World.Walk.Step(new Landing { Z = c, Y = Structures.KitStoreyM }, default(NoEdits), cols, 1, ref x, ref y, ref z, 0, 0.1f, 1);
        var top = (x, y, z);
        Assert.True(top.z > c + 1 && Math.Abs(top.y - Structures.KitStoreyM) < 0.05f, $"stairs: ended at z {top.z:F2} y {top.y:F2}");
        var side = Walk(cols, 0, 0, -c + 0.1f, 0.1f, 0, 40);
        Assert.True(side.x < 0.5f * Structures.PortalWidthM, $"stairs: walked through the wall to {side.x:F2}");
    }

    [Fact]
    public void ModulesAreDeterministicFallPerTierAndStayInTheirCell()
    {
        foreach (int shape in Shapes)
            for (int v = 0; v < 2; v++)
            {
                ulong k = ObjectMeshes.Key("crypt", 0, v);
                var a = Structures.Layout(shape, k, Structures.Iron);
                Assert.Equal(a.Pieces.Count, Structures.Layout(shape, k, Structures.Iron).Pieces.Count);
                Assert.Equal(ObjectMeshes.Build(shape, k, 0, 0, 0, Structures.Iron).Positions, ObjectMeshes.Build(shape, k, 0, 0, 0, Structures.Iron).Positions);
                int t0 = a.Pieces.Count(p => p.Lo == 0), t2 = a.Pieces.Count(p => p.Hi == 2);
                Assert.True(t2 < t0, $"shape {shape}: {t2} far pieces vs {t0} near");
                foreach (var p in a.Pieces)
                    Assert.True(Math.Abs(p.X) <= 0.5f + 1e-3f && Math.Abs(p.Z) <= 0.5f + 1e-3f, $"shape {shape}: piece at {p.X:F3},{p.Z:F3}");
                if (shape == ObjectPart.DungeonRoom || shape == ObjectPart.DungeonDoor || shape == ObjectPart.DungeonPillar) Assert.Contains(a.Pieces, p => p.M == Structures.Iron);
            }
    }

    [Fact]
    public void CryptStyleResolvesStoneAndTrimFromTheLibrary()
    {
        var style = Assert.Single(Objects.Styles, s => s.Id == "crypt");
        var materials = ContentLoader.LoadMaterials(Root);
        Assert.Equal(materials.Find(style.Stone).Color, style.StoneColor);
        foreach (int shape in Shapes)
        {
            var d = Assert.Single(Objects.Objects, o => o.Parts.Count == 1 && o.Parts[0].Shape == shape);
            Assert.Equal("crypt", d.Style);
            Assert.Equal(style.StoneColor, d.Color);
            Assert.Equal(Structures.Iron, d.Parts[0].Trim);
        }
    }

    struct Flat : IGroundHeight
    {
        public float HeightAt(float x, float z) => 0;
    }

    struct Landing : IGroundHeight
    {
        public float Z, Y;
        public float HeightAt(float x, float z) => z > Z ? Y : 0;
    }

    [Fact]
    public void DungeonLayoutsAreConnectedOverlapFreeAndDeterministic()
    {
        int rooms = 0, most = 0;
        for (ulong k = 1; k <= 3000; k++)
        {
            ulong key = Rng.Mix(k * 0x9E3779B97F4A7C15UL);
            Assert.Null(Structures.DungeonLayoutFault(key));
            var a = Structures.DungeonLayout(key);
            var b = Structures.DungeonLayout(key);
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.True(a[i].Shape == b[i].Shape && a[i].X == b[i].X && a[i].Z == b[i].Z && a[i].Open == b[i].Open);
            int n = a.Count(p => p.Shape == ObjectPart.DungeonRoom);
            Assert.InRange(n, 1, Structures.DungeonRoomsMax);
            rooms += n;
            most = Math.Max(most, n);
        }
        Assert.True(rooms >= 3000 * 3, $"layouts average {rooms / 3000f:F1} rooms");
        Assert.True(most >= Structures.DungeonRoomsMin);
    }

    [Fact]
    public void CryptDungeonVariantsHaveSoundLayouts()
    {
        var def = Objects.Objects.First(o => o.Id == "crypt-dungeon");
        Assert.Equal(ObjectField.Dungeon, def.Placement.Discovery);
        for (int v = 0; v < def.Variants; v++) Assert.Null(Structures.DungeonLayoutFault(ObjectMeshes.Key(def.Id, 0, v)));
    }
}
