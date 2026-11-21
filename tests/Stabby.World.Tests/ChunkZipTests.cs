using System.IO;
using Stabby.Content;
using Stabby.Game;
using Stabby.World;

namespace Stabby.World.Tests;

public class ChunkZipTests
{
    static GeneratedWorld World() => WorldGenerator.Generate(new WorldConfig { Seed = 7, Size = 128, CellMeters = WorldConfig.CellMetersFor(128) },
        ContentLoader.LoadBiomes(ContentLoader.FindContentRoot(AppContext.BaseDirectory)), ContentLoader.LoadMaterials(ContentLoader.FindContentRoot(AppContext.BaseDirectory)));

    [Fact]
    public void DeflatedChunksRoundTrip()
    {
        var w = World();
        int a = WorldChunks.Across(w.Config.Size);
        for (int k = 0; k < a * a; k++)
        {
            var raw = WorldChunks.Pack(w, k % a, k / a);
            var zipped = ChunkZip.Deflate(raw);
            Assert.True(zipped.Length < raw.Length);
            Assert.Equal(raw, ChunkZip.Inflate(zipped, WorldChunks.Bytes(w.Config.Size, k % a, k / a)));
        }
    }

    [Fact]
    public void MalformedPayloadsAreRejected()
    {
        var w = World();
        int max = WorldChunks.Bytes(w.Config.Size, 0, 0);
        var zipped = ChunkZip.Deflate(WorldChunks.Pack(w, 0, 0));
        Assert.Throws<InvalidDataException>(() => ChunkZip.Inflate(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x13 }, max));
        Assert.Throws<InvalidDataException>(() => ChunkZip.Inflate(zipped, max - 1));
        Assert.Throws<InvalidDataException>(() => ChunkZip.Inflate(ChunkZip.Deflate(new byte[max * 4]), max));
        var truncated = ChunkZip.Inflate(zipped[..(zipped.Length / 2)], max);
        Assert.Throws<ArgumentException>(() => WorldChunks.Unpack(w, 0, 0, truncated));
    }
}
