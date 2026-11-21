using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class WorldFileTests
{
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(ContentLoader.FindContentRoot(AppContext.BaseDirectory));
    static readonly MaterialLibrary Materials = ContentLoader.LoadMaterials(ContentLoader.FindContentRoot(AppContext.BaseDirectory));

    [Fact]
    public void BakedWorldLoadsBackIdentical()
    {
        var host = WorldGenerator.Generate(new WorldConfig { Seed = 42, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) }, Biomes, Materials);
        var bytes = WorldFile.Write(host);
        var loaded = WorldFile.Read(bytes, Materials);

        Assert.Equal(WorldConfig.GeneratorVersion, WorldFile.GeneratorVersionOf(bytes));
        Assert.Equal(host.BiomeIds, loaded.BiomeIds);
        Assert.Equal(WorldChunks.Checksums(host), WorldChunks.Checksums(loaded));
        Assert.Empty(WorldChecks.Run(loaded));
        Assert.Equal(host.OutputFingerprint(), loaded.OutputFingerprint());
    }

    [Fact]
    public void StaleOrTruncatedFilesAreRejected()
    {
        var bytes = WorldFile.Write(WorldGenerator.Generate(new WorldConfig { Seed = 3, Size = 128, CellMeters = WorldConfig.CellMetersFor(128) }, Biomes, Materials));
        Assert.Throws<FormatException>(() => WorldFile.Read(bytes[..^1], Materials));
        bytes[8] ^= 1;
        Assert.Throws<FormatException>(() => WorldFile.Read(bytes, Materials));
    }

    [Theory]
    [InlineData(24, int.MaxValue)]
    [InlineData(24, -5)]
    [InlineData(28, 0)]
    [InlineData(32, int.MaxValue)]
    [InlineData(36, int.MaxValue)]
    [InlineData(24, 129)]
    public void MalformedHeadersAreRejectedBeforeAllocating(int offset, int value)
    {
        var bytes = WorldFile.Write(WorldGenerator.Generate(new WorldConfig { Seed = 3, Size = 128, CellMeters = WorldConfig.CellMetersFor(128) }, Biomes, Materials));
        BitConverter.GetBytes(value).CopyTo(bytes, offset);
        Assert.Throws<FormatException>(() => WorldFile.Read(bytes, Materials));
    }

    [Fact]
    public void EmptyWorldStreamsToTheHost()
    {
        var host = WorldGenerator.Generate(new WorldConfig { Seed = 42, Size = 128, CellMeters = WorldConfig.CellMetersFor(128) }, Biomes, Materials);
        var peer = WorldFile.Empty(new WorldConfig { Seed = 42, Size = 128, CellMeters = host.Config.CellMeters }, host.BiomeIds, Materials);
        int a = WorldChunks.Across(128);
        for (int k = 0; k < a * a; k++) WorldChunks.Unpack(peer, k % a, k / a, WorldChunks.Pack(host, k % a, k / a));
        WorldChunks.Relink(peer);
        Assert.Equal(WorldChunks.Checksums(host), WorldChunks.Checksums(peer));
        Assert.Empty(WorldChecks.Run(peer));
    }
}
