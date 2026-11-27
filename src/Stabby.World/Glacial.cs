using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class MoraineSketch
    {
        public float X, Z, Fx, Fz, R, L, H, W, Recess;
        public uint Seed;
        public List<float> Kettles = new List<float>(), Breaches = new List<float>();
    }

    public static class Glacial
    {
        public const float SpacingM = 1800f, GroupGapM = 2600f, ColdC = 7f, ForelandC = 6f, ForelandM = 2000f, CoastPadM = 300f, LandmarkClearM = 400f, LakeClearM = 100f;
        public const int BreachPasses = 4, MaxMoraines = 10, Steps = 4, Tries = 6, MaxKettles = 4, MinKettles = 2;
        public const float MinRM = 550f, MaxRM = 950f, MinLShare = 0.8f, MaxLShare = 1.5f, MinHM = 5f, MaxHM = 15f, MinWM = 45f, MaxWM = 90f, MinWCells = 1.3f;
        public const float LateralTaper = 0.35f, RecessShare = 0.62f, RecessH = 0.5f, RecessChance = 0.6f, ReliefShare = 0.3f, MaxSlope = 0.3f, CliffSlope = 1f, CliffSpanM = 90f;
        public const float MinKettleM = 100f, MaxKettleM = 160f, MinKettleCells = 1.7f, MinDepthM = 4f, MaxDepthM = 6f, BowlShare = 1.2f, RimInner = 1f, RimOuter = 1.6f, KettleGap = 2f, CutShare = 0.6f, BenchIn = 1.2f, BenchOut = 2.4f, CrestShare = 0.6f, CrestM = 1f, RecessW = 0.8f, BreachShare = 0.5f, NotchIn = 1.2f, NotchOut = 1.8f, NotchCells = 1.6f, KettleBack = -1f, KettleFront = 0f, KettleSide = 0.3f, WaterShare = 0.9f, RimCells = 1.5f;
        static readonly float[] RingX = { 1f, 0.7071f, 0f, -0.7071f, -1f, -0.7071f, 0f, 0.7071f }, RingZ = { 0f, 0.7071f, 1f, 0.7071f, 0f, -0.7071f, -1f, -0.7071f };
        const int Picks = 3, KettleSpots = 3 * MaxKettles, Draws = Picks + 8 + 1 + 4 * KettleSpots;

        public static void Raise(WorldConfig c, WorldSketch s, bool[] sea, float[] h, ClimateField climate)
        {
            var root = Rng.Stream(c.Seed, "glacial");
            ulong baseSeed = root.NextU64();
            int g = (int)Math.Ceiling(WorldConfig.MapMeters / SpacingM);
            var order = new int[g * g];
            for (int k = 0; k < order.Length; k++) order[k] = k;
            for (int k = order.Length - 1; k > 0; k--)
            {
                int j = (int)(root.NextU64() % (ulong)(k + 1));
                int t = order[k]; order[k] = order[j]; order[j] = t;
            }
            float[] flow = null;
            var v = new float[Draws];
            foreach (int k in order)
            {
                if (s.Moraines.Count >= MaxMoraines) break;
                int gx = k % g, gz = k / g;
                var r = new Rng(Rng.Mix(baseSeed + (ulong)k));
                for (int q = 0; q < v.Length; q++) v[q] = r.NextFloat();
                int start = (int)(v[0] * Steps * Steps), tries = 0;
                for (int t = 0; t < Steps * Steps && tries < Tries; t++)
                {
                    int q = (start + t) % (Steps * Steps);
                    float x = (gx + (q % Steps + 0.25f + 0.5f * v[1]) / Steps) * SpacingM, z = (gz + (q / Steps + 0.25f + 0.5f * v[2]) / Steps) * SpacingM;
                    if (!Cold(c, sea, climate, x, z)) continue;
                    tries++;
                    if (Near(s, x, z)) continue;
                    var m = Shape(c, h, x, z, v, Picks);
                    if (m == null || !Site(c, s, sea, h, climate, m)) continue;
                    flow ??= Mesas.Flow(c, sea, h);
                    if (Mesas.Wet(c, flow, m.X, m.Z, Reach(m))) continue;
                    Kettles(c, sea, h, m, v, Picks + 8);
                    if (m.Kettles.Count == 0) continue;
                    Stamp(c, sea, h, m, 1f);
                    s.Moraines.Add(m);
                    break;
                }
            }
            if (s.Moraines.Count == 0) return;
            for (int pass = 0; pass < BreachPasses && Breach(c, sea, h, climate, s.Moraines); pass++) { }
            foreach (var m in s.Moraines) Dig(c, sea, h, m);
            for (int pass = 0; pass < BreachPasses && Breach(c, sea, h, climate, s.Moraines); pass++) { }
        }

        static bool Breach(WorldConfig c, bool[] sea, float[] h, ClimateField climate, List<MoraineSketch> moraines)
        {
            int n = c.Size;
            float cm = c.CellMeters;
            var surface = Drainage.Fill(h, sea, n, 1e-3f);
            var recv = Drainage.Receivers(surface, sea, n, cm);
            var runoff = new float[n * n];
            for (int i = 0; i < runoff.Length; i++) runoff[i] = Hydrology.Runoff(c, sea[i], climate.PrecipitationMm[i]);
            var flow = Drainage.Accumulate(Drainage.Stack(recv, n), recv, runoff);
            bool any = false;
            foreach (var m in moraines)
            {
                float reach = Reach(m), most = BreachShare * c.RiverMinDischarge;
                int x0 = Math.Max(0, (int)Math.Floor((m.X - reach) / cm)), x1 = Math.Min(n - 1, (int)Math.Ceiling((m.X + reach) / cm));
                int y0 = Math.Max(0, (int)Math.Floor((m.Z - reach) / cm)), y1 = Math.Min(n - 1, (int)Math.Ceiling((m.Z + reach) / cm));
                int at = -1;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = y * n + x;
                        if (flow[i] > most && Ridge(m, x * cm, y * cm) > CrestM) { most = flow[i]; at = i; }
                    }
                if (at < 0) continue;
                Stamp(c, sea, h, m, -1f);
                m.Breaches.Add(at % n * cm); m.Breaches.Add(at / n * cm); m.Breaches.Add(Math.Max(NotchIn * m.W, NotchCells * cm));
                Stamp(c, sea, h, m, 1f);
                any = true;
            }
            return any;
        }

        static bool Cold(WorldConfig c, bool[] sea, ClimateField climate, float x, float z)
        {
            int i = Cell(c, x, z);
            if (i < 0 || sea[i] || climate.TemperatureC[i] >= ColdC + ForelandC) return false;
            if (climate.TemperatureC[i] < ColdC) return true;
            for (int q = 0; q < 8; q++)
            {
                int j = Cell(c, x + ForelandM * RingX[q], z + ForelandM * RingZ[q]);
                if (j >= 0 && !sea[j] && climate.TemperatureC[j] < ColdC) return true;
            }
            return false;
        }

        static int Cell(WorldConfig c, float x, float z)
        {
            int ax = (int)(x / c.CellMeters), az = (int)(z / c.CellMeters);
            return ax < 0 || az < 0 || ax >= c.Size || az >= c.Size ? -1 : az * c.Size + ax;
        }

        static bool Near(WorldSketch s, float x, float z)
        {
            foreach (var m in s.Moraines)
            {
                float dx = m.X - x, dz = m.Z - z;
                if (dx * dx + dz * dz < GroupGapM * GroupGapM) return true;
            }
            return false;
        }

        static float Mix(float a, float b, float t) => a + (b - a) * t;

        static float At(WorldConfig c, float[] h, float x, float z)
        {
            int n = c.Size;
            int ix = Math.Max(0, Math.Min(n - 1, (int)Math.Round(x / c.CellMeters))), iz = Math.Max(0, Math.Min(n - 1, (int)Math.Round(z / c.CellMeters)));
            return h[iz * n + ix];
        }

        static MoraineSketch Shape(WorldConfig c, float[] h, float x, float z, float[] v, int o)
        {
            float rad = Mix(MinRM, MaxRM, v[o]);
            float sx = At(c, h, x - rad, z) - At(c, h, x + rad, z), sz = At(c, h, x, z - rad) - At(c, h, x, z + rad), gl = (float)Math.Sqrt(sx * sx + sz * sz);
            if (gl > MaxSlope * 2f * rad) return null;
            float dx = 0.35f * (2f * v[o + 4] - 1f), dz = 0.35f * (2f * v[o + 5] - 1f);
            if (gl > 1e-3f * rad) { dx += sx / gl; dz += sz / gl; }
            float l = (float)Math.Sqrt(dx * dx + dz * dz);
            if (l < 1e-3f) { dx = 1f; dz = 0f; l = 1f; }
            return new MoraineSketch
            {
                X = x, Z = z, Fx = dx / l, Fz = dz / l, R = rad, L = Mix(MinLShare, MaxLShare, v[o + 1]) * rad,
                H = Mix(MinHM, MaxHM, v[o + 2]), W = Math.Max(MinWCells * c.CellMeters, Mix(MinWM, MaxWM, v[o + 3])),
                Seed = (uint)(v[o + 6] * 16777216f) * 0x9E3779B1u + 0x7F4A7C15u, Recess = v[o + 7] < RecessChance ? RecessShare : 0f,
            };
        }

        public static float Reach(MoraineSketch m) => (float)Math.Sqrt(Math.Max(m.L, m.R) * Math.Max(m.L, m.R) + m.R * m.R) + m.W;

        static float Bump(float d, float w)
        {
            if (d >= w) return 0f;
            float u = d / w, q = 1f - u * u;
            return q * q;
        }

        static void Frame(MoraineSketch m, float x, float z, out float a, out float b)
        {
            float vx = x - m.X, vz = z - m.Z;
            a = vx * m.Fx + vz * m.Fz; b = vz * m.Fx - vx * m.Fz;
        }

        static float Arc(float a, float b, float rho)
        {
            if (a >= 0f) return Math.Abs((float)Math.Sqrt(a * a + b * b) - rho);
            float eb = Math.Abs(b) - rho;
            return (float)Math.Sqrt(a * a + eb * eb);
        }

        static float Outer(MoraineSketch m, float a, float b, out float t)
        {
            float ab = Math.Abs(b);
            if (a >= 0f) { t = 1f; return Math.Abs((float)Math.Sqrt(a * a + b * b) - m.R); }
            if (a >= -m.L) { t = 1f + LateralTaper * a / m.L; return Math.Abs(ab - m.R); }
            float ea = a + m.L, eb = ab - m.R;
            t = 1f - LateralTaper;
            return (float)Math.Sqrt(ea * ea + eb * eb);
        }

        public static float CrestGap(MoraineSketch m, float x, float z)
        {
            Frame(m, x, z, out float a, out float b);
            float d = Outer(m, a, b, out _);
            return m.Recess > 0f ? Math.Min(d, Arc(a, b, m.Recess * m.R)) : d;
        }

        public static float Ridge(MoraineSketch m, float x, float z)
        {
            Frame(m, x, z, out float a, out float b);
            float e = Bump(Outer(m, a, b, out float t), m.W) * t;
            if (m.Recess > 0f) e = Math.Max(e, RecessH * Bump(Arc(a, b, m.Recess * m.R), RecessW * m.W));
            if (e <= 0f) return 0f;
            for (int k = 0; k < m.Breaches.Count; k += 3)
            {
                float bx = x - m.Breaches[k], bz = z - m.Breaches[k + 1], r = m.Breaches[k + 2];
                e *= Grid.Smoothstep(r, r + NotchOut * m.W, (float)Math.Sqrt(bx * bx + bz * bz));
            }
            if (e <= 0f) return 0f;
            float f = 1f / 70f;
            float hum = 0.75f + 0.55f * Noise.Fbm(m.Seed, x * f, z * f, 3);
            return m.H * e * Math.Max(0.3f, Math.Min(1.15f, hum));
        }

        static bool Site(WorldConfig c, WorldSketch s, bool[] sea, float[] h, ClimateField climate, MoraineSketch m)
        {
            int n = c.Size;
            float cm = c.CellMeters, reach = Reach(m);
            int cx = (int)Math.Round(m.X / cm), cy = (int)Math.Round(m.Z / cm);
            int edge = (int)Math.Ceiling((reach + CoastPadM) / cm);
            if (cx - edge < 1 || cy - edge < 1 || cx + edge >= n - 1 || cy + edge >= n - 1) return false;
            foreach (var b in s.Mesas)
            {
                float bx = b.X - m.X, bz = b.Z - m.Z, gap = reach + Mesas.Extent(b) + LandmarkClearM;
                if (bx * bx + bz * bz < gap * gap) return false;
            }
            int step = Math.Max(1, (int)Math.Round(20f / cm)), span = Math.Max(1, (int)Math.Round(CliffSpanM / cm));
            float cliff = CliffSlope * span * cm;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int dy = -edge; dy <= edge; dy += step)
                for (int dx = -edge; dx <= edge; dx += step)
                {
                    int d2 = dx * dx + dy * dy;
                    if (d2 > edge * edge) continue;
                    int i = (cy + dy) * n + cx + dx;
                    if (sea[i]) return false;
                    if (!Inside(m, (cx + dx) * cm, (cy + dy) * cm)) continue;
                    foreach (var lake in s.Lakes)
                        if (lake.Gap(cx + dx, cy + dy, 1) < LakeClearM / cm) return false;
                    foreach (var l in s.Landmarks)
                    {
                        float lx = (l.X - cx - dx) * cm, lz = (l.Y - cy - dy) * cm;
                        if (lx * lx + lz * lz < LandmarkClearM * LandmarkClearM) return false;
                    }
                    if (s.Landmark[i]) return false;
                    if (s.Plateau[i] > 0f) return false;
                    if (climate.TemperatureC[i] >= ColdC + ForelandC) return false;
                    int ex = Math.Min(n - 1, cx + dx + span) - cx - dx, ey = Math.Min(n - 1, cy + dy + span) - cy - dy;
                    if (Math.Abs(h[i + ex] - h[i]) > cliff || Math.Abs(h[i + ey * n] - h[i]) > cliff) return false;
                    lo = Math.Min(lo, h[i]); hi = Math.Max(hi, h[i]);
                }
            if (hi - lo > ReliefShare * 2f * reach) return false;
            return true;
        }

        static bool Inside(MoraineSketch m, float x, float z)
        {
            Frame(m, x, z, out float a, out float b);
            return a >= -m.L - m.W && a <= m.R + m.W && Math.Abs(b) <= m.R + m.W;
        }

        static void Stamp(WorldConfig c, bool[] sea, float[] h, MoraineSketch m, float sign)
        {
            int n = c.Size;
            float cm = c.CellMeters, reach = Reach(m);
            int x0 = Math.Max(0, (int)Math.Floor((m.X - reach) / cm)), x1 = Math.Min(n - 1, (int)Math.Ceiling((m.X + reach) / cm));
            int y0 = Math.Max(0, (int)Math.Floor((m.Z - reach) / cm)), y1 = Math.Min(n - 1, (int)Math.Ceiling((m.Z + reach) / cm));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * n + x;
                    if (!sea[i]) h[i] += sign * Ridge(m, x * cm, y * cm);
                }
        }

        static void Kettles(WorldConfig c, bool[] sea, float[] h, MoraineSketch m, float[] v, int o)
        {
            int count = Math.Min(MaxKettles, MinKettles + (int)(v[o] * (MaxKettles - MinKettles + 1)));
            float cm = c.CellMeters;
            for (int q = 0; q < KettleSpots && m.Kettles.Count < 4 * count; q++)
            {
                int p = o + 1 + 4 * q;
                float rk = Math.Max(MinKettleCells * cm, Mix(MinKettleM, MaxKettleM, v[p + 2])), depth = Mix(MinDepthM, MaxDepthM, v[p + 3]);
                float a = Mix(KettleBack, KettleFront, v[p]) * m.R, b = KettleSide * (2f * v[p + 1] - 1f) * m.R;
                float kx = m.X + a * m.Fx - b * m.Fz, kz = m.Z + a * m.Fz + b * m.Fx;
                if (!Clear(m, kx, kz, rk) || !Rim(c, sea, h, kx, kz, rk, out float rim, out float top)) continue;
                if (top - rim > CutShare * rk) continue;
                m.Kettles.Add(kx); m.Kettles.Add(kz); m.Kettles.Add(rk); m.Kettles.Add(depth);
            }
        }

        static bool Rim(WorldConfig c, bool[] sea, float[] h, float kx, float kz, float rk, out float rim, out float top)
        {
            int n = c.Size;
            float cm = c.CellMeters;
            rim = float.MaxValue; top = float.MinValue;
            float ro = Math.Max(RimOuter * rk, RimInner * rk + RimCells * cm);
            int cx = (int)Math.Round(kx / cm), cy = (int)Math.Round(kz / cm), outer = (int)Math.Ceiling(ro / cm);
            if (cx - outer < 1 || cy - outer < 1 || cx + outer >= n - 1 || cy + outer >= n - 1) return false;
            for (int dy = -outer; dy <= outer; dy++)
                for (int dx = -outer; dx <= outer; dx++)
                {
                    float d = (float)Math.Sqrt(dx * dx + dy * dy) * cm;
                    if (d > ro) continue;
                    int i = (cy + dy) * n + cx + dx;
                    if (sea[i]) return false;
                    if (d >= RimInner * rk) rim = Math.Min(rim, h[i]);
                    else top = Math.Max(top, h[i]);
                }
            return true;
        }

        static void Dig(WorldConfig c, bool[] sea, float[] h, MoraineSketch m)
        {
            int n = c.Size;
            float cm = c.CellMeters;
            for (int k = 0; k < m.Kettles.Count; k += 4)
            {
                float kx = m.Kettles[k], kz = m.Kettles[k + 1], rk = m.Kettles[k + 2], depth = m.Kettles[k + 3];
                Rim(c, sea, h, kx, kz, rk, out float rim, out _);
                int cx = (int)Math.Round(kx / cm), cy = (int)Math.Round(kz / cm), inner = (int)Math.Ceiling(RimInner * rk / cm), bench = (int)Math.Ceiling(BenchOut * rk / cm);
                for (int dy = -bench; dy <= bench; dy++)
                    for (int dx = -bench; dx <= bench; dx++)
                    {
                        int px = cx + dx, py = cy + dy;
                        if (px < 0 || py < 0 || px >= n || py >= n || sea[py * n + px]) continue;
                        float d = (float)Math.Sqrt(dx * dx + dy * dy) * cm, t = 1f - Grid.Smoothstep(BenchIn * rk, BenchOut * rk, d);
                        if (t > 0f && h[py * n + px] > rim) h[py * n + px] = Mix(h[py * n + px], rim, t);
                    }
                float floor = rim - depth;
                for (int dy = -inner; dy <= inner; dy++)
                    for (int dx = -inner; dx <= inner; dx++)
                    {
                        float d = (float)Math.Sqrt(dx * dx + dy * dy) * cm;
                        if (d >= RimInner * rk) continue;
                        int i = (cy + dy) * n + cx + dx;
                        float u = d / rk;
                        h[i] = Math.Min(h[i], floor + BowlShare * depth * u * u);
                    }
            }
        }

        static bool Clear(MoraineSketch m, float kx, float kz, float rk)
        {
            for (int k = 0; k < m.Kettles.Count; k += 4)
            {
                float dx = m.Kettles[k] - kx, dz = m.Kettles[k + 1] - kz, s = KettleGap * (rk + m.Kettles[k + 2]);
                if (dx * dx + dz * dz < s * s) return false;
            }
            return CrestGap(m, kx, kz) >= rk + CrestShare * m.W;
        }
    }
}
