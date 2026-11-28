using System.Text.RegularExpressions;
using Stabby.Content;

namespace Stabby.World.Tests;

public class BiomeBorderTests
{
    const int N = 64;
    const float Cm = 10f;

    static int[] Diagonal()
    {
        var b = new int[N * N];
        for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
                b[z * N + x] = x + z >= N ? 1 : 0;
        return b;
    }

    static float Crossing(Func<float, float, int> at, float s)
    {
        float cx = 0.5f * (N - 0.5f) + s, cz = 0.5f * (N - 0.5f) - s, prevT = -2f;
        int prev = at((cx - 2f) * Cm, (cz - 2f) * Cm), changes = 0;
        float found = float.NaN;
        for (float t = -2f + 0.01f; t <= 2f; t += 0.01f)
        {
            int cur = at((cx + t) * Cm, (cz + t) * Cm);
            if (cur != prev) { changes++; found = 0.5f * (t + prevT); }
            prev = cur;
            prevT = t;
        }
        Assert.Equal(1, changes);
        return found;
    }

    [Fact]
    public void DiagonalBorderIsStraight()
    {
        var biome = Diagonal();
        float worst = 0f, worstCell = 0f;
        for (float s = -8f; s <= 8f; s += 0.05f)
        {
            float t = Crossing((x, z) => BiomeBorder.At(biome, N, Cm, x, z), s);
            float px = 0.5f * (N - 0.5f) + s + t, pz = 0.5f * (N - 0.5f) - s + t;
            BiomeBorder.Warp(px, pz, out float wx, out float wz);
            worst = Math.Max(worst, Math.Abs(px + wx + pz + wz - (N - 0.5f)));
            float tc = Crossing((x, z) => biome[(int)Math.Round(z / Cm) * N + (int)Math.Round(x / Cm)], s);
            worstCell = Math.Max(worstCell, Math.Abs(2f * tc));
        }
        Assert.True(worst < 0.15f, $"unwarped border strays {worst} cells from the diagonal");
        Assert.True(worstCell > 0.4f, $"per-cell lookup strays only {worstCell} cells, so the measure misses stair steps");
    }

    [Fact]
    public void WarpIsSmoothAndBounded()
    {
        float maxStep = 0f;
        for (float g = 0f; g < 40f; g += 0.05f)
        {
            BiomeBorder.Warp(g, 0.37f * g, out float ax, out float az);
            BiomeBorder.Warp(g + 0.05f, 0.37f * (g + 0.05f), out float bx, out float bz);
            Assert.True(Math.Abs(ax) <= BiomeBorder.WarpCells && Math.Abs(az) <= BiomeBorder.WarpCells);
            maxStep = Math.Max(maxStep, Math.Max(Math.Abs(bx - ax), Math.Abs(bz - az)));
        }
        Assert.True(maxStep < 0.05f, $"warp jumps {maxStep} cells in a 0.05-cell step");
    }

    static int ArgMax(Func<int, float> weight, int count)
    {
        int best = -1;
        float top = -1f;
        for (int k = 0; k < count; k++)
            if (weight(k) > top) { top = weight(k); best = k; }
        return best;
    }

    [Fact]
    public void ColourCoatAndPlacementAgree()
    {
        var w = TestWorlds.Default.W;
        var surface = ContentLoader.LoadMaterials(ContentLoader.FindContentRoot(AppContext.BaseDirectory)).Surface;
        var coat = surface.CoatWeights(w);
        int n = w.Config.Size, kinds = w.BiomeIds.Length, checkedQuads = 0;
        float cm = w.Config.CellMeters, scale = 1f / (n * cm), offset = 0.5f / n;
        var rng = Rng.Stream(TestWorlds.DefaultSeed, "biome-border-test");
        for (int i = 0; i < 200000 && checkedQuads < 2000; i++)
        {
            float x = (float)(rng.NextU64() >> 40) / (1 << 24) * (n - 2) * cm, z = (float)(rng.NextU64() >> 40) / (1 << 24) * (n - 2) * cm;
            float px = x * scale / (2 * offset) / 2.5f, pz = z * scale / (2 * offset) / 2.5f;
            BiomeBorder.Warp(px * 2.5f, pz * 2.5f, out float wx, out float wz);
            float u = x * scale + offset + 2 * offset * wx, v = z * scale + offset + 2 * offset * wz;
            float gx = u * n - 0.5f, gz = v * n - 0.5f;
            int x0 = (int)gx, z0 = (int)gz, a = z0 * n + x0;
            if (x0 < 0 || z0 < 0 || x0 > n - 2 || z0 > n - 2) continue;
            int[] q = { a, a + 1, a + n, a + n + 1 };
            if (q.Any(c => w.Sea[c] || w.Biome[c] < 0)) continue;
            if (q.Select(c => w.Biome[c]).Distinct().Count() != 2) continue;
            float tx = gx - x0, tz = gz - z0;
            float[] f = { (1 - tx) * (1 - tz), tx * (1 - tz), (1 - tx) * tz, tx * tz };
            int colour = ArgMax(k => Enumerable.Range(0, 4).Where(j => w.Biome[q[j]] == k).Sum(j => f[j]), kinds);
            int place = BiomeBorder.At(w, x, z);
            Assert.Equal(colour, place);
            int coatKind = surface.CoatOf(w.BiomeIds[place]);
            if (coatKind < 0 || q.Select(c => surface.CoatOf(w.BiomeIds[w.Biome[c]])).Distinct().Count() != 2) continue;
            int coatMax = ArgMax(k => Enumerable.Range(0, 4).Sum(j => coat[q[j] * 4 + k] / 255f * f[j]), 4);
            Assert.Equal(coatMax, coatKind);
            checkedQuads++;
        }
        Assert.True(checkedQuads > 100, $"only {checkedQuads} border points checked");
    }

    [Fact]
    public void ShaderWarpMatchesTheWorldSample()
    {
        var root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
        var hlsl = File.ReadAllText(Path.Combine(root, "..", "unity", "Assets", "Viewer", "Shaders", "TerrainMaterials.hlsl"));
        var cs = File.ReadAllText(Path.Combine(root, "..", "src", "Stabby.World", "BiomeBorder.cs"));
        foreach (var token in new[] { "0x8da6b343u", "0xd8163841u", "0x7feb352du", "0x846ca68bu", "0x2f6bu", "0x9c41u", "16777216" })
        {
            Assert.Contains(token, hlsl);
            Assert.Contains(token, cs);
        }
        Assert.Matches(new Regex(@"\(1\.0 / " + Regex.Escape(BiomeBorder.WarpWavelengthCells.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)) + @"\)"), hlsl);
        Assert.Contains("2 * offset * " + BiomeBorder.WarpCells.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture) + " *", hlsl);
    }
}
