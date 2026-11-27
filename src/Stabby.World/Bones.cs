using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float BonesU = 1 / 19f;
        public static readonly float[] BonesSeat = { 0, 0 };
        static readonly float[] RibCos = { 0.97030f, 0.98481f, 0.86603f, 0.68200f, 0.46947f, 0.25882f };
        static readonly float[] RibSin = { -0.24192f, 0.17365f, 0.5f, 0.73135f, 0.88295f, 0.96593f };

        static void Bone(StructureLayout l, ulong key, in Frame f, float x0, float y0, float z0, float x1, float y1, float z1, float t0, float t1, int lo = 0, int hi = Tiers - 1)
        {
            float dx = x1 - x0, dy = y1 - y0, dz = z1 - z0, len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dy <= 0.01f || len <= 0) return;
            float ov = 0.3f * Math.Max(t0, t1) / len;
            x0 -= ov * dx; y0 -= ov * dy; z0 -= ov * dz; x1 += ov * dx; y1 += ov * dy; z1 += ov * dz;
            dy = y1 - y0;
            float ln = dx / dy, bt = dz / dy, t = 0.5f * (t0 + t1);
            int before = l.Pieces.Count;
            Box(l, key, f, 0.5f * (x0 + x1), y0, 0.5f * (z0 + z1), t * (float)Math.Sqrt(1 + ln * ln), dy, t * (float)Math.Sqrt(1 + bt * bt), Stone, GV, lo, hi, 1, 0, ln, bt);
            if (l.Pieces.Count == before) return;
            var p = l.Pieces[before];
            p.R = RoundStem;
            l.Pieces[before] = p;
        }

        static void Vertebra(StructureLayout l, ref Rng rng, in Frame f, float x, float y, float z, float w, float process, int lo = 0)
        {
            int k = rng.Range(0, 16);
            Hewn(l, rng.NextU64(), f, x, y, z, w * rng.Range(1.05f, 1.3f), w * rng.Range(0.75f, 0.9f), w * rng.Range(0.8f, 0.95f), Cos16(k) * 0.15f + 0.85f, Sin16(k) * 0.15f, lo, Tiers - 1, rng.Range(0.3f, 0.45f));
            if (process > 0) Bone(l, rng.NextU64(), f, x, y + 0.85f * w, z, x + rng.Range(-0.08f, 0.08f), y + 0.85f * w + process, z - 0.35f * process, 0.3f * w, 0.18f * w, 0, 1);
        }

        static void Bones(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "bones");
            int pairs = rng.Range(3, 6);
            float gap = rng.Range(2.3f, 2.8f), tall = rng.Range(9f, 10.5f), wide = rng.Range(0.5f, 0.6f), back = -0.5f * gap * (pairs - 1) - 1.5f;
            float neck = rng.Range(5.5f, 6.5f), tail = rng.Range(8f, 10f), shift = 0.5f * (tail - neck) - 1.5f;
            var top = new float[pairs];
            var zs = new float[pairs];
            float mid = 0.5f * (pairs - 1);
            for (int i = 0; i < pairs; i++)
            {
                float a = mid > 0 ? (i - mid) / mid : 0;
                float h = tall * (1 - 0.32f * a * a) * rng.Range(0.96f, 1.04f), t = rng.Range(0.8f, 1f), bow = rng.Range(0.42f, 0.5f) / h;
                float z = back + shift + i * gap + 1.5f;
                zs[i] = z;
                top[i] = h * RibSin[RibSin.Length - 1];
                float bar0 = -0.5f, bar1 = 0.5f;
                for (int side = -1; side <= 1; side += 2)
                {
                    float w = h * wide * rng.Range(0.95f, 1.05f), sweep = rng.Range(-0.03f, 0.03f);
                    int segs = rng.NextFloat() < 0.12f ? rng.Range(2, 4) : RibCos.Length - 1;
                    for (int j = 0; j < segs; j++)
                    {
                        float ta = t * (1 - 0.6f * j / (RibCos.Length - 1)), tb = t * (1 - 0.6f * (j + 1) / (RibCos.Length - 1));
                        float ya = h * RibSin[j], yb = h * RibSin[j + 1];
                        Bone(l, rng.NextU64(), f, side * w * RibCos[j], ya, z + sweep * ya - bow * ya * ya, side * w * RibCos[j + 1], yb, z + sweep * yb - bow * yb * yb, ta, tb);
                    }
                    Bone(l, rng.NextU64(), f, side * 0.98f * w, -0.6f, z, side * 0.5f, 0.5f, z, 0.75f * t, 0.6f * t);
                    if (segs == RibCos.Length - 1)
                    {
                        float tip = w * RibCos[RibCos.Length - 1] + 0.3f * t;
                        if (side < 0) bar0 = -tip; else bar1 = tip;
                    }
                    else
                    {
                        float ex = side * w * RibCos[segs], ey = h * RibSin[segs];
                        Hewn(l, rng.NextU64(), f, ex, ey - 0.25f * t, z + sweep * ey - bow * ey * ey, 0.6f * t, 0.45f * t, 0.6f * t, 1, 0, 0, 1, 0.35f);
                    }
                    float gx = side * w * RibCos[1] * 1.005f;
                    Touch(l, f, gx, 0, z, 0.7f + t);
                    Touch(l, f, side * 0.5f * w, 0, z, 0.5f + t);
                    Col(l, f, gx, z, 0.6f, -1f, 2.6f);
                }
                int before = l.Pieces.Count;
                Box(l, rng.NextU64(), f, 0.5f * (bar0 + bar1), top[i] - 0.45f * t, z - bow * top[i] * top[i], bar1 - bar0, 0.5f * t, 0.5f * t, Stone, GX, 0, Tiers - 1);
                if (l.Pieces.Count > before) { var p = l.Pieces[before]; p.R = RoundLog; l.Pieces[before] = p; }
            }

            float z0 = zs[0], z1 = zs[pairs - 1];
            int spine = (int)Math.Ceiling((z1 - z0) / 0.9f);
            for (int k = 0; k <= spine; k++)
            {
                float z = z0 + (z1 - z0) * k / Math.Max(1, spine), vw = rng.Range(1.1f, 1.3f);
                Vertebra(l, ref rng, f, rng.Range(-0.05f, 0.05f), -0.4f * vw, z, vw, (k & 1) == 0 ? rng.Range(0.35f, 0.6f) : 0);
                Touch(l, f, 0, 0, z, 0.5f + 0.6f * vw);
                Col(l, f, 0, z, 0.5f * vw, -0.5f, 0.45f * vw);
            }

            float sx = rng.Range(-1.6f, 1.6f);
            for (int k = 1; k <= 5; k++)
            {
                float s = k / 5f, x = sx * s * s, z = z1 + neck * s, vw = 1.2f - 0.2f * s;
                Vertebra(l, ref rng, f, x, -0.4f * vw, z, vw, 0);
                Touch(l, f, x, 0, z, 0.5f + 0.6f * vw);
                Col(l, f, x, z, 0.5f * vw, -0.5f, 0.45f * vw);
            }

            float curl = rng.Range(-2.5f, 2.5f);
            for (int k = 1; k <= 7; k++)
            {
                float s = k / 7f, x = curl * s * s, z = z0 - tail * s, vw = 1.1f - 0.5f * s;
                Vertebra(l, ref rng, f, x, -0.4f * vw, z, vw, 0);
                Touch(l, f, x, 0, z, 0.4f + 0.6f * vw);
                Col(l, f, x, z, 0.5f * vw, -0.5f, 0.4f * vw);
            }

            const float g = 2.2f;
            float sz = z1 + neck + 1.1f * g;
            Yaw(ref rng, 0.4f, out float yc, out float ys);
            var skull = f.Child(sx, 0, sz, yc, ys);
            int head = l.Pieces.Count;
            float cw = g * rng.Range(1.8f, 2.1f), ch = g * rng.Range(1.6f, 1.9f), cl = g * rng.Range(1.9f, 2.2f), snout = g * rng.Range(1.4f, 1.8f);
            Hewn(l, rng.NextU64(), skull, 0, -0.8f * g, 0, cw, ch + 0.3f * g, cl, 1, 0, 0, Tiers - 1, rng.Range(0.3f, 0.4f), 0, rng.Range(-0.1f, 0.05f), 0.08f);
            Hewn(l, rng.NextU64(), skull, rng.Range(-0.1f, 0.1f) * g, -0.75f * g, 0.5f * cl + 0.45f * snout, 0.65f * cw, 0.6f * ch + 0.3f * g, snout, 1, 0, 0, Tiers - 1, rng.Range(0.35f, 0.45f), 0, -0.1f, 0.12f);
            for (int side = -1; side <= 1; side += 2)
            {
                Hewn(l, rng.NextU64(), skull, side * 0.32f * cw, ch - 0.75f * g, 0.25f * cl, 0.45f * cw, 0.35f * g, 0.5f * cl, 1, 0, 0, 1, 0.35f);
                float hx = side * 0.38f * cw, hy = ch - 0.7f * g, hl = g * rng.Range(1.2f, 2f);
                Bone(l, rng.NextU64(), skull, hx, hy, -0.2f * cl, hx + side * 0.7f * hl, hy + 0.55f * hl, -0.2f * cl - 0.6f * hl, 0.38f * g, 0.14f * g, 0, 1);
                for (int k = 0; k < 3; k++)
                {
                    float tz = 0.5f * cl + snout * (0.25f + 0.28f * k), tx = side * 0.28f * cw;
                    Bone(l, rng.NextU64(), skull, tx, -0.75f * g, tz, tx + side * 0.03f * g, g * rng.Range(0.5f, 0.7f), tz + 0.04f * g, 0.14f * g, 0.07f * g, 0, 0);
                }
            }
            Touch(l, skull, 0, 0, 0, 0.5f * (cw + cl));
            Touch(l, skull, 0, 0, 0.5f * cl + 0.45f * snout, 0.6f * snout);
            Col(l, skull, 0, 0, 0.5f * cw, -0.8f * g, ch - 0.5f * g);
            Col(l, skull, 0, 0.5f * cl + 0.45f * snout, 0.35f * cw, -0.75f * g, 0.6f * ch - 0.45f * g);

            float jside = rng.NextFloat() < 0.5f ? -1 : 1, jx = jside * g * rng.Range(1.4f, 1.9f), jz = g * rng.Range(0.6f, 1.2f);
            int jk = rng.Range(0, 16);
            var jaw = skull.Child(jx, 0, jz, Cos16(jk) * 0.2f + 0.8f, Sin16(jk) * 0.2f);
            float jl = cl + 0.8f * snout;
            Hewn(l, rng.NextU64(), jaw, 0, -0.7f * g, 0, 0.6f * g, 1.25f * g, jl, 1, 0, 0, Tiers - 1, 0.4f);
            for (int k = 0; k < 4; k++)
            {
                float tz = jl * (0.1f + 0.1f * k);
                Bone(l, rng.NextU64(), jaw, 0, 0.4f * g, tz, 0.02f * g, g * rng.Range(0.8f, 1f), tz + 0.05f * g, 0.15f * g, 0.07f * g, 0, 0);
            }
            Touch(l, jaw, 0, 0, 0, 0.5f * jl);
            Drape(l, head);

            int loose = l.Pieces.Count, scattered = rng.Range(3, 6);
            for (int k = 0; k < scattered; k++)
            {
                int d = (k * 5 + rng.Range(0, 3)) & 15;
                float r = rng.Range(5f, 9f), x = Cos16(d) * r * 0.8f, z = Sin16(d) * r + 0.5f * (back + z1);
                if (Math.Abs(x) < 4f) x = (x < 0 ? -1 : 1) * rng.Range(4.5f, 6f);
                float w = rng.Range(0.9f, 1.15f), h = rng.Range(1.3f, 1.5f);
                int q = rng.Range(0, 16);
                Hewn(l, rng.NextU64(), f, x, -0.75f, z, w * rng.Range(1.05f, 1.25f), h, w * rng.Range(0.8f, 0.95f), Cos16(q) * 0.15f + 0.85f, Sin16(q) * 0.15f, 0, Tiers - 1, rng.Range(0.3f, 0.45f));
                if (rng.NextFloat() < 0.5f) Bone(l, rng.NextU64(), f, x, h - 0.95f, z, x + rng.Range(-0.3f, 0.3f), h - 0.95f + rng.Range(0.4f, 0.7f), z + rng.Range(-0.3f, 0.3f), 0.3f * w, 0.16f * w, 0, 1);
                Touch(l, f, x, 0, z, 0.9f * w);
                Col(l, f, x, z, 0.5f * w, -0.75f, h - 0.75f);
            }
            int shards = rng.Range(1, 3);
            for (int k = 0; k < shards; k++)
            {
                float side = rng.NextFloat() < 0.5f ? -1 : 1, rx = side * rng.Range(6f, 8f), rz = rng.Range(z0 - 2f, z1 + 2f), reach = rng.Range(1.6f, 2.6f), rise = rng.Range(1.2f, 2f);
                int rk = rng.Range(0, 16);
                Bone(l, rng.NextU64(), f, rx, -1.3f, rz, rx + side * Cos16(rk) * 0.5f * reach, rise, rz + Sin16(rk) * reach, 0.42f, 0.28f);
                Touch(l, f, rx, 0, rz, 1f);
                Col(l, f, rx, rz, 0.35f, -1f, 1f);
            }
            Drape(l, loose);
        }
    }

    public sealed partial class ObjectField
    {
        public const float BonesProbeM = 16f, BonesReliefM = 1f, BonesRiverM = 40f, BonesFloatM = 0.15f, BonesBuryM = 0.1f;

        public float BonesFit(float x, float z)
        {
            float y = GroundAt(detail, cm, x, z), relief = 0;
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, 0.70710678f, out float ux, out float uz);
                float px = x + ux * BonesProbeM, pz = z + uz * BonesProbeM;
                if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) return float.MinValue;
                relief = Math.Max(relief, Math.Abs(GroundAt(detail, cm, px, pz) - y));
            }
            return relief <= BonesReliefM && RiverGap(x, z) >= BonesRiverM ? -relief : float.MinValue;
        }

        public string BonesFault(ObjectInstance o)
        {
            if (BonesFit(o.X, o.Z) == float.MinValue) return "stands on unsteady ground";
            var def = lib.Objects[o.Def];
            float foot = o.Y + def.Placement.Sink * o.Size;
            foreach (var p in Structures.Layout(ObjectPart.Bones, ObjectMeshes.Key(def.Id, 0, o.Variant)).Pieces)
            {
                if (p.Y - p.Base > Structures.GroundY) continue;
                Rotate(o, p.X * o.Size, 0, p.Z * o.Size, out float rx, out _, out float rz);
                float g = GroundAt(detail, cm, o.X + rx, o.Z + rz) - foot;
                if (g < p.Y * o.Size - BonesFloatM || g > (p.Y + p.Sy) * o.Size - BonesBuryM) return "has a bone floating or buried";
            }
            float r = SiteRadius(o);
            foreach (var f in edits)
                if ((f.Shape == FeatureEdit.Cave || f.Shape == FeatureEdit.CaveRoom || f.Shape == FeatureEdit.Sink || f.Shape == FeatureEdit.Shaft || f.Shape == FeatureEdit.Mine) && o.X + r >= f.MinX && o.X - r <= f.MaxX && o.Z + r >= f.MinZ && o.Z - r <= f.MaxZ) return "lies over a cave";
            return null;
        }
    }
}
