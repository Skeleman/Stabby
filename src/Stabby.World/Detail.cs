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

        public float Amplitude(int cell) => amp[cell];

        public float HeightAt(float gx, float gy)
        {
            int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy);
            float tx = gx - x0, ty = gy - y0;
            float r0 = Row(y0 - 1, x0, tx), r1 = Row(y0, x0, tx), r2 = Row(y0 + 1, x0, tx), r3 = Row(y0 + 2, x0, tx);
            int ax = Clamp(x0), ay = Clamp(y0), bx = Clamp(x0 + 1), by = Clamp(y0 + 1);
            float c00 = h[ay * n + ax], c10 = h[ay * n + bx], c01 = h[by * n + ax], c11 = h[by * n + bx];
            float lo = Math.Min(Math.Min(c00, c10), Math.Min(c01, c11)), hi = Math.Max(Math.Max(c00, c10), Math.Max(c01, c11));
            float b = Math.Max(lo, Math.Min(hi, CatmullRom(r0, r1, r2, r3, ty)));

            float a0 = amp[ay * n + ax] + (amp[ay * n + bx] - amp[ay * n + ax]) * tx;
            float a1 = amp[by * n + ax] + (amp[by * n + bx] - amp[by * n + ax]) * tx;
            float a = a0 + (a1 - a0) * ty;
            if (a <= 0) return b;
            float f = Noise.Fbm(seed, gx / NoiseWavelengthCells, gy / NoiseWavelengthCells, Octaves);
            return b + a * Math.Max(-1f, Math.Min(1f, f));
        }

        float Row(int y, int x0, float t)
        {
            int r = Clamp(y) * n;
            return CatmullRom(h[r + Clamp(x0 - 1)], h[r + Clamp(x0)], h[r + Clamp(x0 + 1)], h[r + Clamp(x0 + 2)], t);
        }

        int Clamp(int v) => v < 0 ? 0 : v >= n ? n - 1 : v;

        static float CatmullRom(float p0, float p1, float p2, float p3, float t) =>
            p1 + 0.5f * t * (p2 - p0 + t * (2 * p0 - 5 * p1 + 4 * p2 - p3 + t * (3 * (p1 - p2) + p3 - p0)));

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
}
