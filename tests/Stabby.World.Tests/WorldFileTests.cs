using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class WorldFileTests
{
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(ContentLoader.FindContentRoot(AppContext.BaseDirectory));
    static readonly MaterialLibrary Materials = ContentLoader.LoadMaterials(ContentLoader.FindContentRoot(AppContext.BaseDirectory));

    [Theory]
    [InlineData(42UL)]
    [InlineData(70UL)]
    public void BakedWorldLoadsBackStable(ulong seed)
    {
        var host = WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = 256, CellMeters = WorldConfig.CellMetersFor(256) }, Biomes, Materials);
        var bytes = WorldFile.Write(host, 0x0123456789ABCDEFUL);
        var loaded = WorldFile.Read(bytes, Materials);

        Assert.Equal(WorldConfig.GeneratorVersion, WorldFile.GeneratorVersionOf(bytes));
        Assert.Equal(0x0123456789ABCDEFUL, WorldFile.ContentHashOf(bytes));
        Assert.Equal(host.BiomeIds, loaded.BiomeIds);
        Assert.Equal(WorldChunks.Checksums(host), WorldChunks.Checksums(loaded));
        Assert.Empty(WorldChecks.Run(loaded));
        Assert.Equal(loaded.OutputFingerprint(), WorldFile.Read(WorldFile.Write(loaded), Materials).OutputFingerprint());
    }

    [Fact]
    public void OldFormatNamesTheBakeCommand()
    {
        var bytes = WorldFile.Write(WorldGenerator.Generate(new WorldConfig { Seed = 3, Size = 128, CellMeters = WorldConfig.CellMetersFor(128) }, Biomes, Materials));
        BitConverter.GetBytes(WorldFile.Version - 1).CopyTo(bytes, 4);
        Assert.Equal(WorldFile.Version - 1, WorldFile.VersionOf(bytes));
        Assert.Contains("stabby bake --seed 3 --size 128", Assert.Throws<FormatException>(() => WorldFile.Read(bytes, Materials)).Message);
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
    [InlineData(32, int.MaxValue)]
    [InlineData(32, -5)]
    [InlineData(36, 0)]
    [InlineData(40, int.MaxValue)]
    [InlineData(44, int.MaxValue)]
    [InlineData(32, 129)]
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

    [Fact]
    public void ContentHashIgnoresLineEndingsAndSeparatesFiles()
    {
        byte[] B(string t) => System.Text.Encoding.UTF8.GetBytes(t);
        Assert.Equal(WorldFile.ContentHash(B("a\nb"), B("c")), WorldFile.ContentHash(B("a\r\nb"), B("c")));
        Assert.NotEqual(WorldFile.ContentHash(B("ab"), B("c")), WorldFile.ContentHash(B("a"), B("bc")));
    }

    [Theory]
    [InlineData(128)]
    [InlineData(100)]
    public void EmptyChecksumsMatchAnEmptyWorld(int size)
    {
        var ids = Biomes.Select(b => b.Id).ToArray();
        var empty = WorldFile.Empty(new WorldConfig { Seed = 7, Size = size, CellMeters = WorldConfig.CellMetersFor(size) }, ids, Materials);
        Assert.Equal(WorldChunks.Checksums(empty), WorldChunks.EmptyChecksums(size, WorldConfig.CellMetersFor(size), true));
    }
}
