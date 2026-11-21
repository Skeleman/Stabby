using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class ChunkTests
{
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(ContentLoader.FindContentRoot(AppContext.BaseDirectory));
    static readonly MaterialLibrary Materials = ContentLoader.LoadMaterials(ContentLoader.FindContentRoot(AppContext.BaseDirectory));

    static GeneratedWorld Gen(ulong seed) =>
        WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) }, Biomes, Materials);

    static int[] Differing(ulong[] a, ulong[] b) => Enumerable.Range(0, a.Length).Where(k => a[k] != b[k]).ToArray();

    [Fact]
    public void SameSeedSameChecksums()
    {
        Assert.Equal(WorldChunks.Checksums(Gen(42)), WorldChunks.Checksums(Gen(42)));
    }

    [Fact]
    public void OneEditedCellMarksOnlyItsChunk()
    {
        var host = Gen(42);
        var peer = Gen(42);
        int n = peer.Config.Size;
        peer.Height[(2 * WorldChunks.Size + 5) * n + WorldChunks.Size + 7] += 0.01f;
        Assert.Equal(new[] { 2 * WorldChunks.Across(n) + 1 }, Differing(WorldChunks.Checksums(host), WorldChunks.Checksums(peer)));
    }

    [Fact]
    public void StreamedChunksRebuildTheHostWorld()
    {
        var host = Gen(42);
        var peer = Gen(43);
        peer.Sketch = host.Sketch;
        int a = WorldChunks.Across(host.Config.Size);
        var hostSums = WorldChunks.Checksums(host);
        var stale = Differing(hostSums, WorldChunks.Checksums(peer));
        Assert.NotEmpty(stale);
        foreach (int k in stale) WorldChunks.Unpack(peer, k % a, k / a, WorldChunks.Pack(host, k % a, k / a));
        peer.Config.Seed = host.Config.Seed;
        WorldChunks.Relink(peer);

        Assert.Equal(hostSums, WorldChunks.Checksums(peer));
        Assert.Equal(host.Fingerprint(), peer.Fingerprint());
        Assert.Equal(host.Water.Lakes.Select(l => (l.Level, l.Outlet, l.Cells.Count)), peer.Water.Lakes.Select(l => (l.Level, l.Outlet, l.Cells.Count)));
        Assert.Empty(WorldChecks.Run(peer));
    }
}
