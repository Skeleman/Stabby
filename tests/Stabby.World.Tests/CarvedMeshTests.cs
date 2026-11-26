using System.Diagnostics;
using Stabby.World;
using Xunit.Abstractions;

namespace Stabby.World.Tests;

public class CarvedMeshTests
{
    readonly ITestOutputHelper output;

    public CarvedMeshTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void SkipPathMatchesFullPathInLabyrinthChunks()
    {
        var p = TestWorlds.Default;
        var detail = p.Detail;
        var placed = p.Edits;
        var maze = Array.Find(p.Graphs, g => g.Class == "labyrinth")!;
        float sm = NearField.NodeStep(0), size = NearField.NodeSize(0);
        var cols = new DetailColumns { Detail = detail, StepM = sm };
        var seen = new HashSet<(float, float)>();
        double fast = 0, full = 0;
        foreach (var node in maze.Nodes)
        {
            float x0 = (float)Math.Floor(node.X / size) * size, z0 = (float)Math.Floor(node.Z / size) * size;
            if (!seen.Add((x0, z0)) || seen.Count > 2) continue;
            var list = new EditArray { Items = placed.Where(e => e.MeshedIn(sm, x0, z0, x0 + size, z0 + size)).Select(e => e.AtStep(sm)).ToArray() };
            Assert.Contains(list.Items, e => e.Kind == FeatureEdit.Carve);
            var c = new NetsChunk { X0 = (int)(x0 / sm), Z0 = (int)(z0 / sm), Cells = NearField.NodeQuads, StepM = sm };
            var warm = new NetsBuffers();
            SurfaceNets.Edited(cols, list, new NetsChunk { X0 = c.X0, Z0 = c.Z0, Cells = 2, StepM = sm }, ref warm);
            var clock = Stopwatch.StartNew();
            var a = new NetsBuffers();
            SurfaceNets.Edited(cols, list, c, ref a);
            fast += clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            var b = new NetsBuffers();
            SurfaceNets.Edited(cols, list, c, ref b, false);
            full += clock.Elapsed.TotalMilliseconds;
            Assert.True(a.SameAs(b), $"chunk {x0},{z0} differs from the full path");
        }
        int n = Math.Min(seen.Count, 2);
        output.WriteLine($"{n} labyrinth chunks, skip path {fast / n:F0} ms, full path {full / n:F0} ms per chunk");
    }

    struct RowSink : INetsSink
    {
        public int[] Ints;
        public float[] Floats, Verts;
        public List<int> Tris;
        public int Cursor;

        public void Begin(int ints, int floats)
        {
            Ints = new int[ints];
            Floats = new float[floats];
        }

        public int GetInt(int k) => Ints[k];
        public void SetInt(int k, int v) => Ints[k] = v;
        public float GetFloat(int k) => Floats[k];
        public void SetFloat(int k, float v) => Floats[k] = v;

        public int AddVertex(float x, float y, float z, float nx, float ny, float nz)
        {
            int a = 6 * Cursor;
            Verts[a] = x; Verts[a + 1] = y; Verts[a + 2] = z; Verts[a + 3] = nx; Verts[a + 4] = ny; Verts[a + 5] = nz;
            return Cursor++;
        }

        public void AddTriangle(int a, int b, int c)
        {
            Tris.Add(a); Tris.Add(b); Tris.Add(c);
        }
    }

    [Fact]
    public void RowSlabsMatchOneSequentialPassInLabyrinthChunks()
    {
        var world = TestWorlds.Default;
        var detail = world.Detail;
        var placed = world.Edits;
        var maze = Array.Find(world.Graphs, g => g.Class == "labyrinth")!;
        float sm = 0.5f * NearField.NodeStep(0), size = NearField.NodeSize(0);
        var cols = new DetailColumns { Detail = detail, StepM = sm };
        var node = maze.Nodes.First(n => !n.Entrance);
        float x0 = (float)Math.Floor(node.X / size) * size, z0 = (float)Math.Floor(node.Z / size) * size;
        var list = new EditArray { Items = placed.Where(e => e.MeshedIn(sm, x0, z0, x0 + size, z0 + size)).Select(e => e.AtStep(sm)).ToArray() };
        var c = new NetsChunk { X0 = (int)(x0 / sm), Z0 = (int)(z0 / sm), Cells = 2 * NearField.NodeQuads, StepM = sm };
        var one = new NetsBuffers();
        SurfaceNets.Edited(cols, list, c, ref one);
        var lay = new RowSink();
        int count = SurfaceNets.Layout(cols, list, c, ref lay);
        lay.Verts = new float[6 * count];
        int rows = c.Cells + 1, slab = 7;
        var parts = new List<int>[(rows + slab - 1) / slab];
        for (int p = parts.Length - 1; p >= 0; p--)
        {
            int k0 = p * slab, k1 = Math.Min(rows, k0 + slab);
            var rs = new RowSink { Ints = lay.Ints, Floats = lay.Floats, Verts = lay.Verts, Tris = new List<int>(), Cursor = lay.Ints[3 * k0 * rows + 2] };
            SurfaceNets.Vertices(cols, list, c, ref rs, true, k0, k1);
            Assert.Equal(k1 < rows ? lay.Ints[3 * k1 * rows + 2] : count, rs.Cursor);
            SurfaceNets.Quads(list, c, ref rs, true, k0, k1);
            parts[p] = rs.Tris;
        }
        var tris = parts.SelectMany(t => t).ToList();
        Assert.Equal(one.Verts.Count, lay.Verts.Length);
        for (int q = 0; q < lay.Verts.Length; q++)
            Assert.True(BitConverter.SingleToInt32Bits(one.Verts[q]) == BitConverter.SingleToInt32Bits(lay.Verts[q]), $"vertex float {q} differs");
        Assert.Equal(one.Tris, tris);
        Assert.Contains(list.Items, e => e.Kind == FeatureEdit.Carve);
        output.WriteLine($"{count} vertices, {tris.Count / 3} triangles in {parts.Length} slabs match one pass");
    }
}
