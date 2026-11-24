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
            output.WriteLine($"seed {seed}: {n} labyrinth chunks, skip path {fast / n:F0} ms, full path {full / n:F0} ms per chunk");
            return;
        }
        throw new Xunit.Sdk.XunitException("no labyrinth in seeds 1-8");
    }
}
