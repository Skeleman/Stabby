using System;
using System.Collections.Generic;

namespace Stabby.World
{
    // An island: centre and nominal radius in cells. The coastline is noise
    // around that circle, so area is roughly proportional to Radius squared.
    public sealed class LandmassSketch
    {
        public float X, Y, Radius;
    }

    // A mountain range as a ridge polyline in cell coordinates. Strength scales
    // uplift (0..1); Width is the range's half-width in cells.
    public sealed class RangeSketch
    {
        public float[] X, Y;
        public float Strength, Width;
    }

    // Where a major river must run, source first. Uplift is suppressed along it
    // so erosion cuts the valley and drainage gathers there.
    public sealed class CorridorSketch
    {
        public float[] X, Y;
        public float Width;
    }

    // Design intent that overrides climate: this biome inside the circle.
    public sealed class RegionSketch
    {
        public string Biome;
        public float X, Y, Radius;
    }

    // A fixed site (town, dungeon). Generation keeps it on dry land.
    public sealed class LandmarkSketch
    {
        public string Kind;
        public float X, Y;
    }

    // The coarse, editable layer. The lists are authored (generated or by hand);
    // Land and Uplift are derived by Build, so an edit regenerates a consistent
    // world instead of being patched in afterwards.
    public sealed class WorldSketch
    {
        public int Size;
        public List<LandmassSketch> Landmasses = new List<LandmassSketch>();
        public List<RangeSketch> Ranges = new List<RangeSketch>();
        public List<CorridorSketch> Corridors = new List<CorridorSketch>();
        public List<RegionSketch> Regions = new List<RegionSketch>();
        public List<LandmarkSketch> Landmarks = new List<LandmarkSketch>();

        public bool[] Land;
        public float[] Uplift;
        public bool[] Landmark;
        public float[] Valley, Coast;

        public const int LandmarkPadCells = 2;
        public const float CorridorDepth = 0.9f;

        public static WorldSketch Generate(WorldConfig c)
        {
            int n = c.Size;
            var s = new WorldSketch { Size = n };
            var layout = Rng.Stream(c.Seed, "landmasses");
            bool flipX = layout.NextFloat() < 0.5f, flipY = layout.NextFloat() < 0.5f, swap = layout.NextFloat() < 0.5f;
            float big = n * c.MainIslandRadius, small = big * c.SecondIslandScale;
            var centres = new[] { (0.32f, 0.36f, big), (0.73f, 0.69f, small) };
            for (int k = 0; k < c.Landmasses && k < centres.Length; k++)
            {
                var (u, v, r) = centres[k];
                u += layout.Range(-0.025f, 0.025f); v += layout.Range(-0.025f, 0.025f);
                if (swap) { var t = u; u = v; v = t; }
                if (flipX) u = 1 - u;
                if (flipY) v = 1 - v;
                s.Landmasses.Add(new LandmassSketch { X = u * n, Y = v * n, Radius = r });
            }
            s.Land = s.LandMask(c);

            var ranges = Rng.Stream(c.Seed, "ranges");
            var corridors = Rng.Stream(c.Seed, "corridors");
            var sites = Rng.Stream(c.Seed, "landmarks");
            for (int k = 0; k < s.Landmasses.Count; k++)
            {
                var m = s.Landmasses[k];
                int count = k == 0 ? ranges.Range(c.RangesMin, c.RangesMax + 1) : ranges.Range(1, c.RangesMin + 1);
                for (int i = 0; i < count; i++)
                {
                    var r = RandomRange(n, m, s.Land, ref ranges);
                    if (r != null) s.Ranges.Add(r);
                }
                var cor = RandomCorridor(n, m, s.Land, ref corridors);
                if (cor != null) s.Corridors.Add(cor);
                for (int i = 0; i < c.LandmarksPerIsland; i++)
                {
                    var l = RandomLandmark(n, m, s.Land, ref sites);
                    if (l != null) s.Landmarks.Add(l);
                }
            }
            s.Build(c);
            return s;
        }

        // Derives Land, Uplift and the landmark mask from the authored lists.
        public void Build(WorldConfig c)
        {
            Size = c.Size;
            Land = LandMask(c);
            int n = Size;
            Landmark = new bool[n * n];
            foreach (var l in Landmarks)
                for (int dy = -LandmarkPadCells; dy <= LandmarkPadCells; dy++)
                    for (int dx = -LandmarkPadCells; dx <= LandmarkPadCells; dx++)
                    {
                        int x = (int)l.X + dx, y = (int)l.Y + dy;
                        if (x < 3 || y < 3 || x >= n - 3 || y >= n - 3) continue;
                        Landmark[y * n + x] = true;
                        Land[y * n + x] = true;
                    }
            BuildUplift(c);
        }

        bool[] LandMask(WorldConfig c)
        {
            int n = c.Size;
            uint seed = Rng.Stream(c.Seed, "land-noise").NextU32();
            var land = new bool[n * n];
            for (int y = 3; y < n - 3; y++)
                for (int x = 3; x < n - 3; x++)
                {
                    float wx = x + n * 0.06f * Noise.Fbm(seed ^ 0x51u, x * 3f / n, y * 3f / n, 3);
                    float wy = y + n * 0.06f * Noise.Fbm(seed ^ 0xA7u, x * 3f / n, y * 3f / n, 3);
                    float core = float.MinValue;
                    foreach (var m in Landmasses)
                    {
                        float dx = wx - m.X, dy = wy - m.Y;
                        core = Math.Max(core, 1f - (float)Math.Sqrt(dx * dx + dy * dy) / m.Radius);
                    }
                    float box = Math.Min(Math.Min(x, y), Math.Min(n - 1 - x, n - 1 - y)) / (float)n;
                    float v = core + 0.45f * Noise.Fbm(seed, wx * 6f / n, wy * 6f / n, 6) - 2f * (1 - Grid.Smoothstep(0f, 0.06f, box));
                    land[y * n + x] = v > 0;
                }
            return land;
        }

