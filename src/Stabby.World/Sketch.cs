using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Stabby.World
{
    public sealed class LandmassSketch
    {
        public float X, Y, Radius;
        public List<LobeSketch> Lobes = new List<LobeSketch>();
        public float AxisX = 1, AxisY;

        public float Core(float x, float y)
        {
            if (Lobes.Count == 0) return 1f - Dist(x - X, y - Y) / Radius;
            float best = float.MinValue;
            foreach (var l in Lobes) best = Math.Max(best, 1f - Dist(x - l.X, y - l.Y) / l.Radius);
            return best;
        }

        public float Gap(float x, float y, float reach)
        {
            if (Lobes.Count == 0) return Dist(x - X, y - Y) - reach * Radius;
            float best = float.MaxValue;
            foreach (var l in Lobes) best = Math.Min(best, Dist(x - l.X, y - l.Y) - reach * l.Radius);
            return best;
        }

        static float Dist(float dx, float dy) => (float)Math.Sqrt(dx * dx + dy * dy);
    }

    public sealed class LobeSketch
    {
        public float X, Y, Radius;
    }

    public sealed class RangeSketch
    {
        public float[] X, Y;
        public float Strength, Width;
    }

    public sealed class CorridorSketch
    {
        public float[] X, Y;
        public float Width;
    }

    public sealed class RegionSketch
    {
        public string Biome;
        public float X, Y, Radius;
    }

    public sealed class LandmarkSketch
    {
        public string Kind;
        public float X, Y;
    }

    public sealed class ShoreSketch
    {
        public bool Cliff;
        public float X, Y, Radius;
    }

    public sealed class HighlandSketch
    {
        public bool Plateau;
        public float X, Y, Radius;
    }

    public sealed class WorldSketch
    {
        public int Size;
        public List<LandmassSketch> Landmasses = new List<LandmassSketch>();
        public List<LandmassSketch> Islets = new List<LandmassSketch>();
        public List<RangeSketch> Ranges = new List<RangeSketch>();
        public List<CorridorSketch> Corridors = new List<CorridorSketch>();
        public List<RegionSketch> Regions = new List<RegionSketch>();
        public List<LandmarkSketch> Landmarks = new List<LandmarkSketch>();
        public List<LandmassSketch> Lakes = new List<LandmassSketch>();
        public List<ShoreSketch> Shores = new List<ShoreSketch>();
        public List<HighlandSketch> Highlands = new List<HighlandSketch>();
        public List<MesaSketch> Mesas = new List<MesaSketch>();
        public List<VolcanoSketch> Volcanoes = new List<VolcanoSketch>();

        public bool[] Land;
        public float[] Uplift;
        public bool[] Landmark;
        public float[] Valley, Coast;
        public float[] ValleyRun;
        public float[] Cliff, Beach;
        public float[] Plateau;

        public const int LandmarkPadCells = 2;
        public const float CorridorDepth = 0.9f, LakeDepth = 0.95f;
        const float LakeTownClear = 0.4f, LakeTownCells = 6f, LakeCoastCells = 4f;
        const float CoastReach = 1.45f, WarpReach = 0.045f, Warp = 0.06f;
        const float IsletClear = 0.5f;
        const float HighlandCoastClear = 1.1f;
        const float PlateauEdgeNoise = 0.4f;

        public static WorldSketch Generate(WorldConfig c)
        {
            int n = c.Size;
            var s = new WorldSketch { Size = n };
            var layout = Rng.Stream(c.Seed, "landmasses");
            bool flipX = layout.NextFloat() < 0.5f, flipY = layout.NextFloat() < 0.5f, swap = layout.NextFloat() < 0.5f;
            float big = n * c.MainIslandRadius, small = big * c.SecondIslandScale;
            var centres = new[] { (0.37f, 0.39f, big), (0.7f, 0.67f, small) };
            for (int k = 0; k < c.Landmasses && k < centres.Length; k++)
            {
                var (u, v, r) = centres[k];
                u += layout.Range(-0.025f, 0.025f); v += layout.Range(-0.025f, 0.025f);
                if (swap) { var t = u; u = v; v = t; }
                if (flipX) u = 1 - u;
                if (flipY) v = 1 - v;
                s.Landmasses.Add(new LandmassSketch { X = u * n, Y = v * n, Radius = r });
            }
            var lobes = Rng.Stream(c.Seed, "lobes");
            foreach (var m in s.Landmasses) AddLobes(m, s.Landmasses.IndexOf(m) == 0 ? c.MainLobes : c.SecondLobes, ref lobes);
            var islets = Rng.Stream(c.Seed, "islets");
            int isletCount = islets.Range(c.IsletsMin, c.IsletsMax + 1);
            for (int k = 0; k < isletCount; k++)
            {
                var islet = RandomIslet(n, c, s, k > 0 && islets.NextFloat() < c.IsletClusterChance, ref islets);
                if (islet != null) s.Islets.Add(islet);
            }
            s.Land = s.LandMask(c);

            var ranges = Rng.Stream(c.Seed, "ranges");
            var corridors = Rng.Stream(c.Seed, "corridors");
            var sites = Rng.Stream(c.Seed, "landmarks");
            var inland = CoastDistance(n, s.Land);
            var shores = Rng.Stream(c.Seed, "shores");
            var ponds = Rng.Stream(c.Seed, "ponds");
            for (int k = 0; k < s.Landmasses.Count; k++)
            {
                var m = s.Landmasses[k];
                int firstRange = s.Ranges.Count;
                CorridorSketch cor = null;
                if (k == 0)
                {
                    var bends = Rng.Stream(c.Seed, "valley-bends");
                    cor = AddValley(n, m, s.Land, inland, s.Ranges, ref ranges, ref bends);
                    var lakes = Rng.Stream(c.Seed, "lakes");
                    if (cor != null) s.Lakes.Add(ValleyLake(n, m, cor, inland, c.MainLakeRadius, ref lakes));
                    if (cor != null) AddHighlands(n, c, m, s, s.Ranges[firstRange], cor, inland);
                }
                else
                    for (int i = ranges.Range(c.RangesMin, c.RangesMax + 1); i > 0; i--)
                    {
                        var r = RandomRange(n, m, s.Land, ref ranges);
                        if (r != null) s.Ranges.Add(r);
                    }
                if (cor == null) cor = RandomCorridor(n, m, s.Land, inland, s.Ranges, ref corridors);
                if (cor != null) s.Corridors.Add(cor);
                AddShores(n, m, s, firstRange, cor, ref shores);
                for (int i = ponds.Range(c.PondsMin, c.PondsMax + 1); i > 0; i--)
                {
                    var p = RandomPond(n, c, m, s, inland, ref ponds);
                    if (p != null) s.Lakes.Add(p);
                }
                for (int i = 0; i < c.LandmarksPerIsland; i++)
                {
                    var l = RandomLandmark(n, m, s.Land, s.Lakes, ref sites);
                    if (l != null) s.Landmarks.Add(l);
                }
            }
            foreach (var m in s.Islets)
            {
                if (ranges.NextFloat() < c.IsletRidgeChance)
                {
                    var r = RandomRange(n, m, s.Land, ref ranges);
                    if (r != null) s.Ranges.Add(r);
                }
                var l = RandomLandmark(n, m, s.Land, s.Lakes, ref sites);
                if (l != null) s.Landmarks.Add(l);
            }
            s.Build(c, s.Land);
            return s;
        }

        public void Build(WorldConfig c, bool[] land = null)
        {
            Size = c.Size;
            Land = land ?? LandMask(c);
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
            BuildShores();
            BuildPlateaus(c);
            BuildUplift(c);
        }

        void BuildShores()
        {
            int n = Size;
            Cliff = new float[n * n];
            Beach = new float[n * n];
            foreach (var sh in Shores)
            {
                var w = sh.Cliff ? Cliff : Beach;
                int r = (int)Math.Ceiling(sh.Radius);
                for (int y = Math.Max(0, (int)sh.Y - r); y <= Math.Min(n - 1, (int)sh.Y + r); y++)
                    for (int x = Math.Max(0, (int)sh.X - r); x <= Math.Min(n - 1, (int)sh.X + r); x++)
                    {
                        float d = (float)Math.Sqrt((x - sh.X) * (x - sh.X) + (y - sh.Y) * (y - sh.Y)) / sh.Radius;
                        w[y * n + x] = Math.Max(w[y * n + x], 1f - Grid.Smoothstep(0.6f, 1f, d));
                    }
            }
            for (int i = 0; i < n * n; i++) Cliff[i] *= 1f - Beach[i];
            foreach (var hl in Highlands)
            {
                float reach = hl.Plateau ? 1.4f : 1.6f;
                int r = (int)Math.Ceiling(hl.Radius * (reach + 0.4f));
                for (int y = Math.Max(0, (int)hl.Y - r); y <= Math.Min(n - 1, (int)hl.Y + r); y++)
                    for (int x = Math.Max(0, (int)hl.X - r); x <= Math.Min(n - 1, (int)hl.X + r); x++)
                    {
                        float d = (float)Math.Sqrt((x - hl.X) * (x - hl.X) + (y - hl.Y) * (y - hl.Y)) / hl.Radius;
                        Cliff[y * n + x] *= Grid.Smoothstep(reach, reach + 0.4f, d);
                    }
            }
        }

        void BuildPlateaus(WorldConfig c)
        {
            int n = Size;
            Plateau = new float[n * n];
            uint edge = Rng.Stream(c.Seed, "plateau-edge").NextU32();
            foreach (var hl in Highlands)
            {
                if (!hl.Plateau) continue;
                int r = (int)Math.Ceiling(hl.Radius * (1 + PlateauEdgeNoise));
                for (int y = Math.Max(0, (int)hl.Y - r); y <= Math.Min(n - 1, (int)hl.Y + r); y++)
                    for (int x = Math.Max(0, (int)hl.X - r); x <= Math.Min(n - 1, (int)hl.X + r); x++)
                    {
                        float d = (float)Math.Sqrt((x - hl.X) * (x - hl.X) + (y - hl.Y) * (y - hl.Y)) / hl.Radius;
                        d /= 1 + PlateauEdgeNoise * Noise.Fbm(edge, x * 26f / n, y * 26f / n, 3);
                        Plateau[y * n + x] = Math.Max(Plateau[y * n + x], 1f - Grid.Smoothstep(0.6f, 1f, d));
                    }
            }
        }

        bool[] LandMask(WorldConfig c)
        {
            int n = c.Size;
            uint seed = Rng.Stream(c.Seed, "land-noise").NextU32();
            var land = new bool[n * n];
            var owner = new int[n * n];
            Parallel.For(3, n - 3, y =>
            {
                for (int x = 3; x < n - 3; x++)
                {
                    float wx = x + n * Warp * Noise.Fbm(seed ^ 0x51u, x * 3f / n, y * 3f / n, 3);
                    float wy = y + n * Warp * Noise.Fbm(seed ^ 0xA7u, x * 3f / n, y * 3f / n, 3);
                    float core = float.MinValue;
                    int best = -1;
                    for (int k = 0; k < Landmasses.Count; k++)
                    {
                        float v = Landmasses[k].Core(wx, wy);
                        if (v > core) { core = v; best = k; }
                    }
                    foreach (var m in Islets)
                    {
                        float v = m.Core(x, y);
                        if (v > core) { core = v; best = -1; }
                    }
                    float box = Math.Min(Math.Min(x, y), Math.Min(n - 1 - x, n - 1 - y)) / (float)n;
                    float val = core + 0.45f * Noise.Fbm(seed, wx * 6f / n, wy * 6f / n, 6) + 0.15f * Noise.Fbm(seed ^ 0x77u, wx * 18f / n, wy * 18f / n, 4) - 2f * (1 - Grid.Smoothstep(0f, 0.06f, box));
                    land[y * n + x] = val > 0;
                    owner[y * n + x] = best;
                }
            });

            float channel = c.ChannelFraction * n;
            for (int k = 1; k < Landmasses.Count; k++)
            {
                var notEarlier = new bool[n * n];
                for (int i = 0; i < n * n; i++) notEarlier[i] = !(land[i] && owner[i] >= 0 && owner[i] < k);
                var d = CoastDistance(n, notEarlier);
                for (int i = 0; i < n * n; i++)
                    if (land[i] && owner[i] == k && d[i] < channel) land[i] = false;
            }
            return land;
        }

        static void AddLobes(LandmassSketch m, int count, ref Rng rng)
        {
            m.Lobes.Add(new LobeSketch { X = m.X, Y = m.Y, Radius = m.Radius * 0.7f });
            RandomDirection(ref rng, out float ax, out float ay);
            m.AxisX = ax; m.AxisY = ay;
            for (int k = 1; k < count; k++)
            {
                RandomDirection(ref rng, out float dx, out float dy);
                float along = dx * ax + dy * ay, across = -dx * ay + dy * ax;
                along *= 1.3f; across *= 0.7f;
                float dist = m.Radius * rng.Range(0.4f, 0.72f);
                m.Lobes.Add(new LobeSketch
                {
                    X = m.X + dist * (along * ax - across * ay),
                    Y = m.Y + dist * (along * ay + across * ax),
                    Radius = m.Radius * rng.Range(0.3f, 0.5f),
                });
            }
        }

        static LandmassSketch RandomIslet(int n, WorldConfig c, WorldSketch s, bool cluster, ref Rng rng)
        {
            for (int attempt = 0; attempt < 80; attempt++)
            {
                float x = n * rng.Range(0.1f, 0.9f), y = n * rng.Range(0.1f, 0.9f), r = n * rng.Range(c.IsletRadiusMin, c.IsletRadiusMax);
                float clear = 1.9f * r + 3f, nearest = float.MaxValue, mate = float.MaxValue;
                bool ok = true;
                foreach (var m in s.Landmasses)
                {
                    float g = m.Gap(x, y, CoastReach) - WarpReach * n;
                    if (g < clear) ok = false;
                    nearest = Math.Min(nearest, g);
                }
                foreach (var o in s.Islets)
                {
                    float g = o.Gap(x, y, CoastReach);
                    if (g < IsletClear * r + 3f) ok = false;
                    mate = Math.Min(mate, g);
                }
                if (!ok || nearest >= c.IsletSpread * n || (cluster && mate > c.IsletClusterGap * n)) continue;
                var islet = new LandmassSketch { X = x, Y = y, Radius = r };
                AddLobes(islet, rng.Range(2, 4), ref rng);
                return islet;
            }
            return null;
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

        static CorridorSketch AddValley(int n, LandmassSketch m, bool[] land, float[] inland, List<RangeSketch> ranges, ref Rng rng, ref Rng bends)
        {
            for (int attempt = 0; attempt < 16; attempt++)
            {
                int before = ranges.Count;
                var cor = TryValley(n, m, land, inland, ranges, attempt % 2 == 1, ref rng, ref bends);
                if (cor != null) return cor;
                ranges.RemoveRange(before, ranges.Count - before);
            }
            return null;
        }

        static CorridorSketch TryValley(int n, LandmassSketch m, bool[] land, float[] inland, List<RangeSketch> ranges, bool far, ref Rng rng, ref Rng bends)
        {
            float ax = m.AxisX, ay = m.AxisY;
            if ((RunToSea(n, land, m.X, m.Y, -ax, -ay) < RunToSea(n, land, m.X, m.Y, ax, ay)) != far) { ax = -ax; ay = -ay; }
            float px = -ay, py = ax;
            float half = m.Radius * rng.Range(0.3f, 0.38f);
            float back = m.Radius * rng.Range(0.55f, 0.8f), front = m.Radius * rng.Range(0.45f, 0.7f);
            rng.NextFloat();
            const bool fork = true;
            const int Points = 5;
            var mx = new float[Points]; var my = new float[Points];
            for (int side = -1; side <= 1; side += 2)
            {
                float dx = ax, dy = ay;
                Turn(ref rng, fork ? 0.06f : 0.15f, ref dx, ref dy);
                float ox = fork ? m.X - back * ax : m.X, oy = fork ? m.Y - back * ay : m.Y, t0 = fork ? 0 : -back;
                var xs = new float[Points]; var ys = new float[Points];
                for (int p = 0; p < Points; p++)
                {
                    float f = (float)p / (Points - 1), t = t0 + (back + front) * f;
                    float off = side * half * (fork ? 1.5f * f : 1f) + (fork && p == 0 ? 0 : m.Radius * rng.Range(-0.07f, 0.07f));
                    xs[p] = Math.Max(4, Math.Min(n - 5, ox + t * dx + off * px));
                    ys[p] = Math.Max(4, Math.Min(n - 5, oy + t * dy + off * py));
                    mx[p] += xs[p] * 0.5f; my[p] += ys[p] * 0.5f;
                }
                ranges.Add(new RangeSketch { X = xs, Y = ys, Strength = rng.Range(0.75f, 1f), Width = m.Radius * rng.Range(0.1f, 0.14f) });
            }
            int head = fork ? 1 : 0;
            var cx = new List<float>(mx).GetRange(head, Points - head); var cy = new List<float>(my).GetRange(head, Points - head);
            if (!OnLand(n, land, mx[head], my[head])) return null;
            int open = Points - 1 - head;
            for (int tries = 6; tries >= 0; tries--)
            {
                bool bent = tries > 0;
                var runX = new List<float>(cx); var runY = new List<float>(cy);
                float step = m.Radius * (bent ? 0.06f : 0.15f), x = mx[Points - 1], y = my[Points - 1], hx = ax, hy = ay;
                float sway = bends.NextFloat() < 0.5f ? -1 : 1;
                for (int k = 0; k < (bent ? 75 : 30) && OnLand(n, land, x, y); k++)
                {
                    if (bent)
                    {
                        if (bends.NextFloat() < 0.3f) sway = -sway;
                        float turn = sway * bends.Range(0.25f, 0.55f);
                        float tx = hx - turn * hy, ty = hy + turn * hx;
                        hx = tx + 0.15f * (ax - tx); hy = ty + 0.15f * (ay - ty);
                        float len = (float)Math.Sqrt(hx * hx + hy * hy);
                        hx /= len; hy /= len;
                    }
                    x = Math.Max(1, Math.Min(n - 2, x + step * hx));
                    y = Math.Max(1, Math.Min(n - 2, y + step * hy));
                    runX.Add(x); runY.Add(y);
                }
                if (OnLand(n, land, x, y)) continue;
                var tail = new CorridorSketch { X = runX.GetRange(open, runX.Count - open).ToArray(), Y = runY.GetRange(open, runY.Count - open).ToArray() };
                if (!SkirtsCoast(n, tail, inland)) return new CorridorSketch { X = runX.ToArray(), Y = runY.ToArray(), Width = m.Radius * 0.08f };
            }
            return null;
        }

        static void AddHighlands(int n, WorldConfig c, LandmassSketch m, WorldSketch s, RangeSketch ridge, CorridorSketch cor, float[] inland)
        {
            float ax = ridge.X[0], ay = ridge.Y[0];
            float bx = ax - cor.X[cor.X.Length - 1], by = ay - cor.Y[cor.Y.Length - 1];
            float len = (float)Math.Sqrt(bx * bx + by * by);
            if (len < 1e-3f) return;
            bx /= len; by /= len;
            float pr = m.Radius * c.PeakRadius;
            for (int k = 0; k < 40 && Room(n, inland, ax, ay) < pr * 1.5f; k++)
            {
                float tx = m.X - ax, ty = m.Y - ay, tl = (float)Math.Sqrt(tx * tx + ty * ty);
                if (tl < 1) break;
                ax += tx / tl; ay += ty / tl;
            }
            s.Highlands.Add(new HighlandSketch { X = ax, Y = ay, Radius = pr });
            var rng = Rng.Stream(c.Seed, "highlands");
            float want = m.Radius * rng.Range(c.HighlandRadiusMin, c.HighlandRadiusMax);
            float best = 0, bestX = 0, bestY = 0, bestR = 0;
            float dx = bx, dy = by;
            for (int k = 0; k < 16; k++)
            {
                float facing = dx * bx + dy * by;
                if (facing > -0.2f)
                    for (float r = want; r >= m.Radius * c.HighlandRadiusMin * 0.5f; r *= 0.85f)
                    {
                        float x = ax + dx * (r * 0.75f + pr * 0.5f), y = ay + dy * (r * 0.75f + pr * 0.5f);
                        if (Room(n, inland, x, y) < r * HighlandCoastClear || NearCorridor(cor, x, y, r * 0.7f, 1)) continue;
                        bool clear = true;
                        foreach (var l in s.Lakes)
                            foreach (var b in l.Lobes)
                                if ((b.X - x) * (b.X - x) + (b.Y - y) * (b.Y - y) < (b.Radius + r) * (b.Radius + r)) clear = false;
                        if (!clear) continue;
                        float score = r * (1.5f + facing);
                        if (score > best) { best = score; bestX = x; bestY = y; bestR = r; }
                        break;
                    }
                float tx = dx * 0.92387953f - dy * 0.38268343f;
                dy = dx * 0.38268343f + dy * 0.92387953f; dx = tx;
            }
            if (bestR > 0) s.Highlands.Add(new HighlandSketch { Plateau = true, X = bestX, Y = bestY, Radius = bestR });
        }

        static LandmassSketch ValleyLake(int n, LandmassSketch m, CorridorSketch cor, float[] inland, float size, ref Rng rng)
        {
            int a = Math.Min(1, cor.X.Length - 2);
            float t = rng.Range(0.2f, 0.8f);
            float x = cor.X[a] + (cor.X[a + 1] - cor.X[a]) * t, y = cor.Y[a] + (cor.Y[a + 1] - cor.Y[a]) * t;
            float dx = cor.X[a + 1] - cor.X[a], dy = cor.Y[a + 1] - cor.Y[a], len = (float)Math.Sqrt(dx * dx + dy * dy);
            dx /= len; dy /= len;
            float r = Math.Max(2f, Math.Min(m.Radius * size * rng.Range(0.8f, 1.1f), Room(n, inland, x, y)));
            var lake = new LandmassSketch { X = x, Y = y, Radius = r, AxisX = dx, AxisY = dy };
            lake.Lobes.Add(new LobeSketch { X = x, Y = y, Radius = r });
            for (int side = -1; side <= 1; side += 2)
            {
                float along = 0, lr = r;
                for (int k = rng.Range(1, 3); k > 0; k--)
                {
                    float next = lr * rng.Range(0.6f, 0.85f);
                    along += (lr + next) * rng.Range(0.55f, 0.75f);
                    lr = next;
                    float across = r * rng.Range(-0.35f, 0.35f);
                    float lx = x + side * along * dx - across * dy, ly = y + side * along * dy + across * dx;
                    if (lr > Room(n, inland, lx, ly)) break;
                    lake.Lobes.Add(new LobeSketch { X = lx, Y = ly, Radius = lr });
                }
            }
            return lake;
        }

        static LandmassSketch RandomPond(int n, WorldConfig c, LandmassSketch m, WorldSketch s, float[] inland, ref Rng rng)
        {
            float r = Math.Max(2f, m.Radius * rng.Range(c.PondRadiusMin, c.PondRadiusMax));
            for (int attempt = 0; attempt < 40; attempt++)
            {
                float x = m.X + m.Radius * rng.Range(-1f, 1f), y = m.Y + m.Radius * rng.Range(-1f, 1f);
                if (Room(n, inland, x, y) < r + 1) continue;
                bool clear = true;
                foreach (var g in s.Ranges)
                    if (DistanceToPolyline(g.X, g.Y, x, y) < g.Width * 2 + r) { clear = false; break; }
                foreach (var l in s.Lakes)
                    foreach (var b in l.Lobes)
                        if ((b.X - x) * (b.X - x) + (b.Y - y) * (b.Y - y) < (b.Radius + r * 3) * (b.Radius + r * 3)) clear = false;
                foreach (var h in s.Highlands)
                    if (h.Plateau && (h.X - x) * (h.X - x) + (h.Y - y) * (h.Y - y) < (h.Radius + r * 2) * (h.Radius + r * 2)) clear = false;
                if (!clear) continue;
                var pond = new LandmassSketch { X = x, Y = y, Radius = r, AxisX = 1, AxisY = 0 };
                pond.Lobes.Add(new LobeSketch { X = x, Y = y, Radius = r });
                RandomDirection(ref rng, out float dx, out float dy);
                float lr = r * rng.Range(0.5f, 0.8f), off = r * rng.Range(0.6f, 0.9f);
                if (Room(n, inland, x + dx * off, y + dy * off) >= lr + 1)
                    pond.Lobes.Add(new LobeSketch { X = x + dx * off, Y = y + dy * off, Radius = lr });
                return pond;
            }
            return null;
        }

        static float Room(int n, float[] inland, float x, float y)
        {
            int ix = (int)x, iy = (int)y;
            return ix < 0 || iy < 0 || ix >= n || iy >= n ? 0 : inland[iy * n + ix] - LakeCoastCells;
        }

        static float RunToSea(int n, bool[] land, float x, float y, float dx, float dy)
        {
            int k = 0;
            while (k < n && OnLand(n, land, x + k * dx, y + k * dy)) k++;
            return k;
        }

        static void AddShores(int n, LandmassSketch m, WorldSketch s, int firstRange, CorridorSketch cor, ref Rng rng)
        {
            float cx = float.NaN, cy = 0, best = float.MaxValue, reach = m.Radius, clear = m.Radius * 0.55f;
            var mine = Component(n, s.Land, (int)m.X, (int)m.Y);
            for (int r = firstRange; r < s.Ranges.Count; r++)
            {
                var g = s.Ranges[r];
                for (int end = 0; end < 2; end++)
                {
                    int p = end == 0 ? 0 : g.X.Length - 1;
                    if (NearestCoast(n, mine, g.X[p], g.Y[p], reach, out float sx, out float sy, out float d) && d < best
                        && !OtherLandNear(n, s.Land, mine, sx, sy, clear) && !NearCorridor(cor, sx, sy, clear) && !UnderHighland(s, sx, sy))
                    { best = d; cx = sx; cy = sy; }
                }
            }
            if (float.IsNaN(cx))
            {
                RandomDirection(ref rng, out float dx, out float dy);
                for (int turn = 0; turn < 16; turn++)
                {
                    float k = RunToSea(n, s.Land, m.X, m.Y, dx, dy);
                    float px = m.X + k * dx, py = m.Y + k * dy;
                    if (turn == 0) { cx = px; cy = py; }
                    if (!OtherLandNear(n, s.Land, mine, px, py, clear) && !NearCorridor(cor, px, py, clear) && !UnderHighland(s, px, py))
                    { cx = px; cy = py; break; }
                    float tx = dx * 0.92387953f - dy * 0.38268343f;
                    dy = dx * 0.38268343f + dy * 0.92387953f; dx = tx;
                }
            }
            var cliff = new ShoreSketch { Cliff = true, X = cx, Y = cy, Radius = m.Radius * rng.Range(0.4f, 0.55f) };
            s.Shores.Add(cliff);
            float bx, by;
            int last = cor == null ? 0 : cor.X.Length - 1;
            if (cor == null || !NearestCoast(n, mine, cor.X[last], cor.Y[last], reach, out bx, out by, out _))
            {
                float ax = m.X - cx, ay = m.Y - cy, al = (float)Math.Sqrt(ax * ax + ay * ay);
                if (al < 1e-3f) return;
                ax /= al; ay /= al;
                float k = RunToSea(n, s.Land, m.X, m.Y, ax, ay);
                bx = m.X + k * ax; by = m.Y + k * ay;
            }
            float gap = (float)Math.Sqrt((bx - cx) * (bx - cx) + (by - cy) * (by - cy)) - cliff.Radius;
            float radius = Math.Min(m.Radius * rng.Range(0.5f, 0.75f), gap);
            if (radius > m.Radius * 0.2f) s.Shores.Add(new ShoreSketch { X = bx, Y = by, Radius = radius });
        }

        static bool OtherLandNear(int n, bool[] land, bool[] mine, float x, float y, float r)
        {
            int ri = (int)Math.Ceiling(r);
            for (int yy = Math.Max(0, (int)y - ri); yy <= Math.Min(n - 1, (int)y + ri); yy++)
                for (int xx = Math.Max(0, (int)x - ri); xx <= Math.Min(n - 1, (int)x + ri); xx++)
                {
                    int i = yy * n + xx;
                    if (land[i] && !mine[i] && (xx - x) * (xx - x) + (yy - y) * (yy - y) <= r * r) return true;
                }
            return false;
        }

        static bool UnderHighland(WorldSketch s, float x, float y)
        {
            foreach (var hl in s.Highlands)
            {
                float r = hl.Radius * (hl.Plateau ? 1.8f : 2f);
                if ((hl.X - x) * (hl.X - x) + (hl.Y - y) * (hl.Y - y) < r * r) return true;
            }
            return false;
        }

        static bool NearCorridor(CorridorSketch cor, float x, float y, float r, int from = 0)
        {
            if (cor == null) return false;
            float rr = r + cor.Width;
            for (int k = from; k < cor.X.Length; k++)
                if ((cor.X[k] - x) * (cor.X[k] - x) + (cor.Y[k] - y) * (cor.Y[k] - y) <= rr * rr) return true;
            return false;
        }

        static bool NearestCoast(int n, bool[] mine, float x, float y, float reach, out float sx, out float sy, out float dist)
        {
            sx = sy = 0; float best = reach * reach;
            int r = (int)Math.Ceiling(reach), ix = (int)x, iy = (int)y;
            for (int yy = Math.Max(0, iy - r); yy <= Math.Min(n - 1, iy + r); yy++)
                for (int xx = Math.Max(0, ix - r); xx <= Math.Min(n - 1, ix + r); xx++)
                {
                    if (mine[yy * n + xx] || !NextToLand(n, mine, xx, yy)) continue;
                    float d = (xx - x) * (xx - x) + (yy - y) * (yy - y);
                    if (d < best) { best = d; sx = xx; sy = yy; }
                }
            dist = (float)Math.Sqrt(best);
            return best < reach * reach;
        }

        static bool[] Component(int n, bool[] land, int x, int y)
        {
            var mine = new bool[n * n];
            if (!land[y * n + x]) return mine;
            var queue = new Queue<int>();
            queue.Enqueue(y * n + x);
            mine[y * n + x] = true;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), cx = i % n, cy = i / n;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + Grid.DX[k], ny = cy + Grid.DY[k], j = ny * n + nx;
                    if (nx < 0 || ny < 0 || nx >= n || ny >= n || mine[j] || !land[j]) continue;
                    mine[j] = true;
                    queue.Enqueue(j);
                }
            }
            return mine;
        }

        static bool NextToLand(int n, bool[] land, int x, int y)
        {
            for (int k = 0; k < 8; k++)
            {
                int nx = x + Grid.DX[k], ny = y + Grid.DY[k];
                if (nx >= 0 && ny >= 0 && nx < n && ny < n && land[ny * n + nx]) return true;
            }
            return false;
        }

        static CorridorSketch RandomCorridor(int n, LandmassSketch m, bool[] land, float[] inland, List<RangeSketch> ranges, ref Rng rng)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                float x = 0, y = 0, deepest = 0;
                for (int k = 0; k < 8; k++)
                {
                    float sx = m.X + m.Radius * rng.Range(-0.6f, 0.6f), sy = m.Y + m.Radius * rng.Range(-0.6f, 0.6f);
                    if (!OnLand(n, land, sx, sy)) continue;
                    float d = inland[(int)sy * n + (int)sx];
                    if (d > deepest) { deepest = d; x = sx; y = sy; }
                }
                if (deepest == 0) continue;
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
                var cor = new CorridorSketch { X = xs.ToArray(), Y = ys.ToArray(), Width = m.Radius * 0.08f };
                if (CrossesRange(cor, ranges) || SkirtsCoast(n, cor, inland)) continue;
                return cor;
            }
            return null;
        }

        static bool SkirtsCoast(int n, CorridorSketch cor, float[] inland)
        {
            float remaining = 0;
            for (int p = cor.X.Length - 2; p >= 0; p--)
            {
                float dx = cor.X[p + 1] - cor.X[p], dy = cor.Y[p + 1] - cor.Y[p];
                remaining += (float)Math.Sqrt(dx * dx + dy * dy);
                if (inland[(int)cor.Y[p] * n + (int)cor.X[p]] < 0.5f * remaining - 1) return true;
            }
            return false;
        }

        static bool CrossesRange(CorridorSketch cor, List<RangeSketch> ranges)
        {
            foreach (var r in ranges)
                for (int p = 0; p < cor.X.Length; p++)
                    if (DistanceToPolyline(r.X, r.Y, cor.X[p], cor.Y[p]) < r.Width + cor.Width) return true;
            return false;
        }

        static LandmarkSketch RandomLandmark(int n, LandmassSketch m, bool[] land, List<LandmassSketch> lakes, ref Rng rng)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                float x = m.X + m.Radius * rng.Range(-0.7f, 0.7f), y = m.Y + m.Radius * rng.Range(-0.7f, 0.7f);
                bool wet = false;
                foreach (var lk in lakes) wet |= lk.Core(x, y) > -LakeTownClear || lk.Gap(x, y, 1) < LakeTownCells;
                if (!wet && OnLand(n, land, x, y)) return new LandmarkSketch { Kind = "town", X = (int)x, Y = (int)y };
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
            ValleyRun = new float[n * n];
            uint crest = Rng.Stream(c.Seed, "range-crests").NextU32();
            var fromSummit = new bool[Ranges.Count];
            var rx = new float[Ranges.Count][];
            var ry = new float[Ranges.Count][];
            for (int k = 0; k < Ranges.Count; k++)
            {
                rx[k] = Ranges[k].X; ry[k] = Ranges[k].Y;
                foreach (var hl in Highlands)
                    if (!hl.Plateau && !fromSummit[k] && (Ranges[k].X[0] - hl.X) * (Ranges[k].X[0] - hl.X) + (Ranges[k].Y[0] - hl.Y) * (Ranges[k].Y[0] - hl.Y) <= 4 * hl.Radius * hl.Radius)
                    {
                        fromSummit[k] = true;
                        rx[k] = new float[Ranges[k].X.Length + 1]; ry[k] = new float[Ranges[k].Y.Length + 1];
                        rx[k][0] = hl.X; ry[k][0] = hl.Y;
                        Array.Copy(Ranges[k].X, 0, rx[k], 1, Ranges[k].X.Length);
                        Array.Copy(Ranges[k].Y, 0, ry[k], 1, Ranges[k].Y.Length);
                    }
            }
            Parallel.For(0, n, y =>
            {
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    if (!Land[i]) continue;
                    float u = 0.1f + 0.1f * Noise.Fbm(detail, x * 8f / n, y * 8f / n, 4);
                    for (int k = 0; k < Ranges.Count; k++)
                    {
                        var r = Ranges[k];
                        float dist = DistanceToPolyline(rx[k], ry[k], x, y, out float along);
                        if (dist > r.Width * 4) continue;
                        float peaks = 1 + c.RangeCrestNoise * Noise.Fbm(crest + (uint)k, x * 20f / n, y * 20f / n, 3);
                        float strength = r.Strength * peaks, width = r.Width * peaks;
                        if (fromSummit[k]) { strength *= 1.25f - c.RangeTaper * along; width *= 1.7f * (1 - c.RangeNarrowing * along); }
                        float d = dist / width;
                        u = Math.Max(u, strength * (float)FMath.Exp(-d * d));
                    }
                    foreach (var hl in Highlands)
                    {
                        float d = (float)Math.Sqrt((x - hl.X) * (x - hl.X) + (y - hl.Y) * (y - hl.Y)) / hl.Radius;
                        u = Math.Max(u, hl.Plateau ? c.ShoulderStrength * (1 - Grid.Smoothstep(0.8f, 1.8f, d))
                            : c.PeakStrength * (1 - Grid.Smoothstep(0, 1, d)));
                    }
                    float clear = 1;
                    foreach (var hl in Highlands)
                        if (!hl.Plateau)
                            clear = Math.Min(clear, Grid.Smoothstep(0.6f, 1.2f, (float)Math.Sqrt((x - hl.X) * (x - hl.X) + (y - hl.Y) * (y - hl.Y)) / hl.Radius));
                    foreach (var cor in Corridors)
                    {
                        float d = DistanceToPolyline(cor.X, cor.Y, x, y) / cor.Width;
                        float v = clear * (float)FMath.Exp(-d * d);
                        u *= 1 - CorridorDepth * v;
                        if (v > Valley[i]) { Valley[i] = v; ValleyRun[i] = RemainingAlong(cor.X, cor.Y, x, y); }
                    }
                    foreach (var lk in Lakes) u *= 1 - LakeDepth * Grid.Smoothstep(-0.8f, 0.2f, lk.Core(x, y));
                    u *= 1 - Grid.Smoothstep(0.6f, 1f, Cliff[i]);
                    float taper = Grid.Smoothstep(0, 6, coast[i]);
                    Uplift[i] = Math.Max(0, u) * (taper + (1 - taper) * Cliff[i]);
                }
            });
        }

        public static float DistanceToPolyline(float[] xs, float[] ys, float px, float py) => DistanceToPolyline(xs, ys, px, py, out _);

        public static float DistanceToPolyline(float[] xs, float[] ys, float px, float py, out float along)
        {
            float total = 0, at = 0;
            float best = float.MaxValue;
            for (int i = 0; i + 1 < xs.Length; i++)
            {
                float ax = xs[i], ay = ys[i], bx = xs[i + 1] - ax, by = ys[i + 1] - ay;
                float len2 = bx * bx + by * by;
                float t = len2 > 0 ? Math.Max(0, Math.Min(1, ((px - ax) * bx + (py - ay) * by) / len2)) : 0;
                float dx = px - (ax + t * bx), dy = py - (ay + t * by), len = (float)Math.Sqrt(len2);
                if (dx * dx + dy * dy < best) { best = dx * dx + dy * dy; at = total + t * len; }
                total += len;
            }
            along = total > 0 ? at / total : 0;
            return (float)Math.Sqrt(best);
        }

        static float RemainingAlong(float[] xs, float[] ys, float px, float py)
        {
            float best = float.MaxValue, along = 0, at = 0;
            for (int i = 0; i + 1 < xs.Length; i++)
            {
                float ax = xs[i], ay = ys[i], bx = xs[i + 1] - ax, by = ys[i + 1] - ay;
                float len2 = bx * bx + by * by, len = (float)Math.Sqrt(len2);
                float t = len2 > 0 ? Math.Max(0, Math.Min(1, ((px - ax) * bx + (py - ay) * by) / len2)) : 0;
                float dx = px - (ax + t * bx), dy = py - (ay + t * by);
                if (dx * dx + dy * dy < best) { best = dx * dx + dy * dy; at = along + t * len; }
                along += len;
            }
            return along - at;
        }

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
