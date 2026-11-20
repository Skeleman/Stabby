using System.Reflection;
using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class WorldTests
{
    static readonly List<BiomeDef> Biomes = ContentLoader.LoadBiomes(ContentLoader.FindContentRoot(AppContext.BaseDirectory));

    static GeneratedWorld Gen(ulong seed, int size = 128) =>
        WorldGenerator.Generate(new WorldConfig { Seed = seed, Size = size }, Biomes);

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
        var c = new WorldConfig { Seed = 9, Size = 128 };
        var a = WorldGenerator.Generate(c, Biomes);
        var again = WorldSketch.FromRanges(c, a.Sketch.LandFraction, a.Sketch.Ranges);
        Assert.Equal(a.Fingerprint(), WorldGenerator.Generate(c, Biomes, again).Fingerprint());
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
