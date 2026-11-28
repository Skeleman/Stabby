using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float LodestoneU = 1 / 14f;

        static readonly float[] LodestoneSeats = { 0, 0, 0.14f, 0, -0.14f, 0, 0, 0.14f, 0, -0.14f, 0.1f, 0.1f, -0.1f, 0.1f, 0.1f, -0.1f, -0.1f, -0.1f };

        static void Lodestone(StructureLayout l, ulong key, in Frame root)
        {
            var rng = Rng.Stream(key, "lodestone");
            int d0 = rng.Range(0, 16);
            var f = root.Child(0, 0, 0, Cos16(d0), Sin16(d0));

            int before = l.Pieces.Count, plates = rng.Range(9, 14);
            for (int k = 0; k < plates; k++)
            {
                int d = rng.Range(0, 16);
                float r = 4.6f * (float)Math.Sqrt(rng.NextFloat()), x = Cos16(d) * r, z = Sin16(d) * r, w = rng.Range(1.4f, 2.8f);
                Hewn(l, rng.NextU64(), f, x, -0.06f, z, w, rng.Range(0.1f, 0.16f), w * rng.Range(0.5f, 0.8f), Cos16(d + 4), Sin16(d + 4), 0, 1, rng.Range(0.5f, 0.7f), rng.Range(-0.02f, 0.02f), rng.Range(-0.02f, 0.02f));
            }
            Flat(l, before);
            Drape(l, before);

            int rocks = l.Pieces.Count;
            float h0 = rng.Range(3.6f, 4.8f);
            int q0 = rng.Range(0, 16);
            Hewn(l, rng.NextU64(), f, 0, -0.5f, 0, rng.Range(2.2f, 2.8f), h0 + 0.5f, rng.Range(1.5f, 2f), Cos16(q0), Sin16(q0), 0, 2, rng.Range(0.72f, 0.82f), rng.Range(-0.18f, 0.18f), rng.Range(-0.12f, 0.12f), 0.22f);
            Touch(l, f, 0, 0, 0, 1.5f);
            Col(l, f, 0, 0, 1f, -0.4f, h0);

            int shards = rng.Range(4, 8);
            float t0 = rng.NextFloat();
            for (int i = 0; i < shards; i++)
            {
                Arc(t0 + (i + rng.Range(-0.3f, 0.3f)) / shards, out float c, out float s);
                float r = rng.Range(1.6f, 3.4f), x = c * r, z = s * r, h = h0 * rng.Range(0.25f, 0.7f) * (1.3f - 0.2f * r), wd = rng.Range(0.8f, 1.6f);
                int q = rng.Range(0, 16);
                Hewn(l, rng.NextU64(), f, x, -0.4f, z, wd, h + 0.4f, wd * rng.Range(0.6f, 0.9f), Cos16(q), Sin16(q), 0, 2, rng.Range(0.68f, 0.85f), c * rng.Range(0.05f, 0.25f), s * rng.Range(0.05f, 0.25f), rng.Range(0.05f, 0.25f));
                Touch(l, f, x, 0, z, 0.55f * wd);
                Col(l, f, x, z, 0.4f * wd, -0.3f, h);
            }

            int pebbles = rng.Range(12, 22);
            for (int i = 0; i < pebbles; i++)
            {
                int d = rng.Range(0, 16);
                float r = rng.Range(2.4f, 6.2f), x = Cos16(d) * r + rng.Range(-0.3f, 0.3f), z = Sin16(d) * r + rng.Range(-0.3f, 0.3f), sz = rng.Range(0.14f, 0.42f);
                int q = rng.Range(0, 16);
                Hewn(l, rng.NextU64(), f, x, -0.08f, z, sz, sz * rng.Range(0.5f, 0.8f), sz * rng.Range(0.6f, 0.9f), Cos16(q), Sin16(q), 0, 1, rng.Range(0.55f, 0.8f), rng.Range(-0.12f, 0.12f), rng.Range(-0.12f, 0.12f));
                Touch(l, f, x, 0, z, 0.5f * sz);
            }
            Drape(l, rocks);

            if (rng.NextFloat() < 0.55f)
            {
                Arc(rng.NextFloat(), out float c, out float s);
                float r = rng.Range(5.2f, 6.2f), scale = rng.Range(1.4f, 1.9f);
                int d = rng.Range(0, 16);
                Cairn(l, rng.NextU64(), f.Child(c * r, -0.02f, s * r, Cos16(d), Sin16(d), 0, Tiers - 1, scale));
                Touch(l, f, c * r, 0, s * r, 0.5f * scale);
            }
        }
    }

    public sealed partial class ObjectField
    {
        public const float LodeProbeM = 100f, LodeFlatM = 3f, LodeFlatReliefM = 0.4f, LodeRiseMinM = 4f, LodeRiseCapM = 20f, LodeWoods = 0.5f, LodeRiverM = 40f, LodeSeatM = 0.5f, LodeSeekM = 60f;
        public const int LodeSeekSteps = 3;

        public float LodestoneFit(float x, float z)
        {
            if (x < LodeProbeM || z < LodeProbeM || x >= worldM - LodeProbeM || z >= worldM - LodeProbeM) return float.MinValue;
            int i = Cell(z) * n + Cell(x);
            if (w.Sea[i] || w.Water.LakeId[i] >= 0) return float.MinValue;
            float woods = Woods(x, z);
            if (woods > LodeWoods || RiverGap(x, z) < LodeRiverM) return float.MinValue;
            float y = GroundAt(detail, cm, x, z), rise = 0, dev = 0;
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, 0.70710678f, out float ux, out float uz);
                dev = Math.Max(dev, Math.Abs(GroundAt(detail, cm, x + ux * LodeFlatM, z + uz * LodeFlatM) - y));
                if (dev > LodeFlatReliefM) return float.MinValue;
                rise += 0.125f * (y - GroundAt(detail, cm, x + ux * LodeProbeM, z + uz * LodeProbeM));
            }
            if (rise < LodeRiseMinM) return float.MinValue;
            return Math.Min(rise, LodeRiseCapM) / LodeRiseCapM - woods - dev / LodeFlatReliefM;
        }

        bool SeekLodestone(ref float x, ref float z, float x0, float z0, float x1, float z1)
        {
            float best = float.MinValue, bx = x, bz = z;
            for (int i = -LodeSeekSteps; i <= LodeSeekSteps; i++)
                for (int j = -LodeSeekSteps; j <= LodeSeekSteps; j++)
                {
                    float px = Math.Min(x1, Math.Max(x0, x + i * LodeSeekM)), pz = Math.Min(z1, Math.Max(z0, z + j * LodeSeekM));
                    float fit = LodestoneFit(px, pz);
                    if (fit > best) { best = fit; bx = px; bz = pz; }
                }
            x = bx; z = bz;
            return best > float.MinValue;
        }

        public string LodestoneFault(ObjectInstance o)
        {
            if (LodestoneFit(o.X, o.Z) == float.MinValue) return "is not on dry high ground";
            SeatRange(detail, cm, Seats(lib.Objects[o.Def]), o, out float lo, out float hi);
            if (hi - lo > LodeSeatM) return "stands on uneven ground";
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
