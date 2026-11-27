using Stabby.World;

namespace Stabby.World.Tests;

public class GroundBakeTests
{
    const int Every = 5;

    static readonly Lazy<(GroundBake Bake, TerrainDetail Detail, byte[] File)> Baked = new(() =>
    {
        var w = TestWorlds.World(TestWorlds.DefaultSeed);
        GroundBake g = null!;
        TerrainDetail detail = null!;
        Parallel.Invoke(() => g = GroundBake.Run(w, 77, true, out detail, Every), () => _ = TestWorlds.Default);
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
        var want = new ObjectInstance[(a * a + Every - 1) / Every][];
        Parallel.For(0, want.Length, i => want[i] = fresh.Chunk(i * Every % a, i * Every / a));
        for (int k = 0; k < a * a; k += Every) Near(want[k / Every], adopted.Chunk(k % a, k / a), fresh.ChunkMeters);
        Assert.Equal(0, adopted.ChunkComputes);
        Assert.Equal(fresh.Roads(), adopted.Roads());
        Assert.Equal(fresh.BogPools(), adopted.BogPools());
        Assert.Equal(fresh.TownPlans().Length, adopted.TownPlans().Length);
    }

    [Fact]
    public void CorruptPackedChunkFallsBackToLivePlacement()
    {
        var (g, detail, _) = Baked.Value;
        var w = TestWorlds.World(TestWorlds.DefaultSeed);
        var good = g.Field.PlacedPack;
        int k = Array.FindIndex(g.Field.PlacedRaw, r => r > 0);
        int a = WorldChunks.Across(w.Config.Size);
        var bad = (byte[])good.Clone();
        bad[(g.Field.PlacedAt[k] + g.Field.PlacedAt[k + 1]) / 2] ^= 0x5a;
        ObjectField adopted;
        try
        {
            g.Field.PlacedPack = bad;
            adopted = new ObjectField(w, detail, g.Edits, w.Objects, g.Discoveries).Adopt(g.Field);
        }
        finally { g.Field.PlacedPack = good; }
        var fresh = new ObjectField(w, detail, g.Edits, w.Objects, g.Discoveries);
        Assert.Equal(fresh.Chunk(k % a, k / a), adopted.Chunk(k % a, k / a));
        Assert.Equal(1, adopted.ChunkComputes);
    }

    static void Near(ObjectInstance[] want, ObjectInstance[] got, float span)
    {
        Assert.Equal(want.Length, got.Length);
        for (int i = 0; i < want.Length; i++)
        {
            var (a, b) = (want[i], got[i]);
            Assert.Equal((a.Def, a.Variant, a.Id), (b.Def, b.Variant, b.Id));
            Assert.InRange(MathF.Abs(a.X - b.X), 0, span / 30000);
            Assert.InRange(MathF.Abs(a.Z - b.Z), 0, span / 30000);
            Assert.InRange(MathF.Abs(a.Y - b.Y), 0, 0.02f);
            Assert.InRange(MathF.Abs(a.Size - b.Size), 0, 0.01f + a.Size / 1000);
            float dot = a.Qx * b.Qx + a.Qy * b.Qy + a.Qz * b.Qz + a.Qw * b.Qw;
            float norm = a.Qx * a.Qx + a.Qy * a.Qy + a.Qz * a.Qz + a.Qw * a.Qw;
            Assert.InRange(MathF.Abs(dot), norm * 0.99999f - 1e-6f, 1.0001f);
        }
    }

    [Fact]
    public void CorruptChunksOrGroundAreRejected()
    {
        var file = Baked.Value.File;
        var materials = TestWorlds.Default.W.Materials;
        var table = WorldFile.Read(file, materials).Bake;
        var chunk = (byte[])file.Clone();
        chunk[table.At[0] + table.Len[0] / 2] ^= 0x5a;
        Assert.Throws<FormatException>(() => WorldFile.Read(chunk, materials));
        var ground = (byte[])file.Clone();
        ground[^9] ^= 0x5a;
        Assert.Throws<FormatException>(() => WorldFile.Read(ground, materials));
        Assert.Throws<FormatException>(() => WorldFile.Read(file[..^1], materials));
    }
}
