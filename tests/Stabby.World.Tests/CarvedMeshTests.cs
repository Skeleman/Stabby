using System.Diagnostics;
using Stabby.Content;
using Stabby.World;
using Xunit.Abstractions;

namespace Stabby.World.Tests;

public class CarvedMeshTests
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    readonly ITestOutputHelper output;

    public CarvedMeshTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void SkipPathMatchesFullPathInLabyrinthChunks()
    {
        var biomes = ContentLoader.LoadBiomes(Root);
        var materials = ContentLoader.LoadMaterials(Root);
        var caves = ContentLoader.LoadCaves(Root);
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var w = WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = 512, CellMeters = WorldConfig.CellMetersFor(512) }, biomes, materials, null, null, null, caves);
            var detail = new TerrainDetail(w);
            var placed = Features.Place(w, detail, out var graphs);
            var maze = Array.Find(graphs, g => g.Class == "labyrinth");
            if (maze == null) continue;
            float sm = NearField.NodeStep(0), size = NearField.NodeSize(0);
            var cols = new DetailColumns { Detail = detail, StepM = sm };
            var seen = new HashSet<(float, float)>();
            double fast = 0, full = 0;
            int n = 0;
            var picks = maze.Nodes.Select(v => (v.X, v.Z, true)).ToList();
            foreach (var g in graphs.Where(g => g.Class != "labyrinth").Take(2)) picks.Add((g.Nodes[0].X, g.Nodes[0].Z, false));
            foreach (var (nx, nz, inMaze) in picks)
            {
                float x0 = (float)Math.Floor(nx / size) * size, z0 = (float)Math.Floor(nz / size) * size;
                if ((inMaze && n >= 2) || !seen.Add((x0, z0))) continue;
                var list = new EditArray { Items = placed.Where(e => e.MeshedIn(sm, x0, z0, x0 + size, z0 + size)).Select(e => e.AtStep(sm)).ToArray() };
                if (inMaze) Assert.Contains(list.Items, e => e.Kind == FeatureEdit.Carve);
                var c = new NetsChunk { X0 = (int)(x0 / sm), Z0 = (int)(z0 / sm), Cells = NearField.NodeQuads, StepM = sm };
                var warm = new NetsBuffers();
                SurfaceNets.Edited(cols, list, new NetsChunk { X0 = c.X0, Z0 = c.Z0, Cells = 2, StepM = sm }, ref warm);
                var clock = Stopwatch.StartNew();
                var a = new NetsBuffers();
                SurfaceNets.Edited(cols, list, c, ref a);
                double ta = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                var b = new NetsBuffers();
                SurfaceNets.Edited(cols, list, c, ref b, false);
                double tb = clock.Elapsed.TotalMilliseconds;
                Assert.True(a.SameAs(b), $"chunk {x0},{z0} differs from the full path");
                var again = new NetsBuffers();
                SurfaceNets.Edited(cols, list, new NetsChunk { X0 = c.X0 + 3, Z0 = c.Z0 + 5, Cells = 7, StepM = sm }, ref again);
                var againFull = new NetsBuffers();
                SurfaceNets.Edited(cols, list, new NetsChunk { X0 = c.X0 + 3, Z0 = c.Z0 + 5, Cells = 7, StepM = sm }, ref againFull, false);
                Assert.True(again.SameAs(againFull), $"small chunk at {x0},{z0} differs after reusing scratch");
                if (!inMaze) continue;
                fast += ta;
                full += tb;
                n++;
            }
            output.WriteLine($"seed {seed}: {n} labyrinth chunks, skip path {fast / n:F0} ms, full path {full / n:F0} ms per chunk, {seen.Count - n} other cave chunks");
            return;
        }
        throw new Xunit.Sdk.XunitException("no labyrinth in seeds 1-8");
    }
}
