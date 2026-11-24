using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class MesaSketch
    {
        public float X, Z, Half, Top, Cap, Base, Hi, Spread, Tx, Tz, Flat;
        public uint Seed;
        public float[] L;
        public bool Butte;
    }

    public static class Mesas
    {
        public static readonly string[] Biomes = { "desert", "cold-desert", "steppe", "shrubland" };
        public const float SpacingM = 640f, GroupGapM = 1500f, AridRainMm = 700f, AridMinTempC = 0f, TrySpread = 0.5f, SmallScale = 0.6f;
        public const int MaxGroups = 6, AnchorSteps = 4, Tries = 6, Lobes = 3, MaxButtes = 3;
        public const float MinHalfM = 95f, MaxHalfM = 190f, MinAcrossM = 150f, MaxAcrossM = 600f, MinRiseM = 75f, MaxRiseM = 130f, ReliefShare = 1.2f, HeadroomShare = 0.5f;
        public const float CapShare = 0.45f, CliffGrade = 4.5f, TalusSpread = 2.3f, OutlineNoise = 0.18f, NotchM = 7f, TopNoiseM = 1.2f, MaxTilt = 0.008f;
        public const float ButteMinHalfM = 26f, ButteMaxHalfM = 42f, ButteCapShare = 0.6f, ButteTalusSpread = 1.6f, ButteMinGapM = 50f, ButteMaxGapM = 200f, ButteSlack = 5f;
        public const float CoastPadM = 100f, FlowAreaM2 = 1.5e6f, ValleyMax = 0.2f, GullyShare = 0.12f, SampleM = 20f, PedimentShare = 0.6f, PedimentFlat = 0.8f;
        public const float LipChance = 0.8f, LipMinR = 16f, LipMaxR = 22f, LipMinLength = 30f, LipMaxLength = 60f, LipMinT = 4f, LipMaxT = 8f, LipMinDrop = 25f, LipFlatM = 4f, LipRimM = 2f, LipStepM = 2f;

        public static void Lip(GeneratedWorld w, TerrainDetail detail, bool[] biomes, int tx, int ty, ref Rng r, List<FeatureEdit> output)
        {
            float roll = r.NextFloat(), radius = r.Range(LipMinR, LipMaxR), length = r.Range(LipMinLength, LipMaxLength), thick = r.Range(LipMinT, LipMaxT);
            if (roll >= LipChance) return;
            int n = w.Config.Size, best = -1, bestDir = 0, cells = Features.TileCells, margin = Features.MarginCells;
            float cm = w.Config.CellMeters, bestDrop = Math.Min(LipMinDrop, LipMinDrop / Features.ReferenceCellM * cm);
            for (int y = ty * cells + margin; y < (ty + 1) * cells - margin; y++)
                for (int x = tx * cells + margin; x < (tx + 1) * cells - margin; x++)
                {
                    int i = y * n + x, b = w.Biome[i];
                    if (b < 0 || !biomes[b] || !Features.Dry(w, i)) continue;
                    for (int k = 0; k < 8; k += 2)
                    {
                        int j = i + Grid.DY[k] * n + Grid.DX[k], back = i - Grid.DY[k] * n - Grid.DX[k];
                        float drop = w.Height[i] - w.Height[j];
                        if (drop > bestDrop && Math.Abs(w.Height[back] - w.Height[i]) <= LipFlatM && Features.Dry(w, j)) { bestDrop = drop; best = i; bestDir = k; }
                    }
                }
            if (best < 0) return;
            float gx = Grid.DX[bestDir], gz = Grid.DY[bestDir], bx = best % n, bz = best / n, top = detail.HeightAt(bx, bz);
            float y0 = top - thick - radius, foot = -1f;
            for (float d = 0f; d <= cm; d += LipStepM)
                if (detail.HeightAt(bx + gx * d / cm, bz + gz * d / cm) <= y0) { foot = d; break; }
            if (foot < 0f) return;
            float axis = foot - 0.5f * radius, cx = bx + gx * axis / cm, cz = bz + gz * axis / cm;
            if (detail.HeightAt(cx - gx * radius / cm, cz - gz * radius / cm) < y0 + radius + thick - LipRimM) return;
            if (detail.HeightAt(bx + gx * (foot + radius) / cm, bz + gz * (foot + radius) / cm) > y0 - 0.3f * radius) return;
            float half = length * 0.5f;
            output.Add(new FeatureEdit
            {
                Kind = FeatureEdit.Carve, Shape = FeatureEdit.Lip, R = radius,
                Ax = cx * cm - gz * half, Az = cz * cm + gx * half, Ay = y0,
                Bx = cx * cm + gz * half, Bz = cz * cm - gx * half, By = y0,
            });
        }

        public static void Raise(WorldConfig c, WorldSketch s, bool[] sea, float[] h, ClimateField climate)
        {
            var root = Rng.Stream(c.Seed, "mesas");
            ulong baseSeed = root.NextU64();
            int g = (int)Math.Ceiling(WorldConfig.MapMeters / SpacingM);
            var order = new int[g * g];
            for (int k = 0; k < order.Length; k++) order[k] = k;
            for (int k = order.Length - 1; k > 0; k--)
            {
                int j = (int)(root.NextU64() % (ulong)(k + 1));
                int t = order[k]; order[k] = order[j]; order[j] = t;
            }
            var placed = new List<float>();
            float[] flow = null;
            var v = new float[2 * Tries + 5 + 4 * (Lobes - 1) + 1 + 8 * MaxButtes];
            foreach (int k in order)
            {
                if (placed.Count >= 2 * MaxGroups) break;
                int gx = k % g, gz = k / g;
                if (!Anchor(c, s, sea, climate, gx, gz, out float fx, out float fz)) continue;
                var r = new Rng(Rng.Mix(baseSeed + (ulong)k));
                for (int q = 0; q < v.Length; q++) v[q] = r.NextFloat();
                for (int t = 0; t < 2 * Tries; t++)
                {
                    float x = (fx + TrySpread * (v[2 * (t % Tries)] - 0.5f)) * SpacingM, z = (fz + TrySpread * (v[2 * (t % Tries) + 1] - 0.5f)) * SpacingM;
                    if (Near(placed, x, z)) continue;
                    var b = Main(x, z, v, 2 * Tries, t < Tries ? 1f : SmallScale);
                    float rise = Mix(MinRiseM, MaxRiseM, v[2 * Tries + 1]);
                    if (!Site(c, s, sea, h, climate, b, Reach(b, rise), rise)) continue;
                    flow ??= Flow(c, sea, h);
                    if (Wet(c, flow, b.X, b.Z, Core(b, rise))) continue;
                    b.Top = Math.Max(b.Base + rise, b.Hi + HeadroomShare * rise);
                    b.Cap = CapShare * (b.Top - b.Base);
                    Stamp(c, sea, h, b);
                    s.Mesas.Add(b);
                    Buttes(c, s, sea, h, climate, flow, b, v, 2 * Tries + 5 + 4 * (Lobes - 1));
                    placed.Add(x); placed.Add(z);
                    break;
                }
            }
        }

        static bool Anchor(WorldConfig c, WorldSketch s, bool[] sea, ClimateField climate, int gx, int gz, out float fx, out float fz)
        {
            for (int q = 0; q < AnchorSteps * AnchorSteps; q++)
            {
                fx = gx + (q % AnchorSteps + 0.5f) / AnchorSteps; fz = gz + (q / AnchorSteps + 0.5f) / AnchorSteps;
                int ax = Math.Min(c.Size - 1, (int)(fx * SpacingM / c.CellMeters)), az = Math.Min(c.Size - 1, (int)(fz * SpacingM / c.CellMeters));
                if (!sea[az * c.Size + ax] && Arid(s, climate, c.Size, ax, az)) return true;
            }
            fx = fz = 0f;
            return false;
        }

        static bool Near(List<float> placed, float x, float z)
        {
            for (int q = 0; q < placed.Count; q += 2)
            {
                float dx = placed[q] - x, dz = placed[q + 1] - z;
                if (dx * dx + dz * dz < GroupGapM * GroupGapM) return true;
            }
            return false;
        }

        static float Mix(float a, float b, float t) => a + (b - a) * t;

        static void Dir(float a, float b, out float x, out float z)
        {
            x = 2f * a - 1f; z = 2f * b - 1f;
            float l = (float)Math.Sqrt(x * x + z * z);
            if (l < 0.2f) { x = 1f; z = 0f; return; }
            x /= l; z /= l;
        }

        static MesaSketch Main(float x, float z, float[] v, int o, float scale)
        {
            float half = Math.Max(MinHalfM, scale * Mix(MinHalfM, MaxHalfM, v[o]));
            var b = new MesaSketch { X = x, Z = z, Half = half, Spread = TalusSpread, Flat = PedimentFlat, Tx = MaxTilt * (2f * v[o + 2] - 1f), Tz = MaxTilt * (2f * v[o + 3] - 1f), Seed = (uint)(v[o + 4] * 16777216f) * 0x9E3779B1u + 0x2545F491u, L = new float[3 * Lobes] };
            b.L[0] = x; b.L[1] = z; b.L[2] = 0.85f * half;
            for (int k = 1; k < Lobes; k++)
            {
                int p = o + 5 + 4 * (k - 1);
                Dir(v[p], v[p + 1], out float dx, out float dz);
                float off = Mix(0.35f, 0.7f, v[p + 2]) * half;
                b.L[3 * k] = x + dx * off; b.L[3 * k + 1] = z + dz * off; b.L[3 * k + 2] = Mix(0.4f, 0.65f, v[p + 3]) * half;
            }
            return b;
        }

        public static float Extent(MesaSketch b)
        {
            float e = 0f;
            for (int k = 0; k < b.L.Length; k += 3)
            {
                float dx = b.L[k] - b.X, dz = b.L[k + 1] - b.Z;
                e = Math.Max(e, (float)Math.Sqrt(dx * dx + dz * dz) + b.L[k + 2]);
            }
            return e + OutlineNoise * b.Half + NotchM;
        }

        static float Core(MesaSketch b, float rise) => Extent(b) + rise / CliffGrade + b.Spread * rise;

        public static float Toe(MesaSketch b, float rise)
        {
            float fall = b.Top - b.Cap - b.Base;
            return b.Cap / CliffGrade + (fall > rise ? b.Spread * fall * (1f - (float)Math.Sqrt(rise / fall)) : 0f);
        }

        public static float Reach(MesaSketch b, float rise) => Core(b, rise) + (b.Flat > 0f ? PedimentShare : 0f) * b.Spread * rise;

        public static float Dist(MesaSketch b, float x, float z)
        {
            float d = float.MaxValue;
            for (int k = 0; k < b.L.Length; k += 3)
            {
                float dx = x - b.L[k], dz = z - b.L[k + 1];
                d = Math.Min(d, (float)Math.Sqrt(dx * dx + dz * dz) - b.L[k + 2]);
            }
            float f = 1f / (0.8f * b.Half);
            return d + OutlineNoise * b.Half * Noise.Fbm(b.Seed, x * f, z * f, 3) + NotchM * Noise.Fbm(b.Seed ^ 0x5BD1E995u, x / 40f, z / 40f, 2);
        }

        static float Profile(MesaSketch b, float x, float z, float d)
        {
            float top = b.Top + b.Tx * (x - b.X) + b.Tz * (z - b.Z) + TopNoiseM * Noise.Fbm(b.Seed ^ 0x27D4EB2Fu, x / 60f, z / 60f, 2);
            if (d <= 0f) return top;
            float cliff = b.Cap / CliffGrade;
            if (d < cliff) return top - b.Cap * d / cliff;
            float fall = top - b.Cap - b.Base;
            if (fall <= 0f) return float.MinValue;
            float u = (d - cliff) / (b.Spread * fall);
            if (u >= 1f) return float.MinValue;
            float w = 1f - u;
            return b.Base + fall * w * w - GullyShare * fall * 4f * u * w * Math.Max(0f, Noise.Fbm(b.Seed ^ 0x165667B1u, x / 30f, z / 30f, 2));
        }

        static void Stamp(WorldConfig c, bool[] sea, float[] h, MesaSketch b)
        {
            int n = c.Size;
            float cm = c.CellMeters, reach = Reach(b, b.Top - b.Base);
            int x0 = Math.Max(0, (int)Math.Floor((b.X - reach) / cm)), x1 = Math.Min(n - 1, (int)Math.Ceiling((b.X + reach) / cm));
            int y0 = Math.Max(0, (int)Math.Floor((b.Z - reach) / cm)), y1 = Math.Min(n - 1, (int)Math.Ceiling((b.Z + reach) / cm));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * n + x;
                    if (sea[i]) continue;
                    float px = x * cm, pz = y * cm, d = Dist(b, px, pz);
                    float ground = h[i];
                    if (b.Flat > 0f && ground > b.Base)
                    {
                        float talus = b.Cap / CliffGrade + b.Spread * (b.Top - b.Cap - b.Base);
                        float u = (d - talus) / (PedimentShare * b.Spread * (b.Top - b.Base));
                        float w = u <= 0f ? 1f : u >= 1f ? 0f : (1f - u) * (1f - u) * (1f + 2f * u);
                        ground -= b.Flat * w * (ground - b.Base);
                    }
                    h[i] = Math.Max(ground, Profile(b, px, pz, d));
                }
        }

        static bool Arid(WorldSketch s, ClimateField climate, int n, int x, int y)
        {
            int i = y * n + x;
            if (climate.PrecipitationMm[i] <= AridRainMm && climate.TemperatureC[i] >= AridMinTempC) return true;
            foreach (var g in s.Regions)
                if (Array.IndexOf(Biomes, g.Biome) >= 0 && (x - g.X) * (x - g.X) + (y - g.Y) * (y - g.Y) <= g.Radius * g.Radius) return true;
            return false;
        }

        static bool Site(WorldConfig c, WorldSketch s, bool[] sea, float[] h, ClimateField climate, MesaSketch b, float reach, float rise)
        {
            int n = c.Size;
            float cm = c.CellMeters;
            int cx = (int)Math.Round(b.X / cm), cy = (int)Math.Round(b.Z / cm);
            float core = Core(b, rise);
            int coast = (int)Math.Ceiling((CoastPadM + Extent(b) + rise / CliffGrade) / cm);
            int edge = Math.Max(coast, (int)Math.Ceiling(reach / cm));
            if (cx - edge < 1 || cy - edge < 1 || cx + edge >= n - 1 || cy + edge >= n - 1) return false;
            if (!Arid(s, climate, n, cx, cy)) return false;
            int hr = (int)Math.Round(b.Half / cm);
            int wet = 0;
            for (int k = 0; k < 8; k++)
                if (!Arid(s, climate, n, cx + Grid.DX[k] * hr, cy + Grid.DY[k] * hr)) wet++;
            if (wet > 3) return false;
            foreach (var lake in s.Lakes)
                if (lake.Gap(cx, cy, 1) < core / cm) return false;
            int step = Math.Max(1, coast / 24);
            for (int dy = -coast; dy <= coast; dy += step)
                for (int dx = -coast; dx <= coast; dx += step)
                    if (dx * dx + dy * dy <= coast * coast && sea[(cy + dy) * n + cx + dx]) return false;
            int rr = (int)Math.Ceiling(reach / cm), inner = (int)Math.Ceiling(1.2f * b.Half / cm);
            step = Math.Max(1, (int)Math.Round(SampleM / cm));
            float lo = float.MaxValue, hi = float.MinValue;
            double sum = 0;
            int count = 0;
            for (int dy = -rr; dy <= rr; dy += step)
                for (int dx = -rr; dx <= rr; dx += step)
                {
                    int d2 = dx * dx + dy * dy;
                    if (d2 > rr * rr) continue;
                    int i = (cy + dy) * n + cx + dx;
                    if (s.Beach[i] > 0f && s.Coast[i] * cm < c.BeachMeters) return false;
                    if (s.Landmark[i] || s.Plateau[i] > 0f || s.Cliff[i] > 0f || s.Valley[i] > ValleyMax) return false;
                    if (d2 > inner * inner) continue;
                    lo = Math.Min(lo, h[i]); hi = Math.Max(hi, h[i]);
                    sum += h[i]; count++;
                }
            if (count == 0 || hi - lo > ReliefShare * rise) return false;
            b.Base = (float)(sum / count);
            b.Hi = hi;
            return true;
        }

        static float[] Flow(WorldConfig c, bool[] sea, float[] h)
        {
            int n = c.Size;
            float cm = c.CellMeters;
            var surface = Drainage.Fill(h, sea, n, 1e-3f);
            var recv = Drainage.Receivers(surface, sea, n, cm);
            var stack = Drainage.Stack(recv, n);
            var weight = new float[n * n];
            for (int i = 0; i < weight.Length; i++) weight[i] = cm * cm;
            return Drainage.Accumulate(stack, recv, weight);
        }

        static bool Wet(WorldConfig c, float[] flow, float x, float z, float reach)
        {
            int n = c.Size;
            float cm = c.CellMeters;
            int cx = (int)Math.Round(x / cm), cy = (int)Math.Round(z / cm), rr = (int)Math.Ceiling(reach / cm);
            for (int dy = -rr; dy <= rr; dy++)
                for (int dx = -rr; dx <= rr; dx++)
                {
                    if (dx * dx + dy * dy > rr * rr) continue;
                    int px = cx + dx, py = cy + dy;
                    if (px < 0 || py < 0 || px >= n || py >= n || flow[py * n + px] > FlowAreaM2) return true;
                }
            return false;
        }

        static void Buttes(WorldConfig c, WorldSketch s, bool[] sea, float[] h, ClimateField climate, float[] flow, MesaSketch m, float[] v, int o)
        {
            int count = (int)(v[o] * (MaxButtes + 1));
            float cm = c.CellMeters;
            int n = c.Size;
            for (int q = 0; q < Math.Min(count, MaxButtes); q++)
            {
                int p = o + 1 + 8 * q;
                Dir(v[p], v[p + 1], out float dx, out float dz);
                float edge = 0f;
                while (edge < 3f * m.Half && Dist(m, m.X + dx * edge, m.Z + dz * edge) <= 0f) edge += 10f;
                float half = Mix(ButteMinHalfM, ButteMaxHalfM, v[p + 3]), away = edge + m.Cap / CliffGrade + Mix(ButteMinGapM, ButteMaxGapM, v[p + 2]) + half;
                float x = m.X + dx * away, z = m.Z + dz * away;
                Dir(v[p + 6], v[p + 7], out float lx, out float lz);
                var b = new MesaSketch { Butte = true, X = x, Z = z, Half = half, Spread = ButteTalusSpread, Tx = 0f, Tz = 0f, Seed = (uint)(v[p + 5] * 16777216f) * 0x85EBCA77u + 0x68E31DA4u, L = new float[6] };
                b.L[0] = x; b.L[1] = z; b.L[2] = half;
                b.L[3] = x + lx * 0.5f * half; b.L[4] = z + lz * 0.5f * half; b.L[5] = 0.7f * half;
                float top = m.Top - ButteSlack * v[p + 4];
                int cx = (int)Math.Round(x / cm), cy = (int)Math.Round(z / cm);
                if (cx < 1 || cy < 1 || cx >= n - 1 || cy >= n - 1) continue;
                float rise = top - h[cy * n + cx];
                if (rise < 2f * half + ButteSlack || rise > 1.3f * MaxRiseM) continue;
                float reach = Reach(b, rise);
                if (!Site(c, s, sea, h, climate, b, reach, rise) || Wet(c, flow, x, z, reach)) continue;
                rise = top - b.Base;
                if (rise < 2f * half + ButteSlack) continue;
                b.Top = top;
                b.Cap = ButteCapShare * rise;
                Stamp(c, sea, h, b);
                s.Mesas.Add(b);
            }
        }
    }
}
