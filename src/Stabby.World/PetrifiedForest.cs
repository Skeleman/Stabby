using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float PetrifiedU = 1 / 20f;

        static readonly float[] PetrifiedSeats = { 0, 0, 0.2f, 0, -0.2f, 0, 0, 0.2f, 0, -0.2f, 0.14f, 0.14f, -0.14f, 0.14f, 0.14f, -0.14f, -0.14f, -0.14f, 0.36f, 0, -0.36f, 0, 0, 0.36f, 0, -0.36f };

        static void PetrifiedForest(StructureLayout l, ulong key, in Frame root)
        {
            var rng = Rng.Stream(key, "petrified-forest");
            int d0 = rng.Range(0, 16);
            var f = root.Child(0, 0, 0, Cos16(d0), Sin16(d0));
            int trunks = rng.Range(3, 7);
            float a0 = rng.NextFloat();
            var lens = new float[4];
            for (int t = 0; t < trunks; t++)
            {
                Arc(a0 + (t + rng.Range(-0.3f, 0.3f)) / trunks, out float ac, out float az);
                float rad = t == 0 ? rng.Range(0f, 2.5f) : rng.Range(5f, 11f), heading = rng.NextFloat(), d = rng.Range(0.7f, 1.25f), total = 0;
                int segs = rng.Range(2, 5);
                for (int i = 0; i < segs; i++) { lens[i] = rng.Range(0.7f, 1.9f); total += lens[i] + 0.2f; }
                Arc(heading, out float hc, out float hs);
                rad = Math.Min(rad, Math.Max(0f, 8.6f - 0.5f * total - 0.6f * d));
                float x = rad * ac - 0.5f * total * hc, z = rad * az - 0.5f * total * hs;
                for (int i = 0; i < segs; i++)
                {
                    Arc(heading + rng.Range(-0.025f, 0.025f), out float c, out float s);
                    float len = lens[i], cx = x + 0.5f * len * c, cz = z + 0.5f * len * s, sz = d * rng.Range(0.9f, 1.05f), bury = rng.Range(0.15f, 0.35f);
                    Round(l, rng.NextU64(), f, cx, -bury * d, cz, len, d, sz, Petrified, GX, RoundBroken, 0, 2, c, s);
                    for (float a = -0.5f * len + 0.3f; a <= 0.5f * len - 0.2f; a += 0.55f) Col(l, f, cx + a * c, cz + a * s, 0.45f * sz, 0, (0.95f - bury) * d);
                    Touch(l, f, cx, 0, cz, 0.5f * d);
                    float gap = rng.Range(0.05f, 0.35f), side = rng.Range(-0.2f, 0.2f);
                    x = cx + (0.5f * len + gap) * c - side * s;
                    z = cz + (0.5f * len + gap) * s + side * c;
                    d *= rng.Range(0.9f, 0.98f);
                }
            }
            int stumps = rng.Range(2, 4);
            for (int i = 0; i < stumps; i++)
            {
                Arc(a0 + (i + 0.5f + rng.Range(-0.2f, 0.2f)) / stumps, out float c, out float s);
                float r = rng.Range(2.5f, 9.5f), d = rng.Range(0.8f, 1.4f), h = rng.Range(0.35f, 1.5f);
                Round(l, rng.NextU64(), f, r * c, -0.2f, r * s, d, h + 0.2f, d * rng.Range(0.9f, 1.05f), Petrified, GV, RoundSnag, 0, 2);
                Col(l, f, r * c, r * s, 0.5f * d, 0, h);
                Touch(l, f, r * c, 0, r * s, 0.55f * d);
            }
            int chips = rng.Range(32, 56), solid = l.Pieces.Count;
            for (int i = 0; i < chips; i++)
            {
                var p = l.Pieces[rng.Range(0, solid)];
                float ox = (p.X - f.X) / f.U, oz = (p.Z - f.Z) / f.U, bx = f.C * ox + f.S * oz, bz = -f.S * ox + f.C * oz;
                Arc(rng.NextFloat(), out float c, out float s);
                float r = 0.5f * Math.Max(p.Sx, p.Sz) / f.U + rng.Range(0.1f, 2.2f), cd = rng.Range(0.05f, 0.22f), cx = bx + r * c, cz = bz + r * s, cr = (float)Math.Sqrt(cx * cx + cz * cz), cs = cr > 10f ? 10f / cr : 1f;
                int k = rng.Range(0, 16);
                Round(l, rng.NextU64(), f, cx * cs, -0.35f * cd, cz * cs, cd * rng.Range(1f, 3f), cd, cd * rng.Range(0.7f, 1.3f), Petrified, GX, RoundBroken, 0, 0, Cos16(k), Sin16(k));
            }
        }
    }

    public sealed partial class ObjectField
    {
        public const float PetrifiedProbeM = 24f, PetrifiedFlatM = 10f, PetrifiedReliefM = 1.2f, PetrifiedRimMaxM = 4f, PetrifiedRainMm = 1000f, PetrifiedWoods = 0.15f, PetrifiedRiverM = 60f, PetrifiedSeatM = 0.6f, PetrifiedSeekM = 40f, PetrifiedFloatM = 0.12f, PetrifiedBuryM = 0.15f, PetrifiedChipM = 0.4f;
        public const int PetrifiedSeekSteps = 2;

        public float PetrifiedFit(float x, float z)
        {
            if (x < PetrifiedProbeM || z < PetrifiedProbeM || x >= worldM - PetrifiedProbeM || z >= worldM - PetrifiedProbeM) return float.MinValue;
            int i = Cell(z) * n + Cell(x);
            if (w.Sea[i] || w.Water.LakeId[i] >= 0) return float.MinValue;
            if (!(Woods(x, z) <= PetrifiedWoods) || RiverGap(x, z) < PetrifiedRiverM) return float.MinValue;
            float y = GroundAt(detail, cm, x, z), rim = 0;
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, 0.70710678f, out float ux, out float uz);
                if (Math.Abs(GroundAt(detail, cm, x + ux * PetrifiedFlatM, z + uz * PetrifiedFlatM) - y) > PetrifiedReliefM) return float.MinValue;
                float dy = GroundAt(detail, cm, x + ux * PetrifiedProbeM, z + uz * PetrifiedProbeM) - y;
                if (dy > PetrifiedRimMaxM) return float.MinValue;
                rim += 0.125f * dy;
            }
            return Math.Min(rim, 2f) - w.Climate.PrecipitationMm[i] / PetrifiedRainMm;
        }

        bool SeekPetrified(int d, ref float x, ref float z, float x0, float z0, float x1, float z1)
        {
            float best = float.MinValue, bx = x, bz = z;
            for (int i = -PetrifiedSeekSteps; i <= PetrifiedSeekSteps; i++)
                for (int j = -PetrifiedSeekSteps; j <= PetrifiedSeekSteps; j++)
                {
                    float px = Math.Min(x1, Math.Max(x0, x + i * PetrifiedSeekM)), pz = Math.Min(z1, Math.Max(z0, z + j * PetrifiedSeekM));
                    float fit = Open(d, px, pz) ? PetrifiedFit(px, pz) : float.MinValue;
                    if (fit > best) { best = fit; bx = px; bz = pz; }
                }
            x = bx; z = bz;
            return best > float.MinValue;
        }

        public string PetrifiedFault(ObjectInstance o)
        {
            if (PetrifiedFit(o.X, o.Z) == float.MinValue) return "is not on gentle dry open ground";
            var def = lib.Objects[o.Def];
            SeatRange(detail, cm, Seats(def), o, out float lo, out float hi);
            if (hi - lo > PetrifiedSeatM) return "stands on uneven ground";
            float foot = o.Y + def.Placement.Sink * o.Size;
            foreach (var p in Structures.Layout(ObjectPart.PetrifiedForest, ObjectMeshes.Key(def.Id, 0, o.Variant)).Pieces)
            {
                if (p.Y - p.Base > Structures.GroundY || p.Sy * o.Size < PetrifiedChipM) continue;
                for (int e = -1; e <= 1; e++)
                {
                    float a = 0.4f * e * p.Sx;
                    Rotate(o, (p.X + a * p.C) * o.Size, 0, (p.Z + a * p.S) * o.Size, out float rx, out _, out float rz);
                    float px = o.X + rx, pz = o.Z + rz;
                    int i = Cell(pz) * n + Cell(px);
                    if (w.Sea[i] || w.Water.LakeId[i] >= 0) return "has a log in water";
                    float g = GroundAt(detail, cm, px, pz) - foot;
                    if (g < p.Y * o.Size - PetrifiedFloatM || g > (p.Y + p.Sy) * o.Size - PetrifiedBuryM) return "has a log floating or buried";
                }
            }
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
