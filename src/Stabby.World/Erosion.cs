using System;

namespace Stabby.World
{
    // Uplift against stream-power incision (implicit solver, Braun & Willett 2013)
    // plus hillslope diffusion. Valleys are cut by the same drainage graph that
    // later becomes the river network, so rivers never need carving afterwards.
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
                        float top = c.PlateauHeight * (1 + 0.15f * Noise.Fbm(seed ^ 0xC11Fu, x * 12f / n, y * 12f / n, 3)
                            + 0.12f * Noise.Fbm(seed ^ 0x7AB1u, x * 48f / n, y * 48f / n, 4));
                        h[i] += (top - h[i]) * s.Cliff[i];
                    }
                    float beach = s.Beach[i] * (1 - Grid.Smoothstep(0.5f, 1f, m / c.BeachMeters));
                    if (beach > 0) h[i] += (Math.Min(h[i], BeachCap(c, m)) - h[i]) * beach;
                    // Noise alone outweighs a lowland corridor, so its floor is graded to its mouth;
                    // grading to the nearest coast sends the river out a side bay on lobed islands.
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

        // A shallow shelf, then a slope to the deep; beaches widen the shelf and
        // cliffs drop almost straight to a deeper foot.
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

        // Each range runs on past its ends as a submerged spur, bending with noise
        // and sinking until it fades into the seabed.
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

        // Distance-to-coast creases show as straight seams; a light blur hides them.
        // Shallows are left alone so beaches keep their gentle shelf.
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

        public static void Run(WorldConfig c, WorldSketch s, float[] h, float[] precipitationMm)
        {
            int n = c.Size;
            bool[] sea = SeaOf(s.Land);
            float cellArea = c.CellMeters * c.CellMeters;
            var runoff = new float[n * n];
            for (int i = 0; i < n * n; i++) runoff[i] = sea[i] ? 0 : cellArea * precipitationMm[i] / 1000f;

            // Beaches stay graded to the sea however much uplift and diffusion pile on.
            var beachCap = new float[n * n];
            var slope = new float[n * n];
            var erod = new float[n * n];
            for (int i = 0; i < n * n; i++)
            {
                float m = s.Coast[i] * c.CellMeters;
                float w = s.Beach[i] * (1 - Grid.Smoothstep(0.5f, 1f, m / c.BeachMeters));
                beachCap[i] = BeachCap(c, m) + (1 - w) * 1e6f;
                float core = Grid.Smoothstep(0.6f, 1f, s.Cliff[i]);
                slope[i] = c.MaxSlope * (1 + c.CliffSlope * core);
                erod[i] = c.Erodibility * (1 - c.CaprockHardness * core);
            }

            float dt = c.TimeStepYears;
            for (int step = 0; step < c.ErosionSteps; step++)
            {
                var filled = Drainage.Fill(h, sea, n, 1e-3f);
                var recv = Drainage.Receivers(filled, sea, n, c.CellMeters);
                var stack = Drainage.Stack(recv, n);
                var q = Drainage.Accumulate(stack, recv, runoff);

                for (int i = 0; i < n * n; i++) if (!sea[i]) h[i] += s.Uplift[i] * c.MaxUplift * dt;

                foreach (int i in stack)
                {
                    int r = recv[i];
                    if (r == i || sea[i]) continue;
                    if (filled[i] - h[i] > 0.01f) continue;
                    float hr = sea[r] ? 0 : h[r];
                    if (h[i] <= hr) continue;
                    float len = Drainage.StepLength(n, i, r, c.CellMeters);
                    float f = erod[i] * dt * (float)FMath.Pow(q[i], c.AreaExponent) / len;
                    h[i] = Math.Max(hr, (h[i] + f * hr) / (1 + f));
                    h[i] = Math.Min(h[i], hr + slope[i] * len);
                }

                Diffuse(c, s, h, sea);
                for (int i = 0; i < n * n; i++)
                    if (!sea[i]) h[i] = Math.Max(MinLandHeight, Math.Min(h[i], beachCap[i]));
            }
        }

        const float ShoreCells = 2f, LakeBasinDepth = 25f, FlatGrade = 0.05f;

        // Erosion drains any basin given time, so each sketched lake is cut below its
        // lowest shore after the run; the band is two cells wide so diagonal flow cannot
        // slip through. Beds may sink below sea level: raising a low shore instead
        // leaves flats the outflow would have to climb.
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
            // Erosion leaves near-flats that the lake's outflow can cross; graded to the
            // drainage fill, the outflow never rises above the lake's level.
            var sea = SeaOf(s.Land);
            var graded = Drainage.Fill(h, sea, n, 1e-3f);
            for (int i = 0; i < n * n; i++)
                if (!sea[i] && graded[i] > h[i] && graded[i] - h[i] < FlatGrade) h[i] = graded[i];
        }

        // Cliff cells see the sea as a wall at their own height, so diffusion
        // does not round them off.
        static void Diffuse(WorldConfig c, WorldSketch s, float[] h, bool[] sea)
        {
            int n = c.Size;
            float k = c.Diffusion * c.TimeStepYears / (c.CellMeters * c.CellMeters);
            if (k > 0.24f) k = 0.24f;
            var src = (float[])h.Clone();
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
                    h[i] = src[i] + k * lap;
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
