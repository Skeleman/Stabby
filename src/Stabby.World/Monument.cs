using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float MonumentU = 1 / 12f, MonumentBaseM = -0.3f, MonumentBlockM = 0.8f;

        static float MonumentTiers(StructureLayout l, ref Rng rng, in Frame f, float[] half, float[] tops)
        {
            float y = MonumentBaseM;
            for (int t = 0; t < half.Length; t++)
            {
                float h = half[t], top = tops[t], sy = top - y;
                int per = Math.Max(2, (int)Math.Round(2 * h / MonumentBlockM));
                float bw = 2 * h / per;
                Hewn(l, rng.NextU64(), f, 0, y, 0, 2 * h - bw, sy - 0.03f, 2 * h - bw, 1, 0, 0, 2, 0.95f);
                for (int e = 0; e < 4 * (per - 1); e++)
                {
                    int side = e / (per - 1), k = e % (per - 1);
                    float a = -h + bw * (k + 0.5f), x = side == 0 ? a : side == 1 ? h - 0.5f * bw : side == 2 ? -a : -h + 0.5f * bw, z = side == 0 ? -h + 0.5f * bw : side == 1 ? a : side == 2 ? h - 0.5f * bw : -a;
                    bool front = side == 0;
                    float sag = front ? rng.Range(0.01f, 0.05f) : rng.Range(0f, 0.02f);
                    Hewn(l, rng.NextU64(), f, x, y, z, bw * rng.Range(0.93f, 0.99f), sy - sag, bw * rng.Range(0.93f, 0.99f), 1, 0, 0, t == 0 ? 2 : 1, front ? rng.Range(0.9f, 0.93f) : rng.Range(0.93f, 0.97f), rng.Range(-0.02f, 0.02f), rng.Range(-0.02f, 0.02f));
                }
                y = top;
            }
            return y;
        }

        static float Obelisk(StructureLayout l, ref Rng rng, in Frame f, float y)
        {
            float pw = rng.Range(1.4f, 1.6f), ph = rng.Range(0.7f, 0.9f);
            Hewn(l, rng.NextU64(), f, 0, y, 0, pw, ph, pw, 1, 0, 0, 2, rng.Range(0.92f, 0.96f));
            y += ph;
            float cw = pw + 0.2f;
            Hewn(l, rng.NextU64(), f, 0, y, 0, cw, 0.18f, cw, 1, 0, 0, 2, rng.Range(0.91f, 0.94f));
            y += 0.18f;
            float tall = rng.Range(5f, 7f), w0 = rng.Range(0.85f, 0.95f), w1 = rng.Range(0.5f, 0.6f), base0 = y, px = 0, pz = 0;
            int courses = rng.Range(4, 7);
            for (int k = 0; k < courses; k++)
            {
                float a = (float)k / courses, b = (float)(k + 1) / courses, wa = w0 + (w1 - w0) * a, wb = w0 + (w1 - w0) * b, h = tall / courses;
                Hewn(l, rng.NextU64(), f, px, y, pz, 0.5f * (wa + wb), h, wa, 1, 0, 0, 2, rng.Range(0.92f, 0.96f), 0, 0, (wa - wb) / (2 * h));
                y += h;
                px += rng.Range(-0.015f, 0.015f);
                pz += rng.Range(-0.015f, 0.015f);
            }
            float th = w1 * rng.Range(0.8f, 1f);
            Hewn(l, rng.NextU64(), f, px, y, pz, w1, th, w1, 1, 0, 0, 2, rng.Range(0.91f, 0.94f), 0, 0, 0.5f * w1 / th);
            Hewn(l, rng.NextU64(), f, px, y, pz, w1, th, w1, Cos16(4), Sin16(4), 0, 2, rng.Range(0.91f, 0.94f), 0, 0, 0.5f * w1 / th);
            Col(l, f, 0, 0, 0.5f * w0, base0, y + th);
            return pw;
        }

        static float StatuePlinth(StructureLayout l, ref Rng rng, in Frame f, float y, int tail)
        {
            float bw = rng.Range(1.8f, 2f);
            Hewn(l, rng.NextU64(), f, 0, y, 0, bw, 0.3f, bw, 1, 0, 0, 2, rng.Range(0.92f, 0.95f));
            y += 0.3f;
            float dw = bw - 0.45f, dh = rng.Range(1.4f, 1.8f);
            Hewn(l, rng.NextU64(), f, 0, y, 0, dw, dh, dw, 1, 0, 0, 2, rng.Range(0.93f, 0.96f));
            y += dh;
            Hewn(l, rng.NextU64(), f, 0, y, 0, bw - 0.1f, 0.22f, bw - 0.1f, 1, 0, 0, 2, rng.Range(0.9f, 0.93f));
            y += 0.22f;
            float sh = rng.Range(0.6f, 1.1f);
            Hewn(l, rng.NextU64(), f, rng.Range(-0.1f, 0.1f), y, rng.Range(-0.1f, 0.1f), 0.95f, sh, 0.6f, 1, 0, 0, 2, rng.Range(0.5f, 0.6f), rng.Range(-0.05f, 0.05f), rng.Range(-0.05f, 0.05f), 0.08f);
            Col(l, f, 0, 0, 0.5f * dw, 0, y + sh);
            int d = (tail + rng.Range(-2, 3)) & 15;
            float r = rng.Range(3.6f, 4.2f), x = Cos16(d) * r, z = Sin16(d) * r;
            int q = (d + 4) & 15;
            Hewn(l, rng.NextU64(), f, x, -0.2f, z, 0.9f, 0.75f, 1.9f, Cos16(q), Sin16(q), 0, 2, rng.Range(0.42f, 0.5f), rng.Range(-0.1f, 0.1f), rng.Range(-0.15f, 0.15f), 0.05f);
            Touch(l, f, x, 0, z, 1.1f);
            float hx = x + Cos16(d) * 1.5f, hz = z + Sin16(d) * 1.5f;
            Hewn(l, rng.NextU64(), f, hx, -0.12f, hz, 0.5f, 0.5f, 0.5f, 1, 0, 0, 1, rng.Range(0.32f, 0.4f), rng.Range(-0.2f, 0.2f), rng.Range(-0.2f, 0.2f));
            Touch(l, f, hx, 0, hz, 0.45f);
            return bw;
        }

        static void Monument(StructureLayout l, ulong key, in Frame root)
        {
            var rng = Rng.Stream(key, "monument");
            int d0 = rng.Range(0, 16);
            var f = root.Child(0, 0, 0, Cos16(d0), Sin16(d0));
            int start = l.Pieces.Count;
            float h0 = rng.Range(2.3f, 2.5f);
            var half = new[] { h0, h0 - rng.Range(0.5f, 0.6f), h0 - rng.Range(1.05f, 1.15f) };
            var tops = new[] { 0.35f, 0.7f, 1.05f };
            float y = MonumentTiers(l, ref rng, f, half, tops);
            int tail = rng.Range(0, 16);
            if (rng.NextFloat() < 0.55f) Obelisk(l, ref rng, f, y);
            else StatuePlinth(l, ref rng, f, y, tail);

            for (float x = -h0 + 0.4f; x < h0 - 0.2f; x += 0.8f)
                for (float z = -h0 + 0.4f; z < h0 - 0.2f; z += 0.8f)
                {
                    float m = Math.Max(Math.Abs(x), Math.Abs(z)), top = tops[0];
                    for (int t = 1; t < half.Length; t++) if (m <= half[t] - 0.2f) top = tops[t];
                    Col(l, f, x, z, 0.55f, MonumentBaseM, top);
                }
            for (int k = 0; k < 8; k++)
            {
                float ux = k % 4 == 0 ? 0 : k < 4 ? 1 : -1, uz = (k + 2) % 4 == 0 ? 0 : k > 2 && k < 6 ? -1 : 1;
                Touch(l, f, ux * (h0 - 0.5f), 0, uz * (h0 - 0.5f), 0.9f);
            }

            int blocks = rng.Range(2, 6);
            for (int k = 0; k < blocks; k++)
            {
                int d = (tail + 8 + rng.Range(-5, 6)) & 15;
                float r = rng.Range(3.6f, 5f), x = Cos16(d) * r, z = Sin16(d) * r, s = rng.Range(0.6f, 0.9f);
                int q = rng.Range(0, 16);
                Hewn(l, rng.NextU64(), f, x, -0.2f, z, s, s * rng.Range(0.6f, 0.85f), s * rng.Range(0.8f, 1f), Cos16(q), Sin16(q), 0, 1, rng.Range(0.9f, 0.94f), rng.Range(-0.25f, 0.25f), rng.Range(-0.25f, 0.25f));
                Touch(l, f, x, 0, z, 0.6f * s + 0.15f);
            }
            Mossy(l, start);

            int offer = l.Pieces.Count, stones = rng.Range(2, 5);
            for (int k = 0; k < stones; k++)
            {
                float x = rng.Range(-1.4f, 1.4f), z = -h0 - rng.Range(0.35f, 0.9f), w = rng.Range(0.25f, 0.4f);
                int q = rng.Range(0, 16);
                Hewn(l, rng.NextU64(), f, x, -0.06f, z, w, rng.Range(0.1f, 0.14f), w * rng.Range(0.7f, 0.9f), Cos16(q), Sin16(q), 0, 1, rng.Range(0.45f, 0.6f));
                Touch(l, f, x, 0, z, 0.6f * w);
            }
            Flat(l, offer);
            Drape(l, offer);
        }
    }

    public sealed partial class ObjectField
    {
        public const float MonumentProbeM = 10f, MonumentReliefM = 1.2f, MonumentRiseProbeM = 50f, MonumentRiseM = 1f, MonumentRiseCapM = 10f, MonumentRoadMinM = 14f, MonumentRoadM = 40f, MonumentRiverM = 30f;

        public float MonumentFit(float x, float z)
        {
            if (x < MonumentRiseProbeM || z < MonumentRiseProbeM || x >= worldM - MonumentRiseProbeM || z >= worldM - MonumentRiseProbeM) return float.MinValue;
            int i = Cell(z) * n + Cell(x);
            if (w.Sea[i] || w.Water.LakeId[i] >= 0 || RiverGap(x, z) < MonumentRiverM) return float.MinValue;
            float y = GroundAt(detail, cm, x, z), relief = 0, rise = 0;
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, 0.70710678f, out float ux, out float uz);
                relief = Math.Max(relief, Math.Abs(GroundAt(detail, cm, x + ux * MonumentProbeM, z + uz * MonumentProbeM) - y));
                if (relief > MonumentReliefM) return float.MinValue;
                rise += 0.125f * (y - GroundAt(detail, cm, x + ux * MonumentRiseProbeM, z + uz * MonumentRiseProbeM));
            }
            float d = RoadSide(x, z, out _, out _);
            bool road = d >= MonumentRoadMinM && d <= MonumentRoadM;
            if (rise < MonumentRiseM && !road) return float.MinValue;
            return 0.3f * Math.Min(rise, MonumentRiseCapM) + (road ? 1 : 0) - relief;
        }

        public string MonumentFault(ObjectInstance o)
        {
            if (MonumentFit(o.X, o.Z) == float.MinValue) return "is not on open high ground or beside a road";
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
