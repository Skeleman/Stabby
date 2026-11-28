using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float PetrifiedU = 1 / 20f;

        static readonly float[] PetrifiedSeats = { 0, 0, 0.2f, 0, -0.2f, 0, 0, 0.2f, 0, -0.2f, 0.14f, 0.14f, -0.14f, 0.14f, 0.14f, -0.14f, -0.14f, -0.14f, 0.32f, 0, -0.32f, 0, 0, 0.32f, 0, -0.32f };

        static void PetrifiedForest(StructureLayout l, ulong key, in Frame root)
        {
            var rng = Rng.Stream(key, "petrified-forest");
            int d0 = rng.Range(0, 16);
            var f = root.Child(0, 0, 0, Cos16(d0), Sin16(d0));
            int sections = rng.Range(3, 7), trunks = sections >= 5 && rng.NextFloat() < 0.6f ? 2 : 1, made = 0;
            for (int t = 0; t < trunks; t++)
            {
                int count = t == trunks - 1 ? sections - made : rng.Range(2, sections - made - 1);
                float d = rng.Range(0.75f, 1.25f), x = t == 0 ? rng.Range(-4.5f, -2.5f) : rng.Range(-1.5f, 1.5f), z = t == 0 ? rng.Range(-2f, 2f) : rng.Range(2.5f, 4.5f) * (rng.NextFloat() < 0.5f ? -1 : 1);
                int dir = t == 0 ? rng.Range(-1, 2) : rng.Range(-3, 4);
                for (int i = 0; i < count; i++)
                {
                    Arc((dir + rng.Range(-0.35f, 0.35f)) / 16f, out float c, out float s);
                    float len = rng.Range(0.8f, 2.1f), cx = x + 0.5f * len * c, cz = z + 0.5f * len * s, sz = d * rng.Range(0.9f, 1.04f);
                    Round(l, rng.NextU64(), f, cx, -0.14f * d, cz, len, d, sz, Petrified, GX, RoundLog, 0, 2, c, s);
                    for (float a = -0.5f * len + 0.3f; a <= 0.5f * len - 0.2f; a += 0.55f) Col(l, f, cx + a * c, cz + a * s, 0.45f * sz, 0, 0.84f * d);
                    Touch(l, f, cx, 0, cz, 0.5f * d);
                    float gap = rng.Range(0.05f, 0.6f);
                    x = cx + (0.5f * len + gap) * c - rng.Range(-0.15f, 0.15f) * s;
                    z = cz + (0.5f * len + gap) * s + rng.Range(-0.15f, 0.15f) * c;
                    d *= rng.Range(0.9f, 0.98f);
                    made++;
                }
            }
            int stumps = rng.Range(2, 4);
            float s0 = rng.NextFloat();
            for (int i = 0; i < stumps; i++)
            {
                Arc(s0 + (i + rng.Range(-0.2f, 0.2f)) / stumps, out float c, out float s);
                float r = rng.Range(5.5f, 7f), d = rng.Range(0.8f, 1.3f), h = rng.Range(0.45f, 1.1f);
                Round(l, rng.NextU64(), f, r * c, -0.15f, r * s, d, h + 0.15f, d * rng.Range(0.9f, 1.05f), Petrified, GV, RoundStem, 0, 2);
                Col(l, f, r * c, r * s, 0.5f * d, 0, h);
                Touch(l, f, r * c, 0, r * s, 0.55f * d);
            }
            int chips = rng.Range(14, 24), solid = l.Pieces.Count;
            for (int i = 0; i < chips; i++)
            {
                var p = l.Pieces[rng.Range(0, solid)];
                float ox = (p.X - f.X) / f.U, oz = (p.Z - f.Z) / f.U, bx = f.C * ox + f.S * oz, bz = -f.S * ox + f.C * oz;
                Arc(rng.NextFloat(), out float c, out float s);
                float r = 0.5f * Math.Max(p.Sx, p.Sz) / f.U + rng.Range(0.2f, 1.6f), cd = rng.Range(0.08f, 0.24f);
                int k = rng.Range(0, 16);
                Round(l, rng.NextU64(), f, bx + r * c, -0.35f * cd, bz + r * s, cd * rng.Range(1f, 2.2f), cd, cd * rng.Range(0.8f, 1.2f), Petrified, GX, RoundLog, 0, 0, Cos16(k), Sin16(k));
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

        bool SeekPetrified(ref float x, ref float z, float x0, float z0, float x1, float z1)
        {
            float best = float.MinValue, bx = x, bz = z;
            for (int i = -PetrifiedSeekSteps; i <= PetrifiedSeekSteps; i++)
                for (int j = -PetrifiedSeekSteps; j <= PetrifiedSeekSteps; j++)
                {
                    float px = Math.Min(x1, Math.Max(x0, x + i * PetrifiedSeekM)), pz = Math.Min(z1, Math.Max(z0, z + j * PetrifiedSeekM));
                    float fit = PetrifiedFit(px, pz);
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
