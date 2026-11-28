using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float BrineRim = 0.9f, BrineReach = 9.2f, BrineGap = 0.3f;

        public static void BrinePools(ulong key, List<float> o)
        {
            o.Clear();
            var rng = Rng.Stream(key, "brine-pools");
            int want = rng.Range(3, 7);
            for (int t = 0; t < 60 && o.Count < want * PoolStride; t++)
            {
                float r = o.Count == 0 ? rng.Range(1.6f, 2.2f) : rng.Range(1f, 1.8f), d = o.Count == 0 ? 0 : rng.Range(o[3] + r + BrineRim + BrineGap, BrineReach - r - BrineRim);
                int a = rng.Range(0, 16);
                float x = d * Cos16(a) + (o.Count == 0 ? 0 : rng.Range(-0.4f, 0.4f)), z = d * Sin16(a) + (o.Count == 0 ? 0 : rng.Range(-0.4f, 0.4f));
                bool clear = true;
                for (int j = 0; j < o.Count; j += PoolStride)
                {
                    float dx = o[j] - x, dz = o[j + 1] - z, g = o[j + 3] + r + BrineRim + BrineGap;
                    if (dx * dx + dz * dz < g * g) clear = false;
                }
                if (clear) { o.Add(x); o.Add(z); o.Add(r); o.Add(r + BrineRim); }
            }
        }

        static void BrinePools(StructureLayout l, ulong key, in Frame f)
        {
            var rng = Rng.Stream(key, "brine-crust");
            var pools = new List<float>();
            BrinePools(key, pools);

            int rims = l.Pieces.Count;
            for (int k = 0; k < pools.Count; k += PoolStride)
            {
                float px = pools[k], pz = pools[k + 1], r = pools[k + 2];
                int arcs = rng.Range(1, 3), a0 = rng.Range(0, 16);
                for (int g = 0; g < arcs; g++)
                {
                    float t0 = rng.Range(0f, 6.2831853f);
                    int clusters = rng.Range(1, 4);
                    for (int c = 0; c < clusters; c++)
                    {
                        float t = t0 + rng.Range(-0.6f, 0.6f), ring = r + rng.Range(0.08f, 0.4f);
                        float cx = px + TurnCos(t) * ring, cz = pz + TurnSin(t) * ring;
                        int few = rng.Range(2, 5);
                        for (int m = 0; m < few; m++)
                        {
                            float x = cx + rng.Range(-0.16f, 0.16f), z = cz + rng.Range(-0.16f, 0.16f), w = rng.Range(0.05f, 0.15f);
                            int q = rng.Range(0, 16);
                            Hewn(l, rng.NextU64(), f, x, -0.04f, z, w, rng.Range(0.08f, 0.24f), w * rng.Range(0.5f, 0.9f), Cos16(q), Sin16(q), 0, m == 0 ? 1 : 0, rng.Range(0.1f, 0.22f), rng.Range(-0.3f, 0.3f), rng.Range(-0.3f, 0.3f), rng.Range(0.25f, 0.55f));
                        }
                    }
                }
                int lumps = rng.Range(1, 3);
                for (int m = 0; m < lumps; m++)
                {
                    int d = a0 + m * 16 / lumps + rng.Range(-1, 2);
                    float ring = r + 0.3f, x = px + Cos16(d) * ring, z = pz + Sin16(d) * ring, w = rng.Range(0.35f, 0.6f);
                    int q = rng.Range(0, 16);
                    Hewn(l, rng.NextU64(), f, x, -0.08f, z, w, rng.Range(0.12f, 0.18f), w * rng.Range(0.55f, 0.8f), Cos16(q), Sin16(q), 0, 2, rng.Range(0.3f, 0.45f), Cos16(d) * 0.1f, Sin16(d) * 0.1f, 0.2f);
                    Touch(l, f, x, 0, z, 0.45f * w);
                }
            }
            Flat(l, rims);
            Drape(l, rims);
        }
    }

    public sealed partial class ObjectField
    {
        public const float BrineSinkU = 0.04f, BrineFillU = 0.14f, BrineCrestU = 0.26f, BrineFootU = 0.2f, BrineDepthU = 0.7f, BrineRimU = 0.55f, BrineRimSlope = 1.4f, BrineBankU = 0.12f, BrineBank = 0.9f, BrineLobe = 0.15f;
        public static readonly string[] BrineLiquids = { "brine-teal", "brine-aqua", "brine-rose", "brine-jade" };

        public int BrineLiquid(ObjectInstance o, int pool)
        {
            if (w.Materials == null) return LiquidOf(o);
            int id = w.Materials.Liquid(BrineLiquids[(int)(Rng.Mix(VentSeed(o) ^ (ulong)pool * 0x9E3779B97F4A7C15UL) % (ulong)BrineLiquids.Length)]);
            return id < 0 ? LiquidOf(o) : id;
        }

        public string BrineFault(ObjectInstance o) => SaltFault(o) ?? VentFault(o);
    }
}
