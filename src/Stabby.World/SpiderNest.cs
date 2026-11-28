using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float SpiderU = 1 / 18f, SpiderFootM = 6f;
        static readonly float[] BundleW = { 0.6f, 1f, 0.9f, 0.55f };
        static readonly float[] SpiderSeats = { 0, 0, 0.2f, 0, -0.2f, 0, 0, 0.2f, 0, -0.2f, 0.14f, 0.14f, -0.14f, 0.14f, 0.14f, -0.14f, -0.14f, -0.14f };

        internal sealed class SpiderFrame
        {
            public int Trunks, Rocks;
            public float[] Ta, Tx, Tz, Girth, Top, Lean, Drift, Bx, By, Bz, Lx, Ly, Lz, RootA, RootL, Rx, Rz, Rw, Rt, Rdep, Tilt;
            public int[] Roots, Rd;
        }

        internal static SpiderFrame SpiderBones(ulong key)
        {
            var rng = Rng.Stream(key, "spider-nest");
            var s = new SpiderFrame();
            int n = s.Trunks = rng.Range(3, 5);
            s.Ta = new float[n]; s.Tx = new float[n]; s.Tz = new float[n]; s.Girth = new float[n]; s.Top = new float[n]; s.Lean = new float[n]; s.Drift = new float[n];
            s.Bx = new float[n]; s.By = new float[n]; s.Bz = new float[n]; s.Lx = new float[n]; s.Ly = new float[n]; s.Lz = new float[n];
            s.Roots = new int[n]; s.RootA = new float[n * 3]; s.RootL = new float[n * 3];
            float spin = rng.Range(0, 6.2832f);
            for (int k = 0; k < n; k++)
            {
                float a = spin + 6.2832f * (k + rng.Range(-0.12f, 0.12f)) / n, r = rng.Range(4.2f, 5.6f);
                s.Ta[k] = a; s.Tx[k] = r * TurnCos(a); s.Tz[k] = r * TurnSin(a);
                s.Girth[k] = rng.Range(0.75f, 1f);
                s.Top[k] = rng.Range(7.5f, 12f);
                s.Drift[k] = rng.Range(0, 6.2832f); s.Lean[k] = rng.Range(0.01f, 0.04f);
                s.Roots[k] = rng.Range(2, 4);
                for (int j = 0; j < s.Roots[k]; j++) { s.RootA[k * 3 + j] = a + rng.Range(-1.2f, 1.2f); s.RootL[k * 3 + j] = rng.Range(1.2f, 2f); }
                float h = rng.Range(3.8f, 5.8f), reach = rng.Range(2f, 3.2f), b = a + 3.1416f + rng.Range(-0.4f, 0.4f);
                s.Bx[k] = s.Tx[k] + 0.2f * s.Girth[k] * TurnCos(b); s.Bz[k] = s.Tz[k] + 0.2f * s.Girth[k] * TurnSin(b); s.By[k] = h;
                s.Lx[k] = s.Bx[k] + reach * TurnCos(b); s.Lz[k] = s.Bz[k] + reach * TurnSin(b); s.Ly[k] = h + rng.Range(0.3f, 0.9f);
            }
            int m = s.Rocks = rng.Range(2, 4);
            s.Rx = new float[m]; s.Rz = new float[m]; s.Rw = new float[m]; s.Rt = new float[m]; s.Rdep = new float[m]; s.Tilt = new float[m]; s.Rd = new int[m];
            for (int k = 0; k < m; k++)
            {
                float a = spin + 6.2832f * (k + 0.5f + rng.Range(-0.15f, 0.15f)) / m, r = rng.Range(2.6f, 4.2f), w = rng.Range(1.3f, 2.2f);
                s.Rd[k] = rng.Range(0, 16);
                s.Rx[k] = r * TurnCos(a); s.Rz[k] = r * TurnSin(a); s.Rw[k] = w;
                s.Rt[k] = w * rng.Range(0.55f, 0.75f); s.Rdep[k] = w * rng.Range(0.7f, 0.9f); s.Tilt[k] = rng.Range(-0.1f, 0.1f);
            }
            return s;
        }

        static void SpiderNest(StructureLayout l, ulong key, in Frame f)
        {
            var sp = SpiderBones(key);
            var rng = Rng.Stream(key, "spider-nest-parts");
            int trunks = sp.Trunks, rocks = sp.Rocks;
            float[] tx = sp.Tx, tz = sp.Tz, top = sp.Top, girth = sp.Girth, lx = sp.Lx, ly = sp.Ly, lz = sp.Lz, rx = sp.Rx, rz = sp.Rz, rt = sp.Rt;
            var feet = new List<float>();

            int bark = l.Pieces.Count;
            for (int k = 0; k < trunks; k++)
            {
                float mid = 0.45f * top[k], drift = sp.Drift[k], lean = sp.Lean[k];
                float mx = tx[k] + lean * mid * TurnCos(drift), mz = tz[k] + lean * mid * TurnSin(drift);
                float ex = tx[k] + lean * top[k] * TurnCos(drift), ez = tz[k] + lean * top[k] * TurnSin(drift);
                Bone(l, rng.NextU64(), f, tx[k], -0.4f, tz[k], mx, mid, mz, girth[k] * 1.15f, girth[k] * 0.8f, 0, 2, Timber);
                Bone(l, rng.NextU64(), f, mx, mid, mz, ex, top[k], ez, girth[k] * 0.8f, girth[k] * 0.5f, 0, 2, Timber);
                float sa = rng.Range(0, 6.2832f);
                Bone(l, rng.NextU64(), f, ex, top[k] - 0.3f, ez, ex + 0.35f * TurnCos(sa), top[k] + rng.Range(0.8f, 1.6f), ez + 0.35f * TurnSin(sa), 0.3f * girth[k], 0.1f, 0, 0, Timber);
                var foot = f.Child(tx[k], 0, tz[k]);
                for (int j = 0; j < sp.Roots[k]; j++)
                    AncientRoot(l, ref rng, foot, sp.RootA[k * 3 + j], 0.35f * girth[k], 0.15f, 0.35f * girth[k], sp.RootL[k * 3 + j], 2, feet);
                Bone(l, rng.NextU64(), f, sp.Bx[k], sp.By[k], sp.Bz[k], lx[k], ly[k], lz[k], 0.32f, 0.14f, 0, 1, Timber);
            }
            Mossy(l, bark);

            int stones = l.Pieces.Count;
            for (int k = 0; k < rocks; k++)
                Rock(l, rng.NextU64(), f, rx[k], -0.15f, rz[k], sp.Rw[k], rt[k] + 0.15f, sp.Rdep[k], Cos16(sp.Rd[k]), Sin16(sp.Rd[k]), 0, 2, sp.Tilt[k]);
            Mossy(l, stones);

            int silk = l.Pieces.Count;
            int hangs = rng.Range(2, 5);
            for (int k = 0; k < hangs; k++)
            {
                int t = k % trunks;
                float along = rng.Range(0.55f, 0.95f), bx = tx[t] + along * (lx[t] - tx[t]), bz = tz[t] + along * (lz[t] - tz[t]), by = ly[t] - (1 - along) * 0.6f;
                float w = rng.Range(0.3f, 0.42f), y = by - rng.Range(1.1f, 2.2f) - 2.4f * w;
                float cap = Bundle(l, ref rng, f, bx, y, bz, w);
                Bone(l, rng.NextU64(), f, bx, cap - 0.1f, bz, bx + rng.Range(-0.05f, 0.05f), by, bz + rng.Range(-0.05f, 0.05f), 0.025f, 0.02f, 0, 0, Silk);
            }

            int lying = rng.Range(2, 4);
            for (int k = 0; k < lying; k++)
            {
                int t = (k + 1) % trunks, d = rng.Range(0, 16);
                float a = rng.Range(0, 6.2832f), r = 0.5f * girth[t] + rng.Range(0.6f, 1.3f), x = tx[t] + r * TurnCos(a), z = tz[t] + r * TurnSin(a), w = rng.Range(0.3f, 0.42f);
                Round(l, rng.NextU64(), f, x, -0.05f, z, rng.Range(0.6f, 0.9f), w, w, Silk, GX, RoundLog, 0, 0, Cos16(d), Sin16(d));
                Touch(l, f, x, 0, z, 0.5f);
            }

            {
                int t = rng.Range(0, trunks);
                Toward(tx[t], tz[t], 0, out float c, out float s);
                float r = 0.5f * girth[t] + 1.3f, twist = 1.5708f + rng.Range(-0.3f, 0.3f);
                Toward(tx[t], tz[t], twist, out float ac, out float asn);
                Toward(tx[t], tz[t], twist + 0.5f, out float hc, out float hs);
                float bx = tx[t] + r * c, bz = tz[t] + r * s;
                Round(l, rng.NextU64(), f, bx, -0.12f, bz, rng.Range(1.6f, 1.9f), rng.Range(0.8f, 0.9f), rng.Range(0.75f, 0.85f), Silk, GX, RoundLog, 0, 1, ac, asn);
                float hx = bx + 1f * ac, hz = bz + 1f * asn;
                Round(l, rng.NextU64(), f, hx, 0.2f, hz, 0.7f, 0.42f, 0.4f, Silk, GX, RoundLog, 0, 0, hc, hs);
                int legs = rng.Range(2, 4);
                for (int j = 0; j < legs; j++)
                {
                    float o = -0.55f + 0.5f * j + rng.Range(-0.1f, 0.1f), px = bx + o * ac, pz = bz + o * asn;
                    float qx = px + 0.9f * c + rng.Range(-0.2f, 0.2f) * ac, qz = pz + 0.9f * s + rng.Range(-0.2f, 0.2f) * asn;
                    Bone(l, rng.NextU64(), f, qx, 0.05f, qz, px + 0.2f * c, 0.4f, pz + 0.2f * s, 0.13f, 0.17f, 0, 0, Silk);
                }
                int strands = rng.Range(2, 4);
                for (int j = 0; j < strands; j++)
                {
                    float o = rng.Range(-0.6f, 0.6f), sr = 0.5f * girth[t];
                    Toward(tx[t], tz[t], 3.1416f + rng.Range(-0.5f, 0.5f), out float sc, out float ss);
                    Bone(l, rng.NextU64(), f, bx + o * ac, 0.7f, bz + o * asn, tx[t] + sr * sc, rng.Range(1.8f, 3.2f), tz[t] + sr * ss, 0.035f, 0.025f, 0, 0, Silk);
                }
                Touch(l, f, bx, 0, bz, 1.2f);
            }

            Flat(l, silk);

            int bones = l.Pieces.Count, longs = rng.Range(4, 8);
            for (int i = 0; i < longs; i++)
            {
                int d = rng.Range(0, 16);
                float a = rng.Range(0, 6.2832f), r = rng.Range(0.5f, 4f), x = r * TurnCos(a), z = r * TurnSin(a), d0 = rng.Range(0.05f, 0.09f);
                Log(l, rng.NextU64(), f, x, -0.4f * d0, z, rng.Range(0.3f, 0.65f), d0, Cos16(d), Sin16(d), Plaster, 0, 0);
            }
            {
                int rd = rng.Range(0, 16), ribs = rng.Range(3, 6);
                float a = rng.Range(0, 6.2832f), r = rng.Range(1f, 3f), x0 = r * TurnCos(a), z0 = r * TurnSin(a);
                for (int i = 0; i < ribs; i++)
                {
                    float t = (i - 0.5f * (ribs - 1)) * 0.11f;
                    Log(l, rng.NextU64(), f, x0 - Sin16(rd) * t, -0.01f, z0 + Cos16(rd) * t, rng.Range(0.35f, 0.5f), 0.03f, Cos16(rd + rng.Range(-1, 2)), Sin16(rd + rng.Range(-1, 2)), Plaster, 0, 0);
                }
                int sd = rng.Range(0, 16);
                float sx = x0 + rng.Range(-0.8f, 0.8f), sz = z0 + rng.Range(-0.8f, 0.8f);
                Round(l, rng.NextU64(), f, sx, -0.03f, sz, 0.22f, 0.13f, 0.15f, Plaster, GZ, RoundLog, 0, 0, Cos16(sd), Sin16(sd));
            }
            int husks = rng.Range(5, 9);
            for (int i = 0; i < husks; i++)
            {
                int d = rng.Range(0, 16);
                float a = rng.Range(0, 6.2832f), r = rng.Range(0.3f, 4.5f), w = rng.Range(0.12f, 0.22f);
                Round(l, rng.NextU64(), f, r * TurnCos(a), -0.15f * w, r * TurnSin(a), w * 1.4f, w * 0.55f, w, Timber, GX, RoundLog, 0, 0, Cos16(d), Sin16(d));
            }
            Drape(l, bones);

            int fallen = l.Pieces.Count, falls = rng.Range(3, 5);
            for (int i = 0; i < falls; i++)
            {
                int d = rng.Range(0, 16), e = (d + (rng.NextFloat() < 0.5f ? 2 : 14)) & 15;
                float a = rng.Range(0, 6.2832f), r = rng.Range(5.8f, 7f), x = r * TurnCos(a), z = r * TurnSin(a), d0 = rng.Range(0.06f, 0.11f);
                Log(l, rng.NextU64(), f, x, -0.3f * d0, z, rng.Range(1.4f, 2.4f), d0, Cos16(d), Sin16(d), Timber, 0, 1);
                Log(l, rng.NextU64(), f, x + rng.Range(-0.4f, 0.4f), -0.2f * d0, z + rng.Range(-0.4f, 0.4f), rng.Range(0.5f, 1f), 0.5f * d0, Cos16(e), Sin16(e), Timber, 0, 0);
                Touch(l, f, x, 0, z, 0.6f);
            }
            Mossy(l, fallen);

            for (int k = 0; k < trunks; k++) Col(l, f, tx[k], tz[k], 0.5f * girth[k], 0, top[k]);
            for (int k = 0; k < rocks; k++) Col(l, f, rx[k], rz[k], 0.6f * rt[k] / 0.65f, 0, rt[k]);
            for (int k = 0; k < feet.Count; k += 6) Touch(l, f, feet[k], 0, feet[k + 2], 0.35f);
            for (int k = 0; k < trunks; k++) Touch(l, f, tx[k], 0, tz[k], girth[k] + 0.4f);
            Touch(l, f, 0, 0, 0, 2.4f);
            for (int k = 0; k < 8; k++)
            {
                Arc(k / 8f, out float c, out float s);
                Touch(l, f, 3f * c, 0, 3f * s, 1.5f);
            }
        }

        static float Bundle(StructureLayout l, ref Rng rng, in Frame f, float x, float y, float z, float w)
        {
            float cy = y;
            for (int k = 0; k < 4; k++)
            {
                int d = rng.Range(0, 16);
                float sw = w * BundleW[k] * rng.Range(0.85f, 1.1f);
                Round(l, rng.NextU64(), f, x + rng.Range(-0.05f, 0.05f), cy, z + rng.Range(-0.05f, 0.05f), sw * rng.Range(1.2f, 1.6f), sw, sw * rng.Range(0.8f, 1f), Silk, GV, RoundLog, 0, k == 1 || k == 2 ? 1 : 0, Cos16(d), Sin16(d));
                if (k == 1 && rng.NextFloat() < 0.6f)
                {
                    float a = rng.Range(0, 6.2832f), mid = cy + 0.5f * sw;
                    Bone(l, rng.NextU64(), f, x, mid, z, x + 0.4f * TurnCos(a), mid - rng.Range(0.15f, 0.35f), z + 0.4f * TurnSin(a), 0.035f, 0.015f, 0, 0, Silk);
                }
                cy += 0.6f * sw;
            }
            return cy + 0.3f * w;
        }

        static void Toward(float x, float z, float turn, out float c, out float s)
        {
            float len = Math.Max((float)Math.Sqrt(x * x + z * z), 1e-4f), c0 = -x / len, s0 = -z / len, tc = TurnCos(turn), ts = TurnSin(turn);
            c = c0 * tc - s0 * ts;
            s = s0 * tc + c0 * ts;
        }
    }

    public sealed partial class ObjectField
    {
        public const float SpiderWoods = 1f, SpiderReliefM = 5f, SpiderHollow = 0.3f, SpiderFloatM = 0.5f, SpiderBuryM = 1f, SpiderSeekM = 60f;

        public string SpiderFault(ObjectInstance o)
        {
            float u = o.Size * Structures.SpiderU;
            float centre = GroundAt(detail, cm, o.X, o.Z) - o.Y;
            if (centre > SpiderBuryM * u) return $"hollow is buried {centre:F2} m";
            for (int k = 0; k < 12; k++)
            {
                float a = 6.2832f * k / 12;
                Rotate(o, Structures.SpiderFootM * u * Structures.TurnCos(a), 0, Structures.SpiderFootM * u * Structures.TurnSin(a), out float wx, out _, out float wz);
                float gap = o.Y - GroundAt(detail, cm, o.X + wx, o.Z + wz);
                if (gap > SpiderFloatM) return $"trunks float {gap:F2} m";
            }
            int cx = Math.Min(n - 1, (int)(o.X / cm + 0.5f)), cz = Math.Min(n - 1, (int)(o.Z / cm + 0.5f));
            if (w.Sea[cz * n + cx] || w.Water.LakeId[cz * n + cx] >= 0) return "lies in water";
            if (RiverGap(o.X, o.Z) <= 10f * u + 1) return "lies on a river bed";
            float sr = SiteRadius(o);
            foreach (var f in edits)
                if ((f.Shape == FeatureEdit.Cave || f.Shape == FeatureEdit.CaveRoom || f.Shape == FeatureEdit.Sink || f.Shape == FeatureEdit.Shaft || f.Shape == FeatureEdit.Mine) && o.X + sr >= f.MinX && o.X - sr <= f.MaxX && o.Z + sr >= f.MinZ && o.Z - sr <= f.MaxZ) return "lies over a cave";
            return null;
        }
    }
}
