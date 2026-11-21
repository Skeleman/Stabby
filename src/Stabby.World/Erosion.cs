using System;

namespace Stabby.World
{
    public static class Erosion
    {
        public const float MinLandHeight = 0.5f;
        public const float CorridorGradient = 0.003f;

        public static float[] InitialHeight(WorldConfig c, WorldSketch s)
        {
            int n = c.Size;
            var h = new float[n * n];
            uint seed = Rng.Stream(c.Seed, "initial-height").NextU32();
            var sea = SeaOf(s.Land);
            var offshore = WorldSketch.CoastDistance(n, sea);
            var inland = s.Coast;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    if (!s.Land[i])
                    {
                        h[i] = Seabed(c, s, seed, x, y, offshore[i] * c.CellMeters, s.Cliff[i], s.Beach[i]);
                        continue;
                    }
                    h[i] = 2f + s.Uplift[i] * c.InitialRelief + 25f * (1 + Noise.Fbm(seed, x * 16f / n, y * 16f / n, 3));
                    float m = inland[i] * c.CellMeters;
                    if (s.Cliff[i] > 0)
                    {
                        float rise = Math.Max(0, c.CliffRise * Math.Min(1, m / c.CliffTiltMeters)
                            + 0.25f * c.CliffRise * Noise.Fbm(seed ^ 0xC11Fu, x * 12f / n, y * 12f / n, 3));
                        float step = rise / c.CliffStepMeters, tread = (float)Math.Floor(step);
                        float top = c.PlateauHeight + c.CliffStepMeters * (tread + Grid.Smoothstep(0.7f, 1f, step - tread))
                            + 3f * Noise.Fbm(seed ^ 0x7AB1u, x * 48f / n, y * 48f / n, 4);
                        h[i] += (top - h[i]) * s.Cliff[i];
                    }
                    float beach = s.Beach[i] * (1 - Grid.Smoothstep(0.5f, 1f, m / c.BeachMeters));
                    if (beach > 0) h[i] += (Math.Min(h[i], BeachCap(c, m)) - h[i]) * beach;
                    if (s.Land[i] && s.Valley[i] > 0)
                    {
                        float floor = 1f + s.ValleyRun[i] * c.CellMeters * CorridorGradient;
                        h[i] += (Math.Min(h[i], floor) - h[i]) * s.Valley[i];
                    }
                }
            AddSeaRidges(c, s, h, seed ^ 0x51DEu);
            SmoothSeabed(n, h, sea);
            return h;
        }

        static float BeachCap(WorldConfig c, float metersInland) => MinLandHeight + c.BeachGrade * metersInland;

        static float Seabed(WorldConfig c, WorldSketch s, uint seed, int x, int y, float d, float cliff, float beach)
        {
            int n = c.Size;
            float shelf = c.ShelfMeters * (1 - 0.85f * cliff) * (1 + beach);
            float depth = c.ShelfDepth * Grid.Smoothstep(0, shelf, d)
                + (c.DeepDepth - c.ShelfDepth) * Grid.Smoothstep(shelf, shelf + c.SlopeMeters, d)
                + c.CliffFootDepth * cliff;
            depth *= 1 + 0.25f * Noise.Fbm(seed ^ 0x5EAu, x * 10f / n, y * 10f / n, 4);
            return Math.Min(-1f, -depth);
        }

