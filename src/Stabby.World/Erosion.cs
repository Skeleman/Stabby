using System;

namespace Stabby.World
{
    // Uplift against stream-power incision (implicit solver, Braun & Willett 2013)
    // plus hillslope diffusion. Valleys are cut by the same drainage graph that
    // later becomes the river network, so rivers never need carving afterwards.
    public static class Erosion
    {
        public const float MinLandHeight = 0.5f;

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
                    float f = c.Erodibility * dt * (float)Math.Pow(q[i], c.AreaExponent) / len;
                    h[i] = Math.Max(hr, (h[i] + f * hr) / (1 + f));
                    h[i] = Math.Min(h[i], hr + c.MaxSlope * len);
                }

                Diffuse(c, h, sea);
                for (int i = 0; i < n * n; i++) if (!sea[i] && h[i] < MinLandHeight) h[i] = MinLandHeight;
            }
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
