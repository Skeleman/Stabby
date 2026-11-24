using Stabby.World;

namespace Stabby.World.Tests;

public class SurfaceNetsTests
{
    struct HashColumns : IColumnField
    {
        public uint Seed;
        public float Scale;

        public float Height(int i, int k)
        {
            uint h = (uint)i * 0x9E3779B1u ^ (uint)k * 0x85EBCA77u ^ Seed;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return (h & 0xFFFF) / 65535f * Scale - Scale * 0.3f;
        }
    }

    struct FlatColumns : IColumnField
    {
        public float Height(int i, int k) => 0.5f;
    }

    static (NetsBuffers general, NetsBuffers fast) Both<T>(T cols, NetsChunk c) where T : struct, IColumnField
    {
        SurfaceNets.HeightRange(cols, c, out int jMin, out int jMax);
        var g = new NetsBuffers();
        SurfaceNets.General(new HeightSolid<T> { Columns = cols, StepM = c.StepM }, c, jMin, jMax, ref g);
        var f = new NetsBuffers();
        SurfaceNets.Heightfield(cols, c, ref f);
        return (g, f);
    }

    static void AssertSame(NetsBuffers g, NetsBuffers f)
    {
        Assert.NotEmpty(f.Tris);
        Assert.True(f.SameAs(g));
    }

    [Theory]
    [InlineData(1u, 3f, 0, 0)]
    [InlineData(2u, 40f, -7, 13)]
    [InlineData(3u, 400f, 101, -55)]
    public void HeightfieldPathMatchesGeneralOnRoughFields(uint seed, float scale, int x0, int z0)
    {
        var (g, f) = Both(new HashColumns { Seed = seed, Scale = scale }, new NetsChunk { X0 = x0, Z0 = z0, Cells = 12, StepM = 2.5f });
        AssertSame(g, f);
    }

    [Fact]
    public void EditedPathMatchesGeneralWithCarveAndAdd()
    {
        var cols = new HashColumns { Seed = 9, Scale = 6f };
        var edits = new EditArray
        {
            Items = new[]
            {
                new FeatureEdit { Kind = FeatureEdit.Carve, Ax = 8, Ay = 2, Az = 10, Bx = 22, By = 3, Bz = 14, R = 5 },
                new FeatureEdit { Kind = FeatureEdit.Add, Ax = 6, Ay = 4, Az = 6, Bx = 18, By = 16, Bz = 20, R = 4 },
            },
        };
        var c = new NetsChunk { X0 = 0, Z0 = 0, Cells = 14, StepM = 2f };
        var g = new NetsBuffers();
        SurfaceNets.General(new EditedSolid<HashColumns, EditArray> { Columns = cols, Edits = edits, StepM = c.StepM }, c, -6, 16, ref g);
        var f = new NetsBuffers();
        SurfaceNets.Edited(cols, edits, c, ref f);
        var h = new NetsBuffers();
        SurfaceNets.Heightfield(cols, c, ref h);
        Assert.True(f.SameSurfaceAs(g));
        Assert.False(f.SameSurfaceAs(h));
    }

    [Fact]
    public void EditedPathMatchesGeneralUnderDeepGround()
    {
        var cols = new HashColumns { Seed = 4, Scale = 8f };
        var deep = new DeepColumns { Inner = cols };
        var edits = new EditArray
        {
            Items = new[]
            {
                new FeatureEdit { Kind = FeatureEdit.Carve, Ax = 2, Ay = 4, Az = 4, Bx = 20, By = 8, Bz = 18, R = 4, P = 0.3f, N = 0.5f, S = 0.3f },
                new FeatureEdit { Kind = FeatureEdit.Carve, Ax = 10, Ay = 20, Az = 0, Bx = 12, By = 52, Bz = 22, R = 3 },
                new FeatureEdit { Kind = FeatureEdit.Add, Ax = 6, Ay = 6, Az = 6, Bx = 8, By = 7, Bz = 9, R = 2 },
            },
        };
        var c = new NetsChunk { X0 = -2, Z0 = 1, Cells = 14, StepM = 1f };
        SurfaceNets.HeightRange(deep, c, out _, out int jMax);
        var g = new NetsBuffers();
        SurfaceNets.General(new EditedSolid<DeepColumns, EditArray> { Columns = deep, Edits = edits, StepM = c.StepM }, c, -4, jMax, ref g);
        var f = new NetsBuffers();
        SurfaceNets.Edited(deep, edits, c, ref f);
        Assert.True(f.SameSurfaceAs(g));
    }

    struct DeepColumns : IColumnField
    {
        public HashColumns Inner;
        public float Height(int i, int k) => 60f + Inner.Height(i, k);
    }

    [Fact]
    public void FlatGroundFacesUp()
    {
        var f = new NetsBuffers();
        SurfaceNets.Heightfield(new FlatColumns(), new NetsChunk { X0 = 0, Z0 = 0, Cells = 4, StepM = 1f }, ref f);
        Assert.Equal(4 * 4 * 2 * 3, f.Tris.Count);
        for (int t = 0; t < f.Tris.Count; t += 3)
        {
            var (a, b, c) = (f.Tris[t] * 6, f.Tris[t + 1] * 6, f.Tris[t + 2] * 6);
            float ux = f.Verts[b] - f.Verts[a], uz = f.Verts[b + 2] - f.Verts[a + 2];
            float vx = f.Verts[c] - f.Verts[a], vz = f.Verts[c + 2] - f.Verts[a + 2];
            Assert.True(uz * vx - ux * vz > 0, $"triangle {t / 3} faces down");
            Assert.True(f.Verts[a + 4] > 0.99f);
        }
    }

    [Fact]
    public void AdjacentChunksCloseEveryEdge()
    {
        var cols = new HashColumns { Seed = 5, Scale = 30f };
        var edges = new Dictionary<(long, long), int>();
        var pos = new Dictionary<long, (float x, float z)>();
        for (int cz = 0; cz < 2; cz++)
            for (int cx = 0; cx < 2; cx++)
            {
                var f = new NetsBuffers();
                SurfaceNets.Heightfield(cols, new NetsChunk { X0 = cx * 8, Z0 = cz * 8, Cells = 8, StepM = 2f }, ref f);
                long Key(int v) => ((long)BitConverter.SingleToInt32Bits(f.Verts[v * 6]) << 32 ^ (uint)BitConverter.SingleToInt32Bits(f.Verts[v * 6 + 2])) * 31 + BitConverter.SingleToInt32Bits(f.Verts[v * 6 + 1]);
                for (int v = 0; v < f.Verts.Count / 6; v++) pos[Key(v)] = (f.Verts[v * 6], f.Verts[v * 6 + 2]);
                for (int t = 0; t < f.Tris.Count; t += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        long a = Key(f.Tris[t + e]), b = Key(f.Tris[t + (e + 1) % 3]);
                        var k = (a, b);
                        edges[k] = edges.GetValueOrDefault(k) + 1;
                    }
            }
        bool Outer(long v) => pos[v].x < 0 || pos[v].x > 30 || pos[v].z < 0 || pos[v].z > 30;
        int open = 0;
        foreach (var (a, b) in edges.Keys)
            if (!edges.ContainsKey((b, a)))
            {
                open++;
                Assert.True(Outer(a) && Outer(b), $"open edge inside the block at {pos[a]}");
            }
        Assert.True(open > 0);
    }
}
