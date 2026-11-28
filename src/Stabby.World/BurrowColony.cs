using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float BurrowU = 1 / 45f;

        static void BurrowColony(StructureLayout l, ulong key, in Frame root)
        {
            var rng = Rng.Stream(key, "burrow-colony");
            int d0 = rng.Range(0, 16);
            var f = root.Child(0, 0, 0, Cos16(d0), Sin16(d0));

            float rx = rng.Range(14f, 18f), rz = rng.Range(9f, 14f);
            int want = rng.Range(15, 31), placed = 0;
            var px = new float[want];
            var pz = new float[want];
            for (int tries = 0; tries < 600 && placed < want; tries++)
            {
                Arc(rng.NextFloat(), out float c, out float s);
                float r = (float)Math.Sqrt(rng.NextFloat()), x = c * r * rx + rng.Range(-1f, 1f), z = s * r * rz + rng.Range(-1f, 1f);
                bool near = false;
                for (int j = 0; j < placed && !near; j++)
                {
                    float dx = x - px[j], dz = z - pz[j];
                    near = dx * dx + dz * dz < 2.7f * 2.7f;
                }
                if (near) continue;
                px[placed] = x; pz[placed] = z; placed++;
            }

            for (int i = 0; i < placed; i++)
            {
                float x = px[i], z = pz[i], w = rng.Range(1f, 2f), h = rng.Range(0.3f, 0.6f), ring = rng.Range(2.2f, 3.4f);
                int q = rng.Range(0, 16), hd = rng.Range(0, 16);
                Round(l, rng.NextU64(), f, x, -0.03f, z, ring, 0.07f, ring * rng.Range(0.8f, 1f), Soil, GV, RoundDome, 0, 1, Cos16(q), Sin16(q));
                Round(l, rng.NextU64(), f, x, -0.2f * h, z, w, 1.25f * h, w * rng.Range(0.8f, 1f), Soil, GV, RoundDome, 0, 2, Cos16(q + 3), Sin16(q + 3));
                float off = rng.Range(0.05f, 0.22f) * w, hx = x + Cos16(hd) * off, hz = z + Sin16(hd) * off, hr = rng.Range(0.3f, 0.45f);
                Round(l, rng.NextU64(), f, hx, 0.8f * h, hz, hr, 0.03f, hr * rng.Range(0.8f, 1f), Ash, GV, RoundDisc, 0, 1, Cos16(q), Sin16(q));
                Box(l, rng.NextU64(), f, x, -0.5f * h, z, 0.8f * w, 0.4f * h, 0.8f * w, Soil, GV, 0, 2, Cos16(q), Sin16(q));
                Touch(l, f, x, 0, z, 0.5f * ring);
                if (rng.NextFloat() < 0.4f)
                {
                    int fd = hd + rng.Range(-2, 3);
                    float fl = rng.Range(0.7f, 1.2f), fo = 0.5f * w + 0.4f * fl;
                    Round(l, rng.NextU64(), f, x + Cos16(fd) * fo, -0.02f, z + Sin16(fd) * fo, fl, 0.07f, 0.5f * fl, Soil, GV, RoundDome, 0, 1, Cos16(fd), Sin16(fd));
                }
            }
        }
    }

    public sealed partial class ObjectField
    {
        public const float BurrowProbeM = 60f, BurrowFlatM = 18f, BurrowReliefM = 1.5f, BurrowWoods = 0.1f, BurrowRiverM = 60f, BurrowSeekM = 40f;
        public const int BurrowSeekSteps = 3;

        public float BurrowColonyFit(float x, float z)
        {
            if (x < BurrowProbeM || z < BurrowProbeM || x >= worldM - BurrowProbeM || z >= worldM - BurrowProbeM) return float.MinValue;
            int i = Cell(z) * n + Cell(x);
            if (w.Sea[i] || w.Water.LakeId[i] >= 0) return float.MinValue;
            float woods = Woods(x, z);
            if (woods > BurrowWoods || RiverGap(x, z) < BurrowRiverM) return float.MinValue;
            float y = GroundAt(detail, cm, x, z), dev = 0;
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, 0.70710678f, out float ux, out float uz);
                dev = Math.Max(dev, Math.Abs(GroundAt(detail, cm, x + ux * BurrowFlatM, z + uz * BurrowFlatM) - y));
                if (dev > BurrowReliefM) return float.MinValue;
            }
            return 1 - woods - dev / BurrowReliefM;
        }

        bool SeekBurrowColony(ref float x, ref float z, float x0, float z0, float x1, float z1)
        {
            float best = float.MinValue, bx = x, bz = z;
            for (int i = -BurrowSeekSteps; i <= BurrowSeekSteps; i++)
                for (int j = -BurrowSeekSteps; j <= BurrowSeekSteps; j++)
                {
                    float px = Math.Min(x1, Math.Max(x0, x + i * BurrowSeekM)), pz = Math.Min(z1, Math.Max(z0, z + j * BurrowSeekM));
                    float fit = BurrowColonyFit(px, pz);
                    if (fit > best) { best = fit; bx = px; bz = pz; }
                }
            x = bx; z = bz;
            return best > float.MinValue;
        }

        public string BurrowColonyFault(ObjectInstance o)
        {
            if (BurrowColonyFit(o.X, o.Z) == float.MinValue) return "is not on open level grassland";
            foreach (var t in TownPlans())
            {
                float dx = t.X - o.X, dz = t.Z - o.Z;
                if (Math.Sqrt(dx * dx + dz * dz) - t.Radius < TownClearM) return "stands within 400 m of a town";
            }
            float sr = SiteRadius(o);
            foreach (var f in edits)
                if ((f.Shape == FeatureEdit.Cave || f.Shape == FeatureEdit.CaveRoom || f.Shape == FeatureEdit.Sink || f.Shape == FeatureEdit.Shaft || f.Shape == FeatureEdit.Mine) && o.X + sr >= f.MinX && o.X - sr <= f.MaxX && o.Z + sr >= f.MinZ && o.Z - sr <= f.MaxZ) return "stands over a cave";
            return null;
        }
    }
}
