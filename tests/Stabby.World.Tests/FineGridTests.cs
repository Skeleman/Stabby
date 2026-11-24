using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class FineGridTests
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(Root);
    static readonly MaterialLibrary Materials = ContentLoader.LoadMaterials(Root);
    static readonly ObjectLibrary Objects = ContentLoader.LoadObjects(Root, Biomes.Select(b => b.Id).ToList());
    static readonly CaveLibrary Caves = ContentLoader.LoadCaves(ContentLoader.FindContentRoot(AppContext.BaseDirectory));

    [Fact]
    public void FourKCellsPlaceFeaturesOnTheGrid()
    {
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(1, 13, s =>
        {
            var c = new WorldConfig { Seed = (ulong)s, Size = 256, CellMeters = WorldConfig.CellMetersFor(4096) };
            try
            {
                foreach (var v in WorldChecks.Run(WorldGenerator.Generate(c, Biomes, Materials, null, null, Objects, Caves)))
                    if (v.StartsWith("feature edit", StringComparison.Ordinal)) failures.Add($"seed {s}: {v}");
            }
            catch (Exception x) { failures.Add($"seed {s}: {x.GetType().Name} {x.StackTrace?.Split('\n')[0]}"); }
        });
        Assert.True(failures.IsEmpty, string.Join("\n", failures.OrderBy(f => f)));
    }
}
