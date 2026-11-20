using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class WorldFileTests
{
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(ContentLoader.FindContentRoot(AppContext.BaseDirectory));

    [Fact]
    public void BakedWorldLoadsBackIdentical()
    {
        var host = WorldGenerator.Generate(new WorldConfig { Seed = 42, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) }, Biomes);
        var bytes = WorldFile.Write(host);
        var loaded = WorldFile.Read(bytes);

        Assert.Equal(WorldConfig.GeneratorVersion, WorldFile.GeneratorVersionOf(bytes));
        Assert.Equal(host.BiomeIds, loaded.BiomeIds);
        Assert.Equal(WorldChunks.Checksums(host), WorldChunks.Checksums(loaded));
        Assert.Empty(WorldChecks.Run(loaded));
        Assert.Equal(host.OutputFingerprint(), loaded.OutputFingerprint());
    }

    [Fact]
    public void StaleOrTruncatedFilesAreRejected()
    {
        var bytes = WorldFile.Write(WorldGenerator.Generate(new WorldConfig { Seed = 3, Size = 128, CellMeters = WorldConfig.CellMetersFor(128) }, Biomes));
        Assert.Throws<FormatException>(() => WorldFile.Read(bytes[..^1]));
        bytes[8] ^= 1;
        Assert.Throws<FormatException>(() => WorldFile.Read(bytes));
    }

    [Fact]
    public void EmptyWorldStreamsToTheHost()
    {
        var host = WorldGenerator.Generate(new WorldConfig { Seed = 42, Size = 128, CellMeters = WorldConfig.CellMetersFor(128) }, Biomes);
        var peer = WorldFile.Empty(new WorldConfig { Seed = 42, Size = 128, CellMeters = host.Config.CellMeters }, host.BiomeIds);
        int a = WorldChunks.Across(128);
        for (int k = 0; k < a * a; k++) WorldChunks.Unpack(peer, k % a, k / a, WorldChunks.Pack(host, k % a, k / a));
        WorldChunks.Relink(peer);
        Assert.Equal(WorldChunks.Checksums(host), WorldChunks.Checksums(peer));
        Assert.Empty(WorldChecks.Run(peer));
    }
}
