using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float WatchtowerU = 1 / 12f, WatchR = 2.6f, WatchT = 0.8f, WatchCH = 0.78f, WatchBase = -0.6f, WatchSill = 2.34f, WatchChord = 1.015f;

        static float WatchLow(int c) => c == 0 ? WatchBase : WatchCH * c;

        static void WatchBlock(StructureLayout l, ref Rng rng, in Frame f, int k, float off, float w, float y, float h, bool top)
        {
            float r = WatchR + rng.Range(-0.03f, 0.03f), tx = -Sin16(k), tz = Cos16(k);
            float x = Cos16(k) * r + tx * off, z = Sin16(k) * r + tz * off;
            float lean = top ? rng.Range(-0.15f, 0.15f) : rng.Range(-0.02f, 0.02f);
            if (top) { x += Cos16(k) * rng.Range(-0.08f, 0.08f); z += Sin16(k) * rng.Range(-0.08f, 0.08f); }
            Hewn(l, rng.NextU64(), f, x, y, z, w * 1.04f, h, WatchT * rng.Range(0.92f, 1.02f), tx, tz, 0, 0, rng.Range(0.9f, 0.95f), lean, 0, 0.03f);
        }

        static int WatchDist(int a, int b)
        {
            int d = Math.Abs(a - b) & 15;
            return Math.Min(d, 16 - d);
        }

        static void Watchtower(StructureLayout l, ulong key, in Frame root)
        {
            var rng = Rng.Stream(key, "watchtower");
            var f = root;
            int start = l.Pieces.Count;
            int storeys = rng.Range(2, 5), full = storeys * 4, minC = Math.Max(4, (storeys - 1) * 4);
            int fall = 4 + rng.Range(-1, 2), door = (fall + (rng.NextFloat() < 0.5f ? 4 : 12)) & 15;
            var cnt = new int[16];
            for (int k = 0; k < 16; k++)
            {
                int d = WatchDist(k, fall);
                int drop = d < 4.5f ? (int)Math.Round((full - minC) * (1 - d / 4.5f)) : 0;
                cnt[k] = Math.Max(minC, Math.Min(full, full - drop + rng.Range(-1, 2)));
            }
            int notch = (fall + 8 + rng.Range(-3, 4)) & 15;
            cnt[notch] = Math.Max(minC, cnt[notch] - 1);
            var slit = new bool[16];
            int want = rng.Range(1, 3), got = 0;
            for (int t = 0, k = rng.Range(0, 16); t < 16 && got < want; t++, k = (k + 1) & 15)
            {
                if (WatchDist(k, door) < 2 || WatchDist(k, fall) < 3 || cnt[k] < 7 || slit[(k + 1) & 15] || slit[(k + 15) & 15]) continue;
                slit[k] = true;
                got++;
            }

            for (int k = 0; k < 16; k++)
            {
                float rx = Cos16(k) * WatchR, rz = Sin16(k) * WatchR;
                for (int c = 0; c < cnt[k]; c++)
                {
                    float y = WatchLow(c), h = c == 0 ? WatchCH - WatchBase : WatchCH;
                    bool isTop = c == cnt[k] - 1;
                    if (k == door)
                    {
                        if (c == 0) { WatchBlock(l, ref rng, f, k, 0, WatchChord, WatchBase, -WatchBase, false); continue; }
                        if (c < 3) continue;
                    }
                    if (k != door && !slit[k] && c >= 2 && c < cnt[k] - 2 && (c & 1) == 0)
                    {
                        WatchBlock(l, ref rng, f, k, 0, WatchChord, y, 2 * h, false);
                        c++;
                        continue;
                    }
                    if (slit[k] && (c == 4 || c == 5))
                    {
                        WatchBlock(l, ref rng, f, k, -0.315f, 0.46f, y, h, false);
                        WatchBlock(l, ref rng, f, k, 0.315f, 0.46f, y, h, false);
                        continue;
                    }
                    if (isTop && k != door && rng.NextFloat() < 0.22f) continue;
                    float hh = isTop && k != door ? h * rng.Range(0.4f, 1f) : h;
                    if (!isTop) WatchBlock(l, ref rng, f, k, 0, WatchChord, y, h, false);
                    else
                    {
                        float t = rng.Range(0.3f, 0.7f), w1 = WatchChord * t, w2 = WatchChord - w1;
                        WatchBlock(l, ref rng, f, k, -0.5f * WatchChord + 0.5f * w1, w1, y, hh, isTop);
                        WatchBlock(l, ref rng, f, k, 0.5f * WatchChord - 0.5f * w2, w2, y, hh, isTop);
                    }
                }
                float topY = WatchCH * cnt[k] - 0.15f, tx = -Sin16(k), tz = Cos16(k);
                if (k == door)
                {
                    Hewn(l, rng.NextU64(), f, rx, WatchBase, rz, WatchChord * 1.06f, -WatchBase, WatchT, tx, tz, 1, 2, 0.8f, 0, 0, 0.03f);
                    Hewn(l, rng.NextU64(), f, rx, WatchSill, rz, WatchChord * 1.06f, topY - WatchSill, WatchT, tx, tz, 1, 2, 0.8f, 0, 0, 0.03f);
                    Col(l, f, rx, rz, 0.5f, WatchSill, topY);
                }
                else
                {
                    Hewn(l, rng.NextU64(), f, rx, WatchBase, rz, WatchChord * 1.06f, topY - WatchBase, WatchT, tx, tz, 1, 2, 0.8f, 0, 0, 0.03f);
                    Col(l, f, rx, rz, WatchDist(k, door) == 1 ? 0.5f : 0.62f, WatchBase, topY);
                }
                Touch(l, f, rx, 0, rz, 0.9f);
            }

            float dx = Cos16(door), dz = Sin16(door);
            for (int i = 0; i < 2; i++)
            {
                float rr = WatchR + 0.75f + 0.8f * i, w = 1.5f - 0.2f * i, sx = dx * rr, sz = dz * rr;
                Hewn(l, rng.NextU64(), f, sx, -0.22f - 0.06f * i, sz, w, 0.3f, 0.8f, -dz, dx, 0, 1, rng.Range(0.88f, 0.93f), rng.Range(-0.02f, 0.02f));
                Touch(l, f, sx, 0, sz, 0.8f);
            }

            int apron = rng.Range(9, 14);
            for (int i = 0; i < apron; i++)
            {
                int d = (fall + rng.Range(-2, 3)) & 15;
                float t = rng.NextFloat(), rr = WatchR + 0.4f + t * 3.4f, x = Cos16(d) * rr + rng.Range(-0.4f, 0.4f), z = Sin16(d) * rr + rng.Range(-0.4f, 0.4f);
                float sz = (0.95f - 0.55f * t) * rng.Range(0.7f, 1.1f), y = -0.25f - 0.06f * (rr - WatchR) + rng.Range(0f, 0.35f) * (1 - t);
                int q = rng.Range(0, 16);
                if (rng.NextFloat() < 0.7f) Hewn(l, rng.NextU64(), f, x, y, z, sz * 1.2f, sz * 0.8f, sz, Cos16(q), Sin16(q), 0, 1, rng.Range(0.45f, 0.6f), rng.Range(-0.2f, 0.2f), rng.Range(-0.15f, 0.15f));
                else Rock(l, rng.NextU64(), f, x, y, z, sz * 1.2f, sz * 0.7f, sz, Cos16(q), Sin16(q), 0, 1, rng.Range(-0.1f, 0.1f));
                Touch(l, f, x, 0, z, 0.6f * sz + 0.2f);
            }
            int fallen = rng.Range(2, 3);
            for (int i = 0; i < fallen; i++)
            {
                int d = (fall + rng.Range(-2, 3)) & 15, q = rng.Range(0, 16);
                float rr = WatchR + rng.Range(1.0f, 3.0f), x = Cos16(d) * rr, z = Sin16(d) * rr;
                Hewn(l, rng.NextU64(), f, x, -0.3f - 0.06f * (rr - WatchR), z, rng.Range(1.1f, 1.5f), WatchCH, WatchT, Cos16(q), Sin16(q), 0, 1, rng.Range(0.9f, 0.94f), rng.Range(-0.1f, 0.1f), rng.Range(-0.1f, 0.1f));
                Touch(l, f, x, 0, z, 0.9f);
            }
            int piles = 1;
            for (int i = 0; i < piles; i++)
            {
                int d = (fall + rng.Range(-1, 2)) & 15;
                float rr = WatchR + 1.8f + 2f * i;
                Rubble(l, ref rng, f, Cos16(d) * rr + rng.Range(-0.5f, 0.5f), Sin16(d) * rr + rng.Range(-0.5f, 0.5f), 0.3f);
            }
            Rubble(l, ref rng, f, rng.Range(-0.5f, 0.5f), rng.Range(-0.5f, 0.5f), 0.3f);
            Mossy(l, start);
        }
    }

    public sealed partial class ObjectField
    {
        public const float WatchProbeM = 5f, WatchReliefM = 1.1f, WatchRiseProbeM = 60f, WatchRiseM = 5f, WatchRiseCapM = 25f, WatchViewM = 200f, WatchOpenRiseM = 4f, WatchRiverM = 30f;
        public const int WatchOpenMin = 5;

        public float WatchFit(float x, float z)
        {
            if (x < WatchViewM || z < WatchViewM || x >= worldM - WatchViewM || z >= worldM - WatchViewM) return float.MinValue;
            int i = Cell(z) * n + Cell(x);
            if (w.Sea[i] || w.Water.LakeId[i] >= 0 || RiverGap(x, z) < WatchRiverM) return float.MinValue;
            float y = GroundAt(detail, cm, x, z), relief = 0, rise = 0;
            int open = 0;
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, 0.70710678f, out float ux, out float uz);
                relief = Math.Max(relief, Math.Abs(GroundAt(detail, cm, x + ux * WatchProbeM, z + uz * WatchProbeM) - y));
                if (relief > WatchReliefM) return float.MinValue;
                rise += 0.125f * (y - GroundAt(detail, cm, x + ux * WatchRiseProbeM, z + uz * WatchRiseProbeM));
                if (GroundAt(detail, cm, x + ux * WatchViewM, z + uz * WatchViewM) <= y + WatchOpenRiseM) open++;
            }
            if (rise < WatchRiseM || open < WatchOpenMin) return float.MinValue;
            return 0.3f * Math.Min(rise, WatchRiseCapM) + 0.3f * open - relief;
        }

        public string WatchFault(ObjectInstance o)
        {
            if (WatchFit(o.X, o.Z) == float.MinValue) return "is not on open high ground";
            SeatRange(detail, cm, Seats(lib.Objects[o.Def]), o, out float lo, out float hi);
            if (hi - lo > Structures.SeatM) return "stands on uneven ground";
            foreach (var t in TownPlans())
            {
                float dx = t.X - o.X, dz = t.Z - o.Z;
                if (Math.Sqrt(dx * dx + dz * dz) - t.Radius < TownClearM) return "stands within 400 m of a town";
            }
            float r = SiteRadius(o);
            foreach (var f in edits)
                if ((f.Shape == FeatureEdit.Cave || f.Shape == FeatureEdit.CaveRoom || f.Shape == FeatureEdit.Sink || f.Shape == FeatureEdit.Shaft || f.Shape == FeatureEdit.Mine) && o.X + r >= f.MinX && o.X - r <= f.MaxX && o.Z + r >= f.MinZ && o.Z - r <= f.MaxZ) return "lies over a cave";
            return null;
        }
    }
}
