using System;
using System.Collections.Generic;

namespace Stabby.World
{
    // A mountain range as a ridge polyline in cell coordinates. Strength scales
    // uplift (0..1); Width is the range's half-width in cells.
    public sealed class RangeSketch
    {
        public float[] X, Y;
        public float Strength, Width;
    }

    // The coarse, editable layer: where land is and where the ranges run.
    // Everything finer is derived from it, so a hand edit here regenerates a
    // consistent world instead of being patched in afterwards.
    public sealed class WorldSketch
    {
        public int Size;
        public float LandFraction;
        public List<RangeSketch> Ranges = new List<RangeSketch>();
        public bool[] Land;
        public float[] Uplift;

        public static WorldSketch Generate(WorldConfig c)
        {
            var s = new WorldSketch { Size = c.Size };
            var rng = Rng.Stream(c.Seed, "sketch");
            s.LandFraction = rng.Range(c.LandFractionMin, c.LandFractionMax);
            s.Land = LandMask(c, rng.NextU32(), s.LandFraction);

            int count = rng.Range(c.RangesMin, c.RangesMax + 1);
            for (int i = 0; i < count; i++)
            {
                var r = RandomRange(c, s.Land, ref rng);
                if (r != null) s.Ranges.Add(r);
            }
            s.BuildUplift(c);
            return s;
        }

        // Rebuilds the derived fields after the ranges were edited by hand.
        public static WorldSketch FromRanges(WorldConfig c, float landFraction, List<RangeSketch> ranges)
        {
            var rng = Rng.Stream(c.Seed, "sketch");
            rng.NextFloat();
            var s = new WorldSketch { Size = c.Size, LandFraction = landFraction, Ranges = ranges };
            s.Land = LandMask(c, rng.NextU32(), landFraction);
            s.BuildUplift(c);
            return s;
        }

        static bool[] LandMask(WorldConfig c, uint seed, float fraction)
        {
            int n = c.Size;
            var v = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, w = y / (float)n;
                    float wu = u + 0.25f * Noise.Fbm(seed ^ 0x51u, u * 2f, w * 2f, 3);
                    float ww = w + 0.25f * Noise.Fbm(seed ^ 0xA7u, u * 2f, w * 2f, 3);
                    float dx = wu - 0.5f, dy = ww - 0.5f;
                    float radial = (float)Math.Sqrt(dx * dx + dy * dy) / 0.5f;
                    float box = Math.Min(Math.Min(u, w), Math.Min(1 - u, 1 - w));
                    float edge = 0.7f * radial + 1.5f * (1 - Grid.Smoothstep(0f, 0.12f, box));
                    v[y * n + x] = 1.6f * Noise.Fbm(seed, wu * 3f, ww * 3f, 6) - edge;
                }
            var sorted = (float[])v.Clone();
            Array.Sort(sorted);
            float threshold = sorted[(int)((1 - fraction) * (sorted.Length - 1))];
            var land = new bool[n * n];
            for (int y = 2; y < n - 2; y++)
                for (int x = 2; x < n - 2; x++)
                    land[y * n + x] = v[y * n + x] > threshold;
            return land;
        }

        static RangeSketch RandomRange(WorldConfig c, bool[] land, ref Rng rng)
        {
            int n = c.Size;
            for (int attempt = 0; attempt < 50; attempt++)
            {
                int start = rng.Range(0, n * n);
                if (!land[start]) continue;
                int points = rng.Range(3, 6);
                float dx, dy, len;
                do { dx = rng.Range(-1f, 1f); dy = rng.Range(-1f, 1f); len = dx * dx + dy * dy; } while (len > 1f || len < 0.01f);
                len = (float)Math.Sqrt(len); dx /= len; dy /= len;
                float step = n * rng.Range(0.08f, 0.14f);
                var xs = new float[points]; var ys = new float[points];
                float x = start % n, y = start / n;
                for (int p = 0; p < points; p++)
                {
                    xs[p] = x; ys[p] = y;
                    float turn = rng.Range(-0.6f, 0.6f);
                    float tx = dx - turn * dy, ty = dy + turn * dx;
                    len = (float)Math.Sqrt(tx * tx + ty * ty); dx = tx / len; dy = ty / len;
                    x = Math.Max(4, Math.Min(n - 5, x + step * dx));
                    y = Math.Max(4, Math.Min(n - 5, y + step * dy));
                }
                return new RangeSketch { X = xs, Y = ys, Strength = rng.Range(0.6f, 1f), Width = n * rng.Range(0.03f, 0.06f) };
            }
            return null;
        }

        void BuildUplift(WorldConfig c)
        {
            int n = Size;
            Uplift = new float[n * n];
            uint detail = Rng.Stream(c.Seed, "uplift-detail").NextU32();
            var coast = CoastDistance(n, Land);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    if (!Land[i]) continue;
                    float u = 0.1f + 0.1f * Noise.Fbm(detail, x * 8f / n, y * 8f / n, 4);
                    foreach (var r in Ranges)
                    {
                        float d = DistanceToPolyline(r, x, y) / r.Width;
                        u = Math.Max(u, r.Strength * (float)Math.Exp(-d * d));
                    }
                    Uplift[i] = Math.Max(0, u) * Grid.Smoothstep(0, 6, coast[i]);
                }
        }

        static float DistanceToPolyline(RangeSketch r, float px, float py)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < r.X.Length; i++)
            {
                float ax = r.X[i], ay = r.Y[i], bx = r.X[i + 1] - ax, by = r.Y[i + 1] - ay;
                float len2 = bx * bx + by * by;
                float t = len2 > 0 ? Math.Max(0, Math.Min(1, ((px - ax) * bx + (py - ay) * by) / len2)) : 0;
                float dx = px - (ax + t * bx), dy = py - (ay + t * by);
                best = Math.Min(best, dx * dx + dy * dy);
            }
            return (float)Math.Sqrt(best);
        }

        // Chamfer distance in cells from each land cell to the nearest sea cell.
        public static float[] CoastDistance(int n, bool[] land)
        {
            var d = new float[n * n];
            for (int i = 0; i < d.Length; i++) d[i] = land[i] ? 1e9f : 0;
            for (int pass = 0; pass < 2; pass++)
            {
                int start = pass == 0 ? 0 : n * n - 1, step = pass == 0 ? 1 : -1;
                for (int i = start; i >= 0 && i < n * n; i += step)
                {
                    int x = i % n, y = i / n;
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = x + Grid.DX[k], ny = y + Grid.DY[k];
                        if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                        d[i] = Math.Min(d[i], d[ny * n + nx] + Grid.Dist[k]);
                    }
                }
            }
            return d;
        }
    }
}
