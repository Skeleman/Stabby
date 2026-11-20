using System;

namespace Stabby.World
{
    public sealed class TerrainDetail
    {
        public const float WetFadeCells = 3f;
        public const float NoiseWavelengthCells = 4f;
        public const int Octaves = 5;

        readonly int n;
        readonly float[] h;
        readonly float[] amp;
        readonly uint seed;

        public TerrainDetail(GeneratedWorld w)
        {
            n = w.Config.Size;
            h = w.Height;
            seed = Rng.Stream(w.Config.Seed, "detail").NextU32();
            var dist = WetDistance(w);
            amp = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            float v = h[Clamp(y + dy) * n + Clamp(x + dx)];
                            lo = Math.Min(lo, v); hi = Math.Max(hi, v);
                        }
                    if (lo <= 0) continue;
                    float a = 2f + 0.12f * (hi - lo);
                    a *= Grid.Smoothstep(0.5f, 0.5f + WetFadeCells, dist[i]);
                    amp[i] = Math.Min(Math.Min(a, 40f), 0.6f * lo);
                }
        }

        public int Size => n;
        public uint Seed => seed;
        public float Height(int cell) => h[cell];
        public float Amplitude(int cell) => amp[cell];

        public float HeightAt(float gx, float gy) => DetailSampler.HeightAt(new ArrayCells(h, amp), n, seed, gx, gy);

        int Clamp(int v) => v < 0 ? 0 : v >= n ? n - 1 : v;

        readonly struct ArrayCells : IDetailCells
        {
            readonly float[] h, amp;
            public ArrayCells(float[] h, float[] amp) { this.h = h; this.amp = amp; }
            public float Height(int i) => h[i];
            public float Amp(int i) => amp[i];
        }

        static float[] WetDistance(GeneratedWorld w)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            var d = new float[n * n];
            for (int i = 0; i < d.Length; i++)
                d[i] = w.Sea[i] || w.Water.LakeId[i] >= 0 ? 0f : float.MaxValue;
            foreach (var r in w.Water.Rivers)
                for (int k = 0; k < r.Cells.Count; k++)
                    d[r.Cells[k]] = Math.Min(d[r.Cells[k]], -r.Width[k] * 0.5f / cm);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = pass == 0 ? 0 : n * n - 1, step = pass == 0 ? 1 : -1;
                for (int i = start; i >= 0 && i < n * n; i += step)
                {
                    int x = i % n, y = i / n;
                    for (int k = 0; k < 8; k++)
                    {
                        bool back = Grid.DY[k] < 0 || (Grid.DY[k] == 0 && Grid.DX[k] < 0);
                        if (back != (pass == 0)) continue;
                        int nx = x + Grid.DX[k], ny = y + Grid.DY[k];
                        if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                        float v = d[ny * n + nx];
                        if (v != float.MaxValue) d[i] = Math.Min(d[i], v + Grid.Dist[k]);
                    }
                }
            }
            return d;
        }
    }

    public interface IDetailCells
    {
        float Height(int i);
        float Amp(int i);
    }

    public static class DetailSampler
    {
        public static float HeightAt<T>(T c, int n, uint seed, float gx, float gy) where T : struct, IDetailCells
        {
            int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy);
            float tx = gx - x0, ty = gy - y0;
            float r0 = Row(c, n, y0 - 1, x0, tx), r1 = Row(c, n, y0, x0, tx), r2 = Row(c, n, y0 + 1, x0, tx), r3 = Row(c, n, y0 + 2, x0, tx);
            int ax = Clamp(x0, n), ay = Clamp(y0, n), bx = Clamp(x0 + 1, n), by = Clamp(y0 + 1, n);
            float c00 = c.Height(ay * n + ax), c10 = c.Height(ay * n + bx), c01 = c.Height(by * n + ax), c11 = c.Height(by * n + bx);
            float lo = Math.Min(Math.Min(c00, c10), Math.Min(c01, c11)), hi = Math.Max(Math.Max(c00, c10), Math.Max(c01, c11));
            float b = Math.Max(lo, Math.Min(hi, CatmullRom(r0, r1, r2, r3, ty)));

            float a00 = c.Amp(ay * n + ax), a10 = c.Amp(ay * n + bx), a01 = c.Amp(by * n + ax), a11 = c.Amp(by * n + bx);
            float a0 = a00 + (a10 - a00) * tx;
            float a1 = a01 + (a11 - a01) * tx;
            float a = a0 + (a1 - a0) * ty;
            if (a <= 0) return b;
            float f = Noise.Fbm(seed, gx / TerrainDetail.NoiseWavelengthCells, gy / TerrainDetail.NoiseWavelengthCells, TerrainDetail.Octaves);
            return b + a * Math.Max(-1f, Math.Min(1f, f));
        }

        static float Row<T>(T c, int n, int y, int x0, float t) where T : struct, IDetailCells
        {
            int r = Clamp(y, n) * n;
            return CatmullRom(c.Height(r + Clamp(x0 - 1, n)), c.Height(r + Clamp(x0, n)), c.Height(r + Clamp(x0 + 1, n)), c.Height(r + Clamp(x0 + 2, n)), t);
        }

        static int Clamp(int v, int n) => v < 0 ? 0 : v >= n ? n - 1 : v;

        static float CatmullRom(float p0, float p1, float p2, float p3, float t) =>
            p1 + 0.5f * t * (p2 - p0 + t * (2 * p0 - 5 * p1 + 4 * p2 - p3 + t * (3 * (p1 - p2) + p3 - p0)));
    }
}