        static void AddSeaRidges(WorldConfig c, WorldSketch s, float[] h, uint seed)
        {
            int n = c.Size;
            foreach (var r in s.Ranges)
            {
                int last = r.X.Length - 1;
                if (last < 1) continue;
                float total = 0;
                for (int p = 1; p <= last; p++) total += Dist(r.X[p] - r.X[p - 1], r.Y[p] - r.Y[p - 1]);
                float reach = c.RidgeReach * total, width = 3 * r.Width;
                for (int end = 0; end < 2; end++)
                {
                    int a = end == 0 ? 0 : last, b = end == 0 ? 1 : last - 1;
                    float ex = r.X[a], ey = r.Y[a], dx = ex - r.X[b], dy = ey - r.Y[b], dl = Dist(dx, dy);
                    if (dl < 1e-3f) continue;
                    dx /= dl; dy /= dl;
                    uint sd = seed + (uint)end * 7919u;
                    float span = reach + width;
                    int x0 = Math.Max(0, (int)(ex - span)), x1 = Math.Min(n - 1, (int)(ex + span));
                    int y0 = Math.Max(0, (int)(ey - span)), y1 = Math.Min(n - 1, (int)(ey + span));
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int i = y * n + x;
                            if (s.Land[i]) continue;
                            float px = x - ex, py = y - ey;
                            float t = px * dx + py * dy;
                            if (t <= -width || t >= reach) continue;
                            float side = px * -dy + py * dx;
                            side += r.Width * 1.5f * Noise.Fbm(sd, t * 3f / n + ex * 0.01f, ey * 0.01f, 2);
                            float across = 1 - Grid.Smoothstep(0, width, Math.Abs(side));
                            float run = Math.Max(0, t) / reach;
                            float w = across * Grid.Smoothstep(-width, width, t) * (1 - Grid.Smoothstep(0.5f, 1f, run));
                            if (w <= 0) continue;
                            float crest = -c.RidgeCrestDepth - c.ShelfDepth * run;
                            if (crest > h[i]) h[i] = Math.Min(-1f, h[i] + (crest - h[i]) * w);
                        }
                }
            }
        }

        static void SmoothSeabed(int n, float[] h, bool[] sea)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                var src = (float[])h.Clone();
                for (int y = 1; y < n - 1; y++)
                    for (int x = 1; x < n - 1; x++)
                    {
                        int i = y * n + x;
                        if (!sea[i] || src[i] > -10f) continue;
                        float sum = 0; int count = 0;
                        for (int k = 0; k < 8; k++)
                        {
                            int j = i + Grid.DY[k] * n + Grid.DX[k];
                            if (sea[j]) { sum += src[j]; count++; }
                        }
                        h[i] = Math.Min(-1f, (src[i] + sum) / (1 + count));
                    }
            }
        }

        static float Dist(float x, float y) => (float)Math.Sqrt(x * x + y * y);

        public static void Run(WorldConfig c, WorldSketch s, MaterialLibrary lib, float[] h, float[] precipitationMm)
        {
            int n = c.Size, m = c.ErosionSize;
            int fine = Math.Min(c.FineSteps, c.ErosionSteps);
            if (m <= 0 || n <= m || n % m != 0)
            {
                Steps(c, s, lib, h, precipitationMm, c.ErosionSteps);
                return;
            }
            int f = n / m;
            var cc = c.Clone();
            cc.Size = m;
            cc.CellMeters = c.CellMeters * f;
            var cs = Coarse(s, f, m);
            var hc = new float[m * m];
            var pc = new float[m * m];
            for (int cy = 0; cy < m; cy++)
                for (int cx = 0; cx < m; cx++)
                {
                    int ci = cy * m + cx;
                    bool land = cs.Land[ci];
                    double sum = 0, rain = 0; int count = 0;
                    for (int dy = 0; dy < f; dy++)
                        for (int dx = 0; dx < f; dx++)
                        {
                            int i = (cy * f + dy) * n + cx * f + dx;
                            rain += precipitationMm[i];
                            if (s.Land[i] != land) continue;
                            sum += h[i]; count++;
                        }
                    hc[ci] = (float)(sum / count);
                    pc[ci] = (float)(rain / (f * f));
                }
            var start = (float[])hc.Clone();
            Steps(cc, cs, lib, hc, pc, c.ErosionSteps - fine);
            for (int i = 0; i < m * m; i++) hc[i] = cs.Land[i] ? hc[i] - start[i] : 0;
            float half = (f - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            {
                float v = Math.Max(0, Math.Min(m - 1, (y - half) / f));
                int y0 = Math.Min(m - 2, (int)v);
                float ty = v - y0;
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    if (!s.Land[i]) continue;
                    float u = Math.Max(0, Math.Min(m - 1, (x - half) / f));
                    int x0 = Math.Min(m - 2, (int)u);
                    float tx = u - x0;
                    int a = y0 * m + x0;
                    float top = hc[a] + (hc[a + 1] - hc[a]) * tx, bottom = hc[a + m] + (hc[a + m + 1] - hc[a + m]) * tx;
                    h[i] += top + (bottom - top) * ty;
                }
            }
            if (fine > 0) Steps(c, s, lib, h, precipitationMm, fine);
            else
                for (int i = 0; i < n * n; i++)
                    if (s.Land[i]) h[i] = Math.Max(MinLandHeight, h[i]);
        }

        static WorldSketch Coarse(WorldSketch s, int f, int m)
        {
            int n = s.Size;
            var cs = new WorldSketch
            {
                Size = m, Land = new bool[m * m], Uplift = new float[m * m], Coast = new float[m * m],
                Beach = new float[m * m], Cliff = new float[m * m],
            };
            float half = (f - 1) * 0.5f, area = f * f;
            foreach (var hl in s.Highlands)
                cs.Highlands.Add(new HighlandSketch { Plateau = hl.Plateau, X = (hl.X - half) / f, Y = (hl.Y - half) / f, Radius = hl.Radius / f });
            for (int cy = 0; cy < m; cy++)
                for (int cx = 0; cx < m; cx++)
                {
                    int land = 0;
                    float up = 0, coast = 0, beach = 0, cliff = 0;
                    for (int dy = 0; dy < f; dy++)
                        for (int dx = 0; dx < f; dx++)
                        {
                            int i = (cy * f + dy) * n + cx * f + dx;
                            if (s.Land[i]) land++;
                            up += s.Uplift[i]; coast += s.Coast[i]; beach += s.Beach[i]; cliff += s.Cliff[i];
                        }
                    int ci = cy * m + cx;
                    cs.Land[ci] = 2 * land > f * f;
                    cs.Uplift[ci] = up / area; cs.Coast[ci] = coast / area / f;
                    cs.Beach[ci] = beach / area; cs.Cliff[ci] = cliff / area;
                }
            return cs;
        }

        static void Steps(WorldConfig c, WorldSketch s, MaterialLibrary lib, float[] h, float[] precipitationMm, int steps)
        {
            int n = c.Size;
            bool[] sea = SeaOf(s.Land);
            float cellArea = c.CellMeters * c.CellMeters;
            double sum = 0; int land = 0;
            for (int i = 0; i < n * n; i++) if (!sea[i]) { sum += precipitationMm[i]; land++; }
            float scale = land > 0 && sum > 0 ? (float)(c.ErosionRainMm * land / sum) : 0;
            var runoff = new float[n * n];
            for (int i = 0; i < n * n; i++) runoff[i] = sea[i] ? 0 : cellArea * precipitationMm[i] * scale / 1000f;

            var beachCap = new float[n * n];
            var slope = new float[n * n];
            var erod = new float[n * n];
            var caprock = new float[n * n];
            for (int i = 0; i < n * n; i++)
            {
                float m = s.Coast[i] * c.CellMeters;
                float w = s.Beach[i] * (1 - Grid.Smoothstep(0.5f, 1f, m / c.BeachMeters));
                beachCap[i] = BeachCap(c, m) + (1 - w) * 1e6f;
                caprock[i] = Grid.Smoothstep(0.6f, 1f, s.Cliff[i]);
                slope[i] = c.MaxSlope * (1 + c.CliffSlope * caprock[i]);
                erod[i] = c.Erodibility * (1 - c.CaprockHardness * caprock[i]) * (1 - c.SummitHardness * SummitCore(s, i % n, i / n));
            }

            var strata = new StrataField(c, lib);
            var hard = new float[n * n];
            float dt = c.TimeStepYears;
            var scratch = new DrainageScratch(n);
            var src = new float[n * n];
            for (int step = 0; step < steps; step++)
            {
                var filled = Drainage.Fill(h, sea, n, 1e-3f, scratch);
                var recv = Drainage.Receivers(filled, sea, n, c.CellMeters, scratch.Recv);
                var stack = Drainage.Stack(recv, n, scratch);
                var q = Drainage.Accumulate(stack, recv, runoff, scratch.Acc);

                for (int i = 0; i < n * n; i++) if (!sea[i]) h[i] += s.Uplift[i] * c.MaxUplift * dt;
                for (int i = 0; i < n * n; i++) hard[i] = sea[i] ? 0 : strata.Hardness(i, h[i]);

                foreach (int i in stack)
                {
                    int r = recv[i];
                    if (r == i || sea[i]) continue;
                    if (filled[i] - h[i] > 0.01f) continue;
                    float hr = sea[r] ? s.Cliff[i] * h[i] : h[r];
                    if (h[i] <= hr) continue;
                    float len = Drainage.StepLength(n, i, r, c.CellMeters);
                    float f = erod[i] * strata.Softness(hard[i]) * dt * (float)FMath.Pow(q[i], c.AreaExponent) / len;
                    h[i] = Math.Max(hr, (h[i] + f * hr) / (1 + f));
                    h[i] = Math.Min(h[i], hr + slope[i] * (1 + c.StrataSlope * hard[i]) * len);
                }

                Diffuse(c, s, caprock, hard, h, sea, src);
                for (int i = 0; i < n * n; i++)
                    if (!sea[i]) h[i] = Math.Max(MinLandHeight, Math.Min(h[i], beachCap[i]));
            }
        }

        const float ShoreCells = 2f, LakeBasinDepth = 25f, FlatGrade = 0.05f;

        public static void FlattenTableland(WorldConfig c, WorldSketch s, float[] h)
        {
            int n = c.Size;
            uint seed = Rng.Stream(c.Seed, "tableland").NextU32();
            foreach (var hl in s.Highlands)
            {
                if (!hl.Plateau) continue;
                int r = (int)Math.Ceiling(hl.Radius * 1.5f);
                int x0 = Math.Max(0, (int)hl.X - r), x1 = Math.Min(n - 1, (int)hl.X + r);
                int y0 = Math.Max(0, (int)hl.Y - r), y1 = Math.Min(n - 1, (int)hl.Y + r);
                double sum = 0; int count = 0;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        if (s.Plateau[y * n + x] > 0.9f && Cone(s, x, y) == 0) { sum += h[y * n + x]; count++; }
                if (count == 0) continue;
                float summit = 0;
                for (int i = 0; i < n * n; i++)
                    if (Cone(s, i % n, i / n) > 0.99f) summit = Math.Max(summit, h[i]);
                float level = Math.Max((float)(sum / count), c.TablelandShare * summit);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = y * n + x;
                        if (s.Plateau[i] <= 0 || !s.Land[i]) continue;
                        float d = (float)Math.Sqrt((x - hl.X) * (x - hl.X) + (y - hl.Y) * (y - hl.Y)) / hl.Radius;
                        float top = level * (1 - c.TablelandFall * d)
                            + c.TablelandRelief * (Noise.Fbm(seed, x * 14f / n, y * 14f / n, 3) + 0.3f * Noise.Fbm(seed ^ 0x9E37u, x * 48f / n, y * 48f / n, 4));
                        h[i] += (top - h[i]) * s.Plateau[i] * (top > h[i] ? 1 : 1 - Cone(s, x, y));
                    }
            }
        }

        static float Cone(WorldSketch s, int x, int y)
        {
            float w = 0;
            foreach (var hl in s.Highlands)
                if (!hl.Plateau)
                {
                    float d = (float)Math.Sqrt((x - hl.X) * (x - hl.X) + (y - hl.Y) * (y - hl.Y)) / hl.Radius;
                    w = Math.Max(w, 1 - Grid.Smoothstep(0.5f, 1f, d));
                }
            return w;
        }

        public static void CarveLakes(WorldConfig c, WorldSketch s, float[] h)
        {
            int n = c.Size;
            foreach (var lake in s.Lakes)
            {
                int x0 = Math.Max(1, (int)(lake.X - 3 * lake.Radius)), x1 = Math.Min(n - 2, (int)(lake.X + 3 * lake.Radius));
                int y0 = Math.Max(1, (int)(lake.Y - 3 * lake.Radius)), y1 = Math.Min(n - 2, (int)(lake.Y + 3 * lake.Radius));
                float level = float.MaxValue;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float g = lake.Gap(x, y, 1);
                        if (g >= 0 && g < ShoreCells && s.Land[y * n + x]) level = Math.Min(level, h[y * n + x]);
                    }
                if (level == float.MaxValue) continue;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float g = lake.Gap(x, y, 1);
                        int i = y * n + x;
                        if (g >= 0 || !s.Land[i]) continue;
                        h[i] = Math.Min(h[i], level - 1f - LakeBasinDepth * Math.Min(1f, -3f * g / lake.Radius));
                    }
            }
            if (s.Lakes.Count == 0) return;
            var sea = SeaOf(s.Land);
            var graded = Drainage.Fill(h, sea, n, 1e-3f);
            for (int i = 0; i < n * n; i++)
                if (!sea[i] && graded[i] > h[i] && graded[i] - h[i] < FlatGrade) h[i] = graded[i];
        }

        static float SummitCore(WorldSketch s, int x, int y)
        {
            float w = 0;
            foreach (var hl in s.Highlands)
                if (!hl.Plateau)
                {
                    float d = (float)Math.Sqrt((x - hl.X) * (x - hl.X) + (y - hl.Y) * (y - hl.Y)) / hl.Radius;
                    w = Math.Max(w, 1 - Grid.Smoothstep(0.3f, 1f, d));
                }
            return w;
        }

        static void Diffuse(WorldConfig c, WorldSketch s, float[] caprock, float[] hard, float[] h, bool[] sea, float[] src)
        {
            int n = c.Size;
            float k = c.Diffusion * c.TimeStepYears / (c.CellMeters * c.CellMeters);
            if (k > 0.24f) k = 0.24f;
            Array.Copy(h, src, h.Length);
            for (int y = 1; y < n - 1; y++)
                for (int x = 1; x < n - 1; x++)
                {
                    int i = y * n + x;
                    if (sea[i]) continue;
                    float wall = s.Cliff[i] * src[i], lap = 0;
                    lap += (sea[i - 1] ? wall : src[i - 1]) - src[i];
                    lap += (sea[i + 1] ? wall : src[i + 1]) - src[i];
                    lap += (sea[i - n] ? wall : src[i - n]) - src[i];
                    lap += (sea[i + n] ? wall : src[i + n]) - src[i];
                    h[i] = src[i] + k * (1 - caprock[i]) * (1 - hard[i]) * lap;
                }
        }

        public static bool[] SeaOf(bool[] land)
        {
            var sea = new bool[land.Length];
            for (int i = 0; i < land.Length; i++) sea[i] = !land[i];
            return sea;
        }
    }
}
