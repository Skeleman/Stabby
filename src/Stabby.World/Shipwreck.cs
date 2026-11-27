using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float ShipwreckU = 1 / 15f, ShipwreckBuryM = 0.3f;

        static readonly float[] HullZ = { 0, 0.5f, 0.8f, 0.95f, 1 }, HullY = { 0, 0.1f, 0.3f, 0.6f, 1 };

        struct Hull
        {
            public float Len, Beam, Depth, Sheer, Rocker, C, S, Lift;

            public Hull(float len, float beam, float depth, float sheer, float heel)
            {
                Len = len; Beam = beam; Depth = depth; Sheer = sheer; Rocker = 0.35f * depth;
                C = 1 / (float)Math.Sqrt(1 + heel * heel); S = heel * C; Lift = 0;
                float low = float.MaxValue;
                for (int j = 0; j < HullZ.Length; j++)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Point(0, side, j, 0, out _, out float y);
                        low = Math.Min(low, y);
                    }
                Lift = -low - ShipwreckBuryM;
            }

            public float Spread(float x)
            {
                float t = 2 * x / Len;
                return Math.Max(0.12f, 1 - t * t);
            }

            public void Point(float x, int side, float j, float inset, out float z, out float y)
            {
                int a = Math.Min(HullZ.Length - 2, (int)j);
                float f = j - a, zc = HullZ[a] + (HullZ[a + 1] - HullZ[a]) * f, yc = HullY[a] + (HullY[a + 1] - HullY[a]) * f;
                float t = 2 * x / Len, t2 = t * t;
                float zz = side * (0.5f * Beam * zc * Spread(x) - inset), yy = yc * (Depth + Sheer * t2) + Rocker * t2 * t2 + inset * (1 - yc);
                z = zz * C + yy * S;
                y = yy * C - zz * S + Lift;
            }
        }

        static void HullSegment(StructureLayout l, ulong key, in Frame f, float x0, float x1, float za0, float ya0, float zb0, float yb0, float za1, float ya1, float zb1, float yb1, float th, int m, int g, int lo, int hi)
        {
            float za = 0.5f * (za0 + za1), ya = 0.5f * (ya0 + ya1), zb = 0.5f * (zb0 + zb1), yb = 0.5f * (yb0 + yb1);
            float dx = x1 - x0, dzp = 0.5f * (za1 + zb1 - za0 - zb0), len = (float)Math.Sqrt(dx * dx + dzp * dzp), c = dx / len, s = dzp / len;
            float dy = yb - ya, dz = zb - za, mx = 0.5f * (x0 + x1), mz = 0.5f * (za + zb);
            if (Math.Abs(dy) < 1e-4f && Math.Abs(dz) < 1e-4f) return;
            if (Math.Abs(dy) >= Math.Abs(dz))
            {
                float b = dz / dy;
                Box(l, key, f, mx, Math.Min(ya, yb), mz, len, Math.Abs(dy), th * (float)Math.Sqrt(1 + b * b), m, g, lo, hi, c, s, 0, b);
            }
            else Box(l, key, f, mx, Math.Min(ya, yb) - 0.5f * th, mz, len, Math.Abs(dy) + th, Math.Abs(dz) + 0.02f, m, g, lo, hi, c, s);
        }

        static void Strake(StructureLayout l, ulong key, in Hull h, in Frame f, int side, float j0, float j1, float x0, float x1, float th, int lo, int hi)
        {
            h.Point(x0, side, j0, 0, out float za0, out float ya0);
            h.Point(x0, side, j1, 0, out float zb0, out float yb0);
            h.Point(x1, side, j0, 0, out float za1, out float ya1);
            h.Point(x1, side, j1, 0, out float zb1, out float yb1);
            HullSegment(l, key, f, x0, x1, za0, ya0, zb0, yb0, za1, ya1, zb1, yb1, th, Plank, GX, lo, hi);
        }

        static void Rib(StructureLayout l, ulong key, in Hull h, in Frame f, float x, int side, float j0, float j1, float w, float stub, int lo, int hi)
        {
            float x0 = x - 0.5f * w, x1 = x + 0.5f * w, ins = 0.08f;
            for (int j = (int)j0; j < (int)Math.Ceiling(j1); j++)
            {
                float a = Math.Max(j0, j), b = Math.Min(j1, j + 1);
                h.Point(x, side, a, ins, out float za, out float ya);
                h.Point(x, side, b, ins, out float zb, out float yb);
                if (b >= HullZ.Length - 1 && stub > 0)
                {
                    float ex = zb - za, ey = yb - ya, el = (float)Math.Sqrt(ex * ex + ey * ey);
                    zb += ex / el * stub; yb += ey / el * stub;
                }
                HullSegment(l, key, f, x0, x1, za, ya, zb, yb, za, ya, zb, yb, w, Timber, GV, lo, hi);
            }
        }

        static void Keel(StructureLayout l, ulong key, in Hull h, in Frame f, int pieces, float w, int lo, int hi)
        {
            float half = 0.5f * h.Len * 0.94f;
            for (int k = 0; k < pieces; k++)
            {
                float x0 = -half + 2 * half * k / pieces, x1 = -half + 2 * half * (k + 1) / pieces, xm = 0.5f * (x0 + x1);
                h.Point(xm, 1, 0, 0, out float z, out float y);
                Box(l, key, f, xm, y - 0.8f * w, z, x1 - x0 + 0.04f, w, w, Timber, GX, lo, hi);
            }
            for (int e = -1; e <= 1; e += 2)
            {
                h.Point(e * half, 1, 0, 0, out float z0, out float y0);
                h.Point(e * half, -1, 4, 0, out float zt, out float yt);
                h.Point(e * half, 1, 4, 0, out float zu, out float yu);
                float top = 0.5f * (yt + yu) + 0.2f, ztop = 0.5f * (zt + zu), dy = top - y0;
                Box(l, key, f, e * (half + 0.15f), y0 - 0.5f * w, 0.5f * (z0 + ztop), w, dy + 0.5f * w, w, Timber, GV, lo, hi, 1, 0, e * 0.45f / dy, (ztop - z0) / dy);
            }
        }

        static void Shipwreck(StructureLayout l, ulong key, in Frame root)
        {
            var rng = Rng.Stream(key, "shipwreck");
            int d0 = (rng.Range(-1, 2) + 16) & 15;
            var f = root.Child(0, 0, 0, Cos16(d0), Sin16(d0));
            var h = new Hull(rng.Range(9f, 12.5f), rng.Range(2.8f, 3.7f), rng.Range(1.3f, 1.8f), rng.Range(0.4f, 0.8f), rng.Range(0.35f, 0.75f));
            float half = 0.5f * h.Len * 0.94f;
            int segs = 6, start = l.Pieces.Count;

            Keel(l, rng.NextU64(), h, f, 4, 0.26f, 0, 2);
            int breach = rng.Range(1, segs - 2), wide = rng.Range(2, 4), sprung = rng.Range(0, segs);
            float jLow = rng.NextFloat() < 0.5f ? 1 : 2;
            for (int side = -1; side <= 1; side += 2)
                for (int j = 0; j < 4; j++)
                    for (int k = 0; k < segs; k++)
                    {
                        bool open = side < 0 && j >= jLow && k >= breach && k < breach + wide;
                        bool gone = side > 0 && j == 3 && k == sprung;
                        if (open || gone) continue;
                        float x0 = -half + 2 * half * k / segs, x1 = -half + 2 * half * (k + 1) / segs, sag = side < 0 && j == 3 ? rng.Range(-0.08f, 0f) : 0;
                        Strake(l, rng.NextU64(), h, f, side, j, j + 1 + sag, x0, x1, 0.07f, 0, 1);
                    }
            for (int side = -1; side <= 1; side += 2)
                for (int b = 0; b < 2; b++)
                    for (int k = 0; k < 2; k++)
                    {
                        float x0 = -half + half * k, x1 = x0 + half;
                        Strake(l, rng.NextU64(), h, f, side, 2 * b, 2 * b + 2, x0, x1, 0.12f, 2, 2);
                    }

            int ribs = rng.Range(9, 12);
            for (int r = 0; r < ribs; r++)
            {
                float x = -half * 0.85f + 1.7f * half * (r + 0.5f) / ribs;
                for (int side = -1; side <= 1; side += 2)
                {
                    float stub = side < 0 && rng.NextFloat() < 0.6f ? rng.Range(0.2f, 0.7f) : 0, top = 4;
                    if (side < 0 && stub == 0 && rng.NextFloat() < 0.35f) top = rng.Range(2.6f, 3.6f);
                    Rib(l, rng.NextU64(), h, f, x, side, 0, top, 0.13f, stub, 0, 1);
                }
            }

            float mx = rng.Range(-0.1f, 0.15f) * h.Len;
            h.Point(mx, 1, 0, 0, out float mz0, out float my0);
            float stubLen = rng.Range(1.2f, 2.6f), msy = stubLen * h.C, md = rng.Range(0.26f, 0.32f);
            int before = l.Pieces.Count;
            Round(l, rng.NextU64(), f, mx, my0, mz0 + 0.5f * msy * h.S / h.C, md, msy, md, Timber, GV, RoundStem, 0, 1);
            if (l.Pieces.Count > before)
            {
                var p = l.Pieces[before];
                p.B = h.S / h.C;
                l.Pieces[before] = p;
            }
            Col(l, f, mx, mz0, 0.3f, 0, my0 + msy);

            for (float x = -half + 0.6f; x < half - 0.3f; x += 1.2f)
            {
                h.Point(x, -1, 4, 0, out float zt, out float yt);
                h.Point(x, 1, 2, 0, out float zl, out float _);
                float zc = 0.5f * (zt + zl), r = Math.Max(0.4f, 0.4f * h.Beam * h.Spread(x));
                Col(l, f, x, zc, r, -ShipwreckBuryM, yt);
                Touch(l, f, x, 0, zc, r + 0.3f);
            }
            Mossy(l, start);

            int detail = l.Pieces.Count;
            int fd = rng.Range(2, 7);
            float fr = rng.Range(2.2f, 2.8f), flen = rng.Range(4f, 6f), fx = Cos16(fd) * fr, fz = Sin16(fd) * fr + 0.6f * h.Beam;
            int fq = (fd + 4 + rng.Range(-2, 3)) & 15;
            float fc = Cos16(fq), fs = Sin16(fq);
            Log(l, rng.NextU64(), f, fx, -0.1f, fz, flen, md, fc, fs, Timber, 0, 1);
            for (int t = -1; t <= 1; t++) Touch(l, f, fx + fc * t * 0.4f * flen, 0, fz + fs * t * 0.4f * flen, 0.45f);
            Col(l, f, fx, fz, 0.35f, -0.1f, md - 0.1f);
            int ropes = rng.Range(2, 4);
            for (int k = 0; k < ropes; k++)
            {
                float a = rng.Range(-0.45f, 0.45f) * flen, rx = fx + fc * a, rz = fz + fs * a, rl = rng.Range(1.2f, 2.4f);
                int rq = rng.Range(0, 16);
                Log(l, rng.NextU64(), f, rx + Cos16(rq) * 0.5f * rl, -0.01f, rz + Sin16(rq) * 0.5f * rl, rl, 0.05f, Cos16(rq), Sin16(rq), Cloth, 0, 0);
            }

            int drape = l.Pieces.Count;
            if (rng.NextFloat() < 0.75f)
            {
                float sa = rng.Range(-0.3f, 0.3f) * flen, sx = fx + fc * sa - fs * 1.1f, sz = fz + fs * sa + fc * 1.1f;
                Box(l, rng.NextU64(), f, sx, -0.02f, sz, rng.Range(1.8f, 2.6f), 0.05f, rng.Range(1f, 1.5f), Cloth, GX, 0, 1, fc, fs, 0, 0, 0.1f);
                Touch(l, f, sx, 0, sz, 1f);
            }
            int boards = rng.Range(3, 7);
            for (int k = 0; k < boards; k++)
            {
                int q = rng.Range(0, 16);
                float bx = rng.Range(-0.4f, 0.4f) * h.Len, bz = rng.Range(-0.6f, 1.2f) * h.Beam;
                if (Math.Abs(bz) < 0.75f * h.Beam) bz = (bz < 0 ? -1 : 1) * 0.75f * h.Beam;
                float bl = rng.Range(1.2f, 2.6f);
                Box(l, rng.NextU64(), f, bx, -0.03f, bz, bl, 0.06f, rng.Range(0.18f, 0.26f), Plank, GX, 0, 0, Cos16(q), Sin16(q));
                Touch(l, f, bx, 0, bz, 0.5f * bl);
            }
            Flat(l, drape);
            Drape(l, drape);

            int cargo = rng.Range(3, 7);
            for (int k = 0; k < cargo; k++)
            {
                int d = rng.Range(0, 16), q = rng.Range(0, 16);
                float r = rng.Range(1.2f, 2.8f), cx = Cos16(d) * r * 1.6f, cz = Sin16(d) * r + (Sin16(d) >= 0 ? 0.7f * h.Beam : -0.45f * h.Beam - 0.3f), bury = rng.Range(0.08f, 0.3f);
                cx = Math.Max(-6.5f, Math.Min(6.5f, cx));
                var cf = f.Child(cx, -bury, cz, Cos16(q), Sin16(q), 0, 1);
                if (rng.NextFloat() < 0.5f) Barrel(l, rng.NextU64(), cf);
                else Crate(l, rng.NextU64(), cf);
                Touch(l, f, cx, 0, cz, 0.45f);
            }
            Mossy(l, detail);
        }
    }

    public sealed partial class ObjectField
    {
        public const float ShipwreckSeaM = 45f, ShipwreckStepM = 5f, ShipwreckProbeM = 8f, ShipwreckReliefM = 1.2f, ShipwreckMinY = 0.6f, ShipwreckMaxY = 3f, ShipwreckRiverM = 20f, ShipwreckSeekM = 25f, ShipwreckSeekMaxM = 1000f;
        static readonly float[] ShipwreckBack = { 12f, 20f, 28f };

        float SeaReach(float x, float z, float max, out float ux, out float uz)
        {
            ux = 0; uz = 1;
            for (float t = ShipwreckStepM; t <= max; t += ShipwreckStepM)
                for (int k = 0; k < 8; k++)
                {
                    FanDir(k, 0.70710678f, out float vx, out float vz);
                    float px = x + vx * t, pz = z + vz * t;
                    if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) continue;
                    if (w.Sea[Cell(pz) * n + Cell(px)]) { ux = vx; uz = vz; return t; }
                }
            return float.MaxValue;
        }

        public float ShipwreckFit(float x, float z)
        {
            if (x < ShipwreckSeaM || z < ShipwreckSeaM || x >= worldM - ShipwreckSeaM || z >= worldM - ShipwreckSeaM) return float.MinValue;
            int i = Cell(z) * n + Cell(x);
            if (w.Sea[i] || w.Water.LakeId[i] >= 0 || RiverGap(x, z) < ShipwreckRiverM) return float.MinValue;
            float y = GroundAt(detail, cm, x, z), relief = 0;
            if (y < ShipwreckMinY || y > ShipwreckMaxY) return float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, 0.70710678f, out float ux, out float uz);
                relief = Math.Max(relief, Math.Abs(GroundAt(detail, cm, x + ux * ShipwreckProbeM, z + uz * ShipwreckProbeM) - y));
                if (relief > ShipwreckReliefM) return float.MinValue;
            }
            float sea = SeaReach(x, z, ShipwreckSeaM, out _, out _);
            if (sea == float.MaxValue) return float.MinValue;
            return -relief - 0.02f * sea - 0.1f * y;
        }

        bool SeekShore(ref float x, ref float z, float x0, float z0, float x1, float z1, out float qa, out float qb)
        {
            float best = float.MinValue, bx = x, bz = z;
            qa = 1; qb = 0;
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, 0.70710678f, out float ux, out float uz);
                for (float t = 0; t <= ShipwreckSeekMaxM; t += ShipwreckSeekM)
                {
                    float px = x + ux * t, pz = z + uz * t;
                    if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) break;
                    if (!w.Sea[Cell(pz) * n + Cell(px)]) continue;
                    for (int b = 0; b < ShipwreckBack.Length; b++)
                    {
                        float cx = Math.Min(x1, Math.Max(x0, px - ux * ShipwreckBack[b])), cz = Math.Min(z1, Math.Max(z0, pz - uz * ShipwreckBack[b]));
                        float fit = ShipwreckFit(cx, cz);
                        if (fit > best) { best = fit; bx = cx; bz = cz; }
                    }
                    break;
                }
            }
            if (best == float.MinValue) return false;
            x = bx; z = bz;
            SeaReach(x, z, ShipwreckSeaM, out float vx, out float vz);
            qa = (float)Math.Sqrt(Math.Max(0, 0.5f * (1 + vz)));
            qb = (vx < 0 ? -1 : 1) * (float)Math.Sqrt(Math.Max(0, 0.5f * (1 - vz)));
            return true;
        }

        public string ShipwreckFault(ObjectInstance o)
        {
            if (ShipwreckFit(o.X, o.Z) == float.MinValue) return "is not on a sea shore above the tide line";
            SeatRange(detail, cm, Seats(lib.Objects[o.Def]), o, out float lo, out float hi);
            if (hi - lo > Structures.SeatM) return "lies on uneven ground";
            foreach (var t in TownPlans())
            {
                float dx = t.X - o.X, dz = t.Z - o.Z;
                if (Math.Sqrt(dx * dx + dz * dz) - t.Radius < TownClearM) return "lies within 400 m of a town";
            }
            float r = SiteRadius(o);
            foreach (var f in edits)
                if ((f.Shape == FeatureEdit.Cave || f.Shape == FeatureEdit.CaveRoom || f.Shape == FeatureEdit.Sink || f.Shape == FeatureEdit.Shaft || f.Shape == FeatureEdit.Mine) && o.X + r >= f.MinX && o.X - r <= f.MaxX && o.Z + r >= f.MinZ && o.Z - r <= f.MaxZ) return "lies over a cave";
            return null;
        }
    }
}
