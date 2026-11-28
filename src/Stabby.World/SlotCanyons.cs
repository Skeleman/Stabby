using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static class SlotCanyons
    {
        public const float MinW = 0.9f, MaxW = 2.4f, CheckMinW = 0.75f, CheckMaxW = 2.5f, MinDepth = 15f, MaxDepth = 40f, MinLen = 150f, MaxLen = 500f;
        public const float StepM = 16f, ScanM = 40f, ProbeM = 4f, MinSlope = 0.1f, FlatSlope = 0.035f, MinGrade = 0.03f, MaxGrade = 0.07f, MaxRise = 0.15f, LoopM = 32f, MouthCut = 0.4f, OpenM = 24f, OpenRiseM = 1f, OpenDrop = 0.25f, SkinM = 0.6f;
        public const float Blend = 1.2f, DryM = 150f, GapM = 1500f, ClearM = 12f, RimPad = 3f, HeadRoomM = 0.6f, StoneTopM = 0.9f, ChockChance = 0.3f;
        public const int Tries = 300, Shortlist = 6, Sub = 2;
        public static readonly string[] Biomes = { "desert", "cold-desert", "steppe", "shrubland" };

        public static void Place(GeneratedWorld w, TerrainDetail detail, List<FeatureEdit> list)
        {
            if (detail == null || w.Height == null || w.Biome == null) return;
            var r = Rng.Stream(w.Config.Seed, "slotcanyons");
            ulong seed = r.NextU64();
            int n = w.Config.Size, want = n >= 1024 ? r.Range(1, 4) : 1, tc = Features.TileCells, tiles = n / tc;
            float cm = w.Config.CellMeters;
            var biomes = Landforms.BiomeMask(w, Biomes);
            int pad = Features.MarginCells + 1, step = Math.Max(1, (int)Math.Round(ScanM / cm));
            var picks = new List<KeyValuePair<float, int>>();
            for (int ty = 0; ty < tiles; ty++)
                for (int tx = 0; tx < tiles; tx++)
                    for (int y = ty * tc + pad; y < (ty + 1) * tc - pad; y += step)
                        for (int x = tx * tc + pad; x < (tx + 1) * tc - pad; x += step)
                        {
                            int i = y * n + x, b = w.Biome[i];
                            if (b < 0 || b >= biomes.Length || !biomes[b] || !Features.Dry(w, i)) continue;
                            float gx = (w.Height[i + 1] - w.Height[i - 1]) / (2f * cm), gz = (w.Height[i + n] - w.Height[i - n]) / (2f * cm), slope = (float)Math.Sqrt(gx * gx + gz * gz);
                            if (slope < MinSlope) continue;
                            var q = new Rng(Rng.Mix(seed + (ulong)i));
                            picks.Add(new KeyValuePair<float, int>(0.5f * q.NextFloat() + Math.Min(slope, 0.5f), i));
                        }
            picks.Sort((a, b) => b.Key.CompareTo(a.Key) != 0 ? b.Key.CompareTo(a.Key) : a.Value.CompareTo(b.Value));
            var placed = new List<FeatureEdit>();
            var built = new List<FeatureEdit>();
            List<FeatureEdit> best = null;
            int bestLen = 0, seen = 0;
            for (int k = 0; k < picks.Count && k < Tries && placed.Count < want; k++)
            {
                int i = picks[k].Value, x = i % n, y = i / n;
                float px = (x + 0.5f) * cm, pz = (y + 0.5f) * cm;
                bool near = false;
                foreach (var c in placed) if ((c.Ax - px) * (c.Ax - px) + (c.Az - pz) * (c.Az - pz) < GapM * GapM) near = true;
                if (!near && !Landforms.Crowded(w, px, pz, MaxLen))
                {
                    var q = new Rng(Rng.Mix(seed ^ (ulong)i * 0x9E3779B97F4A7C15UL));
                    float target = new Rng(Rng.Mix(seed ^ (ulong)i * 0xD1B54A32D192ED03UL)).Range(MinLen, MaxLen);
                    if (Build(w, detail, px, pz, cm, target, ref q, built) && !Clashes(built, list))
                    {
                        int len = 0;
                        foreach (var e in built) if (e.Shape == FeatureEdit.SlotCanyon) len++;
                        if (len > bestLen) { best = new List<FeatureEdit>(built); bestLen = len; }
                        seen++;
                    }
                }
                if (best != null && (seen >= Shortlist || k + 1 >= Math.Min(picks.Count, Tries)))
                {
                    list.AddRange(best);
                    placed.Add(best[0]);
                    best = null;
                    bestLen = seen = 0;
                }
            }
        }

        static bool Build(GeneratedWorld w, TerrainDetail detail, float hx, float hz, float cm, float target, ref Rng q, List<FeatureEdit> output)
        {
            output.Clear();
            var xs = new List<float>();
            var zs = new List<float>();
            float x = hx, z = hz, dx, dz, arc = 0f;
            if (!Downhill(detail, x, z, cm, out dx, out dz)) return false;
            int open = -1;
            float ox = 0f, oz = 0f;
            for (int k = 0; arc + StepM <= MaxLen; k++)
            {
                xs.Add(x);
                zs.Add(z);
                float turn = q.Range(-0.6f, 0.6f);
                if (Downhill(detail, x, z, cm, out float gx, out float gz)) { dx = 0.55f * dx + 0.45f * gx; dz = 0.55f * dz + 0.45f * gz; }
                float tx = dx - 0.35f * turn * dz, tz = dz + 0.35f * turn * dx, tl = (float)Math.Sqrt(tx * tx + tz * tz);
                dx = tx / tl;
                dz = tz / tl;
                float before = Ground(detail, x, z, cm);
                x += dx * StepM;
                z += dz * StepM;
                arc += StepM;
                bool loop = false;
                for (int j = 0; j + 3 < xs.Count; j++)
                    if ((xs[j] - x) * (xs[j] - x) + (zs[j] - z) * (zs[j] - z) < LoopM * LoopM) loop = true;
                if (loop) break;
                if (arc >= MinLen && (before - Ground(detail, x, z, cm)) / StepM < FlatSlope && Open(detail, x, z, dx, dz, cm))
                {
                    open = xs.Count;
                    ox = x;
                    oz = z;
                    if (arc >= target) break;
                }
            }
            if (open < 0) return false;
            xs.RemoveRange(open, xs.Count - open);
            zs.RemoveRange(open, zs.Count - open);
            xs.Add(ox);
            zs.Add(oz);
            xs.Reverse();
            zs.Reverse();
            Subdivide(xs);
            Subdivide(zs);
            var at = new float[xs.Count];
            for (int k = 1; k < xs.Count; k++) at[k] = at[k - 1] + (float)Math.Sqrt((xs[k] - xs[k - 1]) * (xs[k] - xs[k - 1]) + (zs[k] - zs[k - 1]) * (zs[k] - zs[k - 1]));
            float width = q.Range(MinW, MaxW), grade = q.Range(MinGrade, MaxGrade), sd = q.NextFloat();
            var probe = new FeatureEdit { Kind = FeatureEdit.Carve, Shape = FeatureEdit.SlotCanyon, W = width, S = sd };
            float mouth = Ground(detail, xs[0], zs[0], cm) - MouthCut, deepest = 0f;
            int end = 0;
            var floors = new List<float> { mouth };
            var rims = new List<float> { mouth + MouthCut };
            for (int k = 1; k < xs.Count; k++)
            {
                float low = float.MaxValue, sl = at[k] - at[k - 1];
                if (at[k] > MaxLen) break;
                for (int m = 1; m <= 4; m++) low = Math.Min(low, Ground(detail, xs[k - 1] + (xs[k] - xs[k - 1]) * m / 4f, zs[k - 1] + (zs[k] - zs[k - 1]) * m / 4f, cm));
                float prev = floors[k - 1], f = Math.Min(Math.Max(prev + grade * sl, low - MaxDepth), prev + MaxRise * sl);
                f = Math.Min(f, at[k] > OpenM ? low - SkinM : low);
                if (Math.Abs(f - prev) > MaxRise * sl) break;
                bool ok = true;
                for (int m = 1; m <= 4 && ok; m++)
                {
                    float u = at[k - 1] + sl * m / 4f, sx = xs[k - 1] + (xs[k] - xs[k - 1]) * m / 4f, sz = zs[k - 1] + (zs[k] - zs[k - 1]) * m / 4f;
                    float depth = Ground(detail, sx, sz, cm) - (prev + (f - prev) * m / 4f);
                    if (depth > MaxDepth || (u > OpenM && depth < SkinM) || depth < -MouthCut) ok = false;
                }
                if (!ok) break;
                float rim = Ground(detail, xs[k], zs[k], cm);
                floors.Add(f);
                rims.Add(rim);
                deepest = Math.Max(deepest, rim - f);
                end = k;
            }
            if (at[end] < MinLen || deepest < MinDepth) return false;
            int dry = (int)Math.Ceiling(DryM / cm), n = w.Config.Size;
            for (int k = 0; k <= end; k++)
            {
                int cx = (int)(xs[k] / cm), cz = (int)(zs[k] / cm);
                if (cx < 0 || cz < 0 || cx >= n || cz >= n || !Landforms.DryAround(w, cx, cz, dry)) return false;
            }
            if (Landforms.Crowded(w, xs[0], zs[0], 0f)) return false;
            float floorLow = float.MaxValue, top = float.MinValue, span = width * (1f + FeatureEdit.SlotBelly) + FeatureEdit.SlotFlare + FeatureEdit.SlotScallopM + FeatureEdit.SlotSwayM + FeatureEdit.SlotLeanM + Blend + 1f;
            for (int k = 0; k <= end; k++)
            {
                floorLow = Math.Min(floorLow, floors[k]);
                for (int a = -2; a <= 2; a++)
                    for (int b = -2; b <= 2; b++)
                        top = Math.Max(top, Ground(detail, xs[k] + a * 0.5f * span, zs[k] + b * 0.5f * span, cm));
            }
            float height = top - floorLow + RimPad;
            for (int k = end; k > 0; k--)
            {
                var e = probe;
                e.R = Blend;
                e.H = height;
                e.Ax = xs[k]; e.Ay = floors[k]; e.Az = zs[k];
                e.Bx = xs[k - 1]; e.By = floors[k - 1]; e.Bz = zs[k - 1];
                e.P = rims[k];
                e.T = rims[k - 1];
                float depth = 0.5f * (rims[k] + rims[k - 1] - floors[k] - floors[k - 1]);
                float chock = Math.Min(3.5f, 1.5f * width + 0.6f), lift = Math.Min(chock + 9f, 0.6f * depth);
                if (k > 2 * Sub && q.NextFloat() * Sub < ChockChance && lift > chock + Walk.Height + HeadRoomM + 1f)
                {
                    e.F = chock;
                    e.D = q.Range(chock + Walk.Height + HeadRoomM + 1f, lift);
                    e.N = q.Range(0.35f, 0.65f);
                }
                if (!Features.Inside(e, cm)) return false;
                output.Add(e);
            }
            int stones = q.Range(1, 4);
            for (int m = 0; m < stones; m++)
            {
                int k = q.Range(3 * Sub, end + 1);
                float t = q.Range(0.3f, 0.7f), sx = xs[k] + (xs[k - 1] - xs[k]) * t, sz = zs[k] + (zs[k - 1] - zs[k]) * t;
                float ex = xs[k - 1] - xs[k], ez = zs[k - 1] - zs[k], el = (float)Math.Sqrt(ex * ex + ez * ez), sway = probe.SlotSway(sx, sz);
                sx += -ez / el * sway;
                sz += ex / el * sway;
                float rr = 1.15f * width, floor = floors[k] + (floors[k - 1] - floors[k]) * t;
                float y = floor + StoneTopM - rr;
                var stone = new FeatureEdit { Kind = FeatureEdit.Add, Shape = FeatureEdit.Boulder, R = rr, N = ObjectField.BoulderRelief * rr, S = q.NextFloat(), Ax = sx, Ay = y, Az = sz, Bx = sx, By = y, Bz = sz };
                if (Features.Inside(stone, cm)) output.Add(stone);
            }
            return true;
        }

        static void Subdivide(List<float> p)
        {
            int c = p.Count;
            var o = new List<float>(2 * c);
            for (int k = 0; k + 1 < c; k++)
            {
                float a = p[Math.Max(0, k - 1)], b = p[k], d = p[k + 1], e = p[Math.Min(c - 1, k + 2)];
                o.Add(b);
                o.Add((9f * (b + d) - a - e) / 16f);
            }
            o.Add(p[c - 1]);
            p.Clear();
            p.AddRange(o);
        }

        static bool Open(TerrainDetail detail, float x, float z, float dx, float dz, float cm)
        {
            float g0 = Ground(detail, x, z, cm);
            for (int m = 1; m <= 3; m++)
            {
                float rise = Ground(detail, x + dx * OpenM * m / 3f, z + dz * OpenM * m / 3f, cm) - g0;
                if (rise > OpenRiseM || rise < -OpenDrop * OpenM) return false;
            }
            return true;
        }

        static bool Downhill(TerrainDetail detail, float x, float z, float cm, out float dx, out float dz)
        {
            float gx = Ground(detail, x + ProbeM, z, cm) - Ground(detail, x - ProbeM, z, cm), gz = Ground(detail, x, z + ProbeM, cm) - Ground(detail, x, z - ProbeM, cm), l = (float)Math.Sqrt(gx * gx + gz * gz);
            dx = l > 1e-4f ? -gx / l : 0f;
            dz = l > 1e-4f ? -gz / l : 0f;
            return l > 1e-4f;
        }

        static float Ground(TerrainDetail detail, float x, float z, float cm) => detail.HeightAt(x / cm, z / cm);

        static bool Clashes(List<FeatureEdit> built, List<FeatureEdit> list)
        {
            foreach (var e in built)
                foreach (var f in list)
                    if (e.MinX - ClearM < f.MaxX && e.MaxX + ClearM > f.MinX && e.MinZ - ClearM < f.MaxZ && e.MaxZ + ClearM > f.MinZ) return true;
            return false;
        }
    }
}
