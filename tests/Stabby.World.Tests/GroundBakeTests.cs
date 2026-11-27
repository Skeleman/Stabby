using Stabby.World;

namespace Stabby.World.Tests;

public class GroundBakeTests
{
    static readonly Lazy<(GroundBake Bake, TerrainDetail Detail, byte[] File)> Baked = new(() =>
    {
        var w = TestWorlds.World(TestWorlds.DefaultSeed);
        var g = GroundBake.Run(w, 77, true, out var detail);
        return (g, detail, WorldFile.Write(w, 9, 5, g));
    });

    [Fact]
    public void StoredGroundMatchesTheLiveStage()
    {
        var (g, detail, file) = Baked.Value;
        var p = TestWorlds.Default;
        var loaded = WorldFile.Read(file, p.W.Materials);
        var back = loaded.Bake.Ground;
        Assert.Equal(5, loaded.Bake.Mound);
        Assert.Equal(WorldChunks.Checksums(p.W), loaded.Bake.Sums);
        Assert.Equal(77UL, back.Key);
        Assert.Equal(g.Write(), back.Write());
        Assert.Equal(p.Edits.Length, back.Edits.Length);
        Assert.Equal(p.Roads, back.Roads);
        var stored = new TerrainDetail(loaded, back.Detail);
        for (int i = 0; i < p.W.Height.Length; i += 97)
        {
            Assert.Equal(detail.Cells.Amp(i), stored.Cells.Amp(i));
            Assert.Equal(detail.Cells.Offset(i), stored.Cells.Offset(i));
        }
        Assert.Equal(detail.Cells.PieceCount, stored.Cells.PieceCount);
    }

    [Fact]
    public void AdoptedCachesPlaceTheSameObjects()
    {
        var (g, detail, _) = Baked.Value;
        var w = TestWorlds.World(TestWorlds.DefaultSeed);
        var fresh = new ObjectField(w, detail, g.Edits, w.Objects, g.Discoveries);
        var adopted = new ObjectField(w, detail, g.Edits, w.Objects, g.Discoveries).Adopt(g.Field);
        int a = WorldChunks.Across(w.Config.Size);
        for (int k = 0; k < a * a; k += 5) Assert.Equal(fresh.Chunk(k % a, k / a), adopted.Chunk(k % a, k / a));
        Assert.Equal(0, adopted.ChunkComputes);
        Assert.Equal(fresh.Roads(), adopted.Roads());
        Assert.Equal(fresh.BogPools(), adopted.BogPools());
        Assert.Equal(fresh.TownPlans().Length, adopted.TownPlans().Length);
    }

    [Fact]
    public void CorruptChunksOrGroundAreRejected()
    {
        var file = Baked.Value.File;
        var materials = TestWorlds.Default.W.Materials;
        var chunk = (byte[])file.Clone();
        chunk[file.Length / 4] ^= 0x5a;
        Assert.Throws<FormatException>(() => WorldFile.Read(chunk, materials));
        var ground = (byte[])file.Clone();
        ground[^9] ^= 0x5a;
        Assert.Throws<FormatException>(() => WorldFile.Read(ground, materials));
        Assert.Throws<FormatException>(() => WorldFile.Read(file[..^1], materials));
    }
}
