using System;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float GreatMushroomU = 1 / 19.5f, GreatHueMin = 0.4f;

        static readonly float[] GreatMushroomSeats = { 0, 0, 0.25f, 0, -0.25f, 0, 0, 0.25f, 0, -0.25f, 0.18f, 0.18f, -0.18f, 0.18f, 0.18f, -0.18f, -0.18f, -0.18f };

        static float Hue(ulong key)
        {
            var tr = ToneRng(key);
            tr.NextFloat();
            tr.NextFloat();
            return tr.NextFloat();
        }

        static void GreatMushroom(StructureLayout l, ulong key, in Frame root)
        {
            var rng = Rng.Stream(key, "great-mushroom");
            int d0 = rng.Range(0, 16);
            var f = root.Child(0, 0, 0, Cos16(d0), Sin16(d0));
            ulong dye = Rng.Mix(key ^ 0x9E3779B97F4A7C15UL);
            while (Hue(dye) < GreatHueMin) dye = Rng.Mix(dye + 1);

            Band(l, f, 0, 0, rng.Range(9f, 11f), rng.Range(1.6f, 2.2f));
            Mushroom(l, rng.NextU64(), f.Child(0, 0, 0, 1, 0, 0, 2, rng.Range(10.5f, 11.5f)), dye, true);

            int clumps = rng.Range(4, 8);
            float t0 = rng.NextFloat();
            for (int i = 0; i < clumps; i++)
            {
                Arc(t0 + (i + rng.Range(-0.3f, 0.3f)) / clumps, out float c, out float s);
                float r = rng.Range(2.9f, 4.6f);
                int d = rng.Range(0, 16);
                Mushroom(l, rng.NextU64(), f.Child(c * r, -0.1f, s * r, Cos16(d), Sin16(d), 0, 2, rng.Range(5.5f, 9f)), dye);
            }
            int tiny = rng.Range(5, 10);
            for (int i = 0; i < tiny; i++)
            {
                Arc(rng.NextFloat(), out float c, out float s);
                float r = rng.Range(3.2f, 5f);
                int d = rng.Range(0, 16);
                Mushroom(l, rng.NextU64(), f.Child(c * r, -0.04f, s * r, Cos16(d), Sin16(d), 0, 1, rng.Range(2.4f, 3.6f)), dye);
            }
        }
    }

    public sealed partial class ObjectField
    {
        public const float GreatProbeM = 18f, GreatFlatM = 5f, GreatFlatReliefM = 0.25f, GreatRiseMinM = 0.4f, GreatRiseMaxM = 3f, GreatDipM = 0.3f, GreatRiseAimM = 1.5f, GreatWoods = 0.5f, GreatRiverM = 20f, GreatSeatM = 0.25f;

        public float GreatMushroomFit(float x, float z)
        {
            if (x < GreatProbeM || z < GreatProbeM || x >= worldM - GreatProbeM || z >= worldM - GreatProbeM) return float.MinValue;
            int i = Cell(z) * n + Cell(x);
            if (w.Sea[i] || w.Water.LakeId[i] >= 0) return float.MinValue;
            float woods = Woods(x, z);
            if (!(woods >= GreatWoods) || RiverGap(x, z) < GreatRiverM) return float.MinValue;
            float y = GroundAt(detail, cm, x, z), rise = 0;
            for (int k = 0; k < 8; k++)
            {
                FanDir(k, 0.70710678f, out float ux, out float uz);
                if (Math.Abs(GroundAt(detail, cm, x + ux * GreatFlatM, z + uz * GreatFlatM) - y) > GreatFlatReliefM) return float.MinValue;
                float dy = y - GroundAt(detail, cm, x + ux * GreatProbeM, z + uz * GreatProbeM);
                if (dy < -GreatDipM) return float.MinValue;
                rise += 0.125f * dy;
            }
            if (rise < GreatRiseMinM || rise > GreatRiseMaxM) return float.MinValue;
            return woods - 0.3f * Math.Abs(rise - GreatRiseAimM);
        }

        public string GreatMushroomFault(ObjectInstance o)
        {
            if (GreatMushroomFit(o.X, o.Z) == float.MinValue) return "is not on a low mound in damp forest";
            SeatRange(detail, cm, Seats(lib.Objects[o.Def]), o, out float lo, out float hi);
            if (hi - lo > GreatSeatM) return "stands on uneven ground";
            foreach (var t in TownPlans())
            {
                float dx = t.X - o.X, dz = t.Z - o.Z;
                if (Math.Sqrt(dx * dx + dz * dz) - t.Radius < TownClearM) return "stands within 400 m of a town";
            }
            return null;
        }
    }
}
