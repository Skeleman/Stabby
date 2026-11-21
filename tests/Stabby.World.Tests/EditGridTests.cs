using System.Collections.Generic;
using Stabby.World;

namespace Stabby.World.Tests;

public class EditGridTests
{
    struct Slope : IGroundHeight
    {
        public float HeightAt(float x, float z) => 0.02f * x - 0.01f * z;
    }

    static float Hash(uint i, uint salt)
    {
        uint h = i * 0x9E3779B1u ^ salt * 0x85EBCA77u;
        h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
        return (h & 0xFFFF) / 65535f;
    }

    static FeatureEdit[] Edits(int count, float worldM)
    {
        var e = new FeatureEdit[count];
        for (uint q = 0; q < count; q++)
        {
            float x = Hash(q, 1) * worldM, z = Hash(q, 2) * worldM, len = 20 * Hash(q, 3);
            e[q] = new FeatureEdit
            {
                Kind = Hash(q, 4) < 0.5f ? FeatureEdit.Add : FeatureEdit.Carve,
                Shape = Hash(q, 5) < 0.5f ? FeatureEdit.Boulder : FeatureEdit.Cave,
                R = 1 + 9 * Hash(q, 6),
                Ax = x, Ay = 2 * Hash(q, 7), Az = z,
                Bx = x + len, By = 2 * Hash(q, 8), Bz = z + len * (Hash(q, 9) - 0.5f),
            };
        }
        return e;
    }

    [Fact]
    public void CellListMatchesFullListForWalk()
    {
        const float worldM = 600;
        var edits = Edits(300, worldM);
        var grid = new EditGrid(edits, worldM);
        var all = new EditArray { Items = edits };
        for (uint p = 0; p < 4000; p++)
        {
            float x = Hash(p, 11) * worldM, z = Hash(p, 12) * worldM, y = 12 * Hash(p, 13) - 4;
            var cell = grid.Cell(x, z);
            float dx = (Hash(p, 14) - 0.5f) * (EditGrid.PadM - Walk.Radius), dz = (Hash(p, 15) - 0.5f) * (EditGrid.PadM - Walk.Radius);
            Assert.Equal(Walk.Solid(new Slope(), all, 2, x + dx, y, z + dz), Walk.Solid(new Slope(), cell, 2, x + dx, y, z + dz));
            float ax = x, ay = y + 2, az = z, bx = x, by = y + 2, bz = z;
            bool ma = Walk.Step(new Slope(), all, 2, ref ax, ref ay, ref az, 0.5f * dx, 0.5f * dz, 1);
            bool mb = Walk.Step(new Slope(), cell, 2, ref bx, ref by, ref bz, 0.5f * dx, 0.5f * dz, 1);
            Assert.Equal((ma, ax, ay, az), (mb, bx, by, bz));
        }
    }

    [Fact]
    public void NearFindsEveryOverlapInOrder()
    {
        const float worldM = 600;
        var edits = Edits(300, worldM);
        var grid = new EditGrid(edits, worldM);
        var ids = new List<int>();
        for (uint p = 0; p < 200; p++)
        {
            float x0 = Hash(p, 21) * worldM, z0 = Hash(p, 22) * worldM, x1 = x0 + 150 * Hash(p, 23), z1 = z0 + 150 * Hash(p, 24);
            grid.Near(x0, z0, x1, z1, ids);
            var want = new List<int>();
            for (int q = 0; q < edits.Length; q++)
                if (edits[q].MaxX > x0 && edits[q].MinX < x1 && edits[q].MaxZ > z0 && edits[q].MinZ < z1) want.Add(q);
            Assert.Equal(want, ids);
        }
    }

    [Fact]
    public void SmallBouldersFadeKeepingTheirTop()
    {
        var b = new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Boulder, R = 3, Ax = 10, Ay = 5, Az = 10, Bx = 14, By = 5, Bz = 10 };
        Assert.Equal(b.R, b.AtStep(4).R);
        var f = b.AtStep(8);
        Assert.Equal(6, f.R, 3);
        Assert.Equal(b.MaxY, f.MaxY, 3);
        Assert.True(b.MeshedIn(8, 0, 0, 20, 20));
        Assert.False(b.MeshedIn(16, 0, 0, 20, 20));
        var cave = b;
        cave.Shape = FeatureEdit.Cave;
        Assert.False(cave.MeshedIn(8, 0, 0, 20, 20));
    }
}
