using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class ChunkTests
{
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(ContentLoader.FindContentRoot(AppContext.BaseDirectory));
    static readonly MaterialLibrary Materials = ContentLoader.LoadMaterials(ContentLoader.FindContentRoot(AppContext.BaseDirectory));

    static GeneratedWorld Gen(ulong seed) =>
        WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) }, Biomes, Materials);

    static GeneratedWorld Decoded(GeneratedWorld g)
    {
        var d = WorldFile.Read(WorldFile.Write(g), Materials);
        d.Sketch = g.Sketch;
        return d;
    }

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
        peer.Height[(2 * WorldChunks.Size + 5) * n + WorldChunks.Size + 7] += 0.5f;
        Assert.Equal(new[] { 2 * WorldChunks.Across(n) + 1 }, Differing(WorldChunks.Checksums(host), WorldChunks.Checksums(peer)));
    }

    [Fact]
    public void StreamedChunksRebuildTheHostWorld()
    {
        var host = Decoded(Gen(42));
        var peer = Decoded(Gen(43));
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

    [Fact]
    public void DecodedLayersStayWithinTheirSteps()
    {
        var g = Gen(42);
        var d = Decoded(g);
        float half = (float)ChunkCodes.Step(d.HeightScale) / 2 + 1e-3f;
        for (int i = 0; i < g.Height.Length; i++)
        {
            Assert.InRange(d.Height[i] - g.Height[i], -half, half);
            Assert.InRange(d.Water.WaterSurface[i] - g.Water.WaterSurface[i], -half, half);
            float q = g.Water.DischargeM3s[i];
            Assert.InRange(d.Water.DischargeM3s[i], q * 0.9996f - 2e-5f, q * 1.0004f + 2e-5f);
            Assert.InRange(d.Climate.TemperatureC[i] - g.Climate.TemperatureC[i], -0.21f, 0.21f);
            Assert.Equal(g.Water.Receiver[i], d.Water.Receiver[i]);
            Assert.Equal(g.Water.LakeId[i], d.Water.LakeId[i]);
            Assert.Equal(g.Biome[i], d.Biome[i]);
            Assert.Equal(g.Sea[i], d.Sea[i]);
            Assert.Equal(g.Water.IsRiver[i], d.Water.IsRiver[i]);
        }
        Assert.Equal(WorldChunks.Checksums(d), WorldChunks.Checksums(Decoded(d)));
    }

    [Fact]
    public void CoarseClimateInterpolatesBetweenNodes()
    {
        var c = new WorldConfig { Seed = 1, Size = 100, CellMeters = 11.25f };
        int n = c.Size, f = WorldChunks.ClimateStride(c.CellMeters), a = WorldChunks.Across(n);
        Assert.Equal(4, f);
        var w = WorldFile.Empty(c, null, Materials);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                w.Climate.TemperatureC[y * n + x] = -20 + 0.4f * x + 0.8f * y;
                w.Climate.PrecipitationMm[y * n + x] = 400 + 10 * x;
            }
        var d = WorldFile.Empty(c, null, Materials);
        for (int k = 0; k < a * a; k++) WorldChunks.Unpack(d, k % a, k / a, WorldChunks.Pack(w, k % a, k / a));
        for (int i = 0; i < n * n; i++)
        {
            Assert.InRange(d.Climate.TemperatureC[i] - w.Climate.TemperatureC[i], -0.21f, 0.21f);
            float half = 0.1f * MathF.Sqrt(10 * w.Climate.PrecipitationMm[i]) + 0.1f;
            Assert.InRange(d.Climate.PrecipitationMm[i] - w.Climate.PrecipitationMm[i], -half, half);
        }
    }
}