        static RangeSketch RandomRange(int n, LandmassSketch m, bool[] land, ref Rng rng)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                float sx = m.X + m.Radius * rng.Range(-0.6f, 0.6f), sy = m.Y + m.Radius * rng.Range(-0.6f, 0.6f);
                if (!OnLand(n, land, sx, sy)) continue;
                int points = rng.Range(3, 6);
                RandomDirection(ref rng, out float dx, out float dy);
                float step = m.Radius * rng.Range(0.25f, 0.4f);
                var xs = new float[points]; var ys = new float[points];
                float x = sx, y = sy;
                for (int p = 0; p < points; p++)
                {
                    xs[p] = x; ys[p] = y;
                    Turn(ref rng, 0.6f, ref dx, ref dy);
                    x = Math.Max(4, Math.Min(n - 5, x + step * dx));
                    y = Math.Max(4, Math.Min(n - 5, y + step * dy));
                }
                return new RangeSketch { X = xs, Y = ys, Strength = rng.Range(0.6f, 1f), Width = m.Radius * rng.Range(0.1f, 0.2f) };
            }
            return null;
        }

        // Walks from the island's interior toward the sea and stops at the coast.
        static CorridorSketch RandomCorridor(int n, LandmassSketch m, bool[] land, ref Rng rng)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                float x = m.X + m.Radius * rng.Range(-0.25f, 0.25f), y = m.Y + m.Radius * rng.Range(-0.25f, 0.25f);
                if (!OnLand(n, land, x, y)) continue;
                RandomDirection(ref rng, out float dx, out float dy);
                float step = m.Radius * 0.2f;
                var xs = new List<float>(); var ys = new List<float>();
                for (int p = 0; p < 12; p++)
                {
                    xs.Add(x); ys.Add(y);
                    if (!OnLand(n, land, x, y)) break;
                    Turn(ref rng, 0.35f, ref dx, ref dy);
                    x = Math.Max(1, Math.Min(n - 2, x + step * dx));
                    y = Math.Max(1, Math.Min(n - 2, y + step * dy));
                }
                if (OnLand(n, land, xs[xs.Count - 1], ys[ys.Count - 1]) || xs.Count < 4) continue;
                return new CorridorSketch { X = xs.ToArray(), Y = ys.ToArray(), Width = m.Radius * 0.08f };
            }
            return null;
        }

        static LandmarkSketch RandomLandmark(int n, LandmassSketch m, bool[] land, ref Rng rng)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                float x = m.X + m.Radius * rng.Range(-0.7f, 0.7f), y = m.Y + m.Radius * rng.Range(-0.7f, 0.7f);
                if (OnLand(n, land, x, y)) return new LandmarkSketch { Kind = "town", X = (int)x, Y = (int)y };
            }
            return null;
        }

        static bool OnLand(int n, bool[] land, float x, float y)
        {
            int ix = (int)x, iy = (int)y;
            return ix >= 0 && iy >= 0 && ix < n && iy < n && land[iy * n + ix];
        }

        static void RandomDirection(ref Rng rng, out float dx, out float dy)
        {
            float len;
            do { dx = rng.Range(-1f, 1f); dy = rng.Range(-1f, 1f); len = dx * dx + dy * dy; } while (len > 1f || len < 0.01f);
            len = (float)Math.Sqrt(len); dx /= len; dy /= len;
        }

        static void Turn(ref Rng rng, float max, ref float dx, ref float dy)
        {
            float turn = rng.Range(-max, max);
            float tx = dx - turn * dy, ty = dy + turn * dx;
            float len = (float)Math.Sqrt(tx * tx + ty * ty);
            dx = tx / len; dy = ty / len;
        }

        void BuildUplift(WorldConfig c)
        {
            int n = Size;
            Uplift = new float[n * n];
            uint detail = Rng.Stream(c.Seed, "uplift-detail").NextU32();
            var coast = CoastDistance(n, Land);
            Coast = coast;
            Valley = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    if (!Land[i]) continue;
                    float u = 0.1f + 0.1f * Noise.Fbm(detail, x * 8f / n, y * 8f / n, 4);
                    foreach (var r in Ranges)
                    {
                        float d = DistanceToPolyline(r.X, r.Y, x, y) / r.Width;
                        u = Math.Max(u, r.Strength * (float)Math.Exp(-d * d));
                    }
                    foreach (var cor in Corridors)
                    {
                        float d = DistanceToPolyline(cor.X, cor.Y, x, y) / cor.Width;
                        float v = (float)Math.Exp(-d * d);
                        u *= 1 - CorridorDepth * v;
                        Valley[i] = Math.Max(Valley[i], v);
                    }
                    Uplift[i] = Math.Max(0, u) * Grid.Smoothstep(0, 6, coast[i]);
                }
        }

        public static float DistanceToPolyline(float[] xs, float[] ys, float px, float py)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < xs.Length; i++)
            {
                float ax = xs[i], ay = ys[i], bx = xs[i + 1] - ax, by = ys[i + 1] - ay;
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
