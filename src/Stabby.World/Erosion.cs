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
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    h[i] = s.Land[i]
                        ? 2f + s.Uplift[i] * c.InitialRelief + 25f * (1 + Noise.Fbm(seed, x * 16f / n, y * 16f / n, 3))
                        : c.SeaFloor;
                    // Noise alone outweighs a lowland corridor, so its floor is graded to its mouth;
                    // grading to the nearest coast sends the river out a side bay on lobed islands.
                    if (s.Land[i] && s.Valley[i] > 0)
                    {
                        float floor = 1f + s.ValleyRun[i] * c.CellMeters * CorridorGradient;
                        h[i] += (Math.Min(h[i], floor) - h[i]) * s.Valley[i];
                    }
                }
            return h;
        }

        public static void Run(WorldConfig c, WorldSketch s, float[] h, float[] precipitationMm)
        {
            int n = c.Size;
            bool[] sea = SeaOf(s.Land);
            float cellArea = c.CellMeters * c.CellMeters;
            var runoff = new float[n * n];
            for (int i = 0; i < n * n; i++) runoff[i] = sea[i] ? 0 : cellArea * precipitationMm[i] / 1000f;

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
                    float f = c.Erodibility * dt * (float)FMath.Pow(q[i], c.AreaExponent) / len;
                    h[i] = Math.Max(hr, (h[i] + f * hr) / (1 + f));
                    h[i] = Math.Min(h[i], hr + c.MaxSlope * len);
                }

                Diffuse(c, h, sea);
                for (int i = 0; i < n * n; i++) if (!sea[i] && h[i] < MinLandHeight) h[i] = MinLandHeight;
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

        static void Diffuse(WorldConfig c, float[] h, bool[] sea)
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
                    float lap = 0;
                    lap += (sea[i - 1] ? 0 : src[i - 1]) - src[i];
                    lap += (sea[i + 1] ? 0 : src[i + 1]) - src[i];
                    lap += (sea[i - n] ? 0 : src[i - n]) - src[i];
                    lap += (sea[i + n] ? 0 : src[i + n]) - src[i];
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
