using System.Reflection;
using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class WorldTests
{
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(ContentLoader.FindContentRoot(AppContext.BaseDirectory));

    // Same 30 km span as the default world at coarser cells, so tests stay fast.
    static WorldConfig Cfg(ulong seed, int size = 128) =>
        new WorldConfig { Seed = seed, Size = size, CellMeters = 60f * 512 / size };

    static GeneratedWorld Gen(ulong seed, int size = 128) => WorldGenerator.Generate(Cfg(seed, size), Biomes);

    [Fact]
    public void HundredSeedsPassEveryCheck()
    {
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(1, 101, s =>
        {
            foreach (var v in WorldChecks.Run(Gen((ulong)s))) failures.Add($"seed {s}: {v}");
        });
        Assert.True(failures.IsEmpty, string.Join("\n", failures.OrderBy(f => f).Take(20)));
    }

    [Fact]
    public void SameSeedSameWorld()
    {
        Assert.Equal(Gen(42).Fingerprint(), Gen(42).Fingerprint());
        Assert.NotEqual(Gen(42).Fingerprint(), Gen(43).Fingerprint());
    }

    [Fact]
    public void EditedSketchRoundTripsToSameWorld()
    {
        var c = Cfg(9);
        var a = WorldGenerator.Generate(c, Biomes);
        var again = SketchFile.Read(SketchFile.Write(a.Sketch, c), Cfg(1, 64));
        Assert.Equal(a.Fingerprint(), WorldGenerator.Generate(c, Biomes, again).Fingerprint());
    }

    [Fact]
    public void AuthoredEditsAreHonoured()
    {
        var c = Cfg(11);
        var s = WorldGenerator.Generate(c, Biomes).Sketch;
        var big = s.Landmasses[0];
        s.Regions.Clear();
        s.Regions.Add(new RegionSketch { Biome = "desert", X = big.X, Y = big.Y, Radius = big.Radius * 0.5f });
        s.Landmarks.Add(new LandmarkSketch { Kind = "town", X = big.X + big.Radius * 0.3f, Y = big.Y });
        s.Build(c);
        var w = WorldGenerator.Generate(c, Biomes, s);
        Assert.Empty(WorldChecks.Run(w));
        int n = c.Size, centre = (int)big.Y * n + (int)big.X;
        if (!w.Sea[centre] && w.Water.LakeId[centre] < 0) Assert.Equal("desert", w.BiomeIds[w.Biome[centre]]);
    }

    [Fact]
    public void TerrainStageSkipsClimateAndBiomes()
    {
        var c = Cfg(5);
        c.UniformRainMm = 1200;
        var w = WorldGenerator.Generate(c, Biomes);
        Assert.Null(w.Biome);
        Assert.Empty(WorldChecks.Run(w));
        Assert.True(w.Water.Rivers.Count > 0);
    }

    [Fact]
    public void BiomeContentMatchesSchema()
    {
        var root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
        using var schema = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "schemas", "biomes.schema.json")));
        using var data = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "biomes.json")));
        var item = schema.RootElement.GetProperty("properties").GetProperty("biomes").GetProperty("items");
        var required = item.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToList();
        var allowed = item.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();
        foreach (var b in data.RootElement.GetProperty("biomes").EnumerateArray())
        {
            var names = b.EnumerateObject().Select(p => p.Name).ToList();
            Assert.Empty(required.Except(names));
            Assert.Empty(names.Except(allowed));
        }
    }

    [Fact]
    public void MountainsCastRainShadows()
    {
        double wind = 0, lee = 0; int nw = 0, nl = 0;
        for (ulong s = 1; s <= 20; s++)
        {
            var w = Gen(s);
            int n = w.Config.Size;
            for (int y = 1; y < n - 1; y++)
                for (int x = 1; x < n - 1; x++)
                {
                    int i = y * n + x;
                    if (w.Sea[i] || w.Height[i] < 200) continue;
                    float rise = w.Height[i + 1] - w.Height[i - 1];
                    if (rise > 20) { wind += w.Climate.PrecipitationMm[i]; nw++; }
                    else if (rise < -20) { lee += w.Climate.PrecipitationMm[i]; nl++; }
                }
        }
        Assert.True(nw > 100 && nl > 100, $"too few slope cells: {nw} windward, {nl} leeward");
        Assert.True(wind / nw > 1.1 * lee / nl, $"windward {wind / nw:F0} mm vs leeward {lee / nl:F0} mm");
    }

    [Fact]
    public void WorldsHaveRiversLakesAndMountains()
    {
        var w = Gen(3, 256);
        Assert.True(w.Water.Rivers.Count > 10, $"{w.Water.Rivers.Count} river reaches");
        Assert.True(w.Height.Max() > 500, $"peak {w.Height.Max()} m");
    }

    [Fact]
    public void BiomeContentLoads() => Assert.Equal(9, Biomes.Count);

    [Fact]
    public void WorldLibraryHasNoEngineOrFileDependencies()
    {
        var asm = typeof(WorldGenerator).Assembly;
        var refs = asm.GetReferencedAssemblies().Select(r => r.Name ?? "").ToList();
        Assert.DoesNotContain(refs, r => r.StartsWith("UnityEngine") || r.StartsWith("System.Text.Json"));
        var banned = new[] { "System.IO.File", "System.IO.Directory", "System.IO.FileStream", "System.Console" };
        foreach (var t in asm.GetTypes())
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var body = m.GetMethodBody();
                if (body == null) continue;
                var il = body.GetILAsByteArray() ?? Array.Empty<byte>();
                for (int k = 0; k + 4 < il.Length; k++)
                {
                    if (il[k] != 0x28 && il[k] != 0x6F && il[k] != 0x73) continue;
                    MethodBase callee;
                    try { callee = m.Module.ResolveMethod(BitConverter.ToInt32(il, k + 1))!; } catch { continue; }
                    var owner = callee.DeclaringType?.FullName ?? "";
                    Assert.False(banned.Contains(owner), $"{t.Name}.{m.Name} calls {owner}.{callee.Name}");
                }
            }
    }
}
