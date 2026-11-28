using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float BrineRim = 0.9f, BrineReach = 9.2f, BrineGap = 0.3f, BrineSpread = 10.4f, BrineCell = 1.9f;

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

            int before = l.Pieces.Count, rows = (int)(BrineSpread / (0.866f * BrineCell)) + 1;
            for (int j = -rows; j <= rows; j++)
                for (int i = -rows; i <= rows; i++)
                {
                    float x = (i + 0.5f * (j & 1)) * BrineCell + rng.Range(-0.2f, 0.2f) * BrineCell, z = 0.866f * j * BrineCell + rng.Range(-0.2f, 0.2f) * BrineCell;
                    float edge = BrineSpread * rng.Range(0.72f, 1f);
                    if (x * x + z * z > edge * edge) continue;
                    bool wet = false;
                    for (int k = 0; k < pools.Count; k += PoolStride)
                    {
                        float dx = pools[k] - x, dz = pools[k + 1] - z, g = pools[k + 3] + 0.4f * BrineCell;
                        if (dx * dx + dz * dz < g * g) wet = true;
                    }
                    if (wet) continue;
                    float w = BrineCell * rng.Range(0.8f, 0.94f);
                    int q = rng.Range(0, 16);
                    Hewn(l, rng.NextU64(), f, x, -0.07f, z, w, rng.Range(0.1f, 0.16f), w * rng.Range(0.78f, 0.95f), Cos16(q), Sin16(q), 0, 1, rng.Range(0.28f, 0.42f), rng.Range(-0.02f, 0.02f), rng.Range(-0.02f, 0.02f));
                }
            Flat(l, before);
            Drape(l, before);

            int rims = l.Pieces.Count;
            for (int k = 0; k < pools.Count; k += PoolStride)
            {
                float px = pools[k], pz = pools[k + 1], r = pools[k + 2];
                int crystals = (int)(6.2831853f * (r + 0.3f) / 0.42f), a0 = rng.Range(0, 16);
                for (int m = 0; m < crystals; m++)
                {
                    float t = (m + rng.Range(-0.3f, 0.3f)) / crystals * 6.2831853f, ring = r + rng.Range(0.05f, 0.45f), c = TurnCos(t), s = TurnSin(t);
                    float x = px + c * ring, z = pz + s * ring, w = rng.Range(0.16f, 0.36f);
                    int q = rng.Range(0, 16);
                    Hewn(l, rng.NextU64(), f, x, -0.05f, z, w, rng.Range(0.24f, 0.44f), w * rng.Range(0.6f, 0.9f), Cos16(q), Sin16(q), 0, 0, rng.Range(0.18f, 0.3f), c * 0.2f, s * 0.2f, 0.25f);
                }
                int lumps = rng.Range(2, 4);
                for (int m = 0; m < lumps; m++)
                {
                    int d = a0 + m * 16 / lumps + rng.Range(-1, 2);
                    float ring = r + 0.35f, x = px + Cos16(d) * ring, z = pz + Sin16(d) * ring, w = rng.Range(0.5f, 0.8f);
                    int q = rng.Range(0, 16);
                    Hewn(l, rng.NextU64(), f, x, -0.12f, z, w, rng.Range(0.22f, 0.32f), w * rng.Range(0.55f, 0.8f), Cos16(q), Sin16(q), 0, 2, rng.Range(0.3f, 0.45f), Cos16(d) * 0.15f, Sin16(d) * 0.15f, 0.2f);
                    Touch(l, f, x, 0, z, 0.45f * w);
                }
            }
            Drape(l, rims);
        }
    }

    public sealed partial class ObjectField
    {
        public const float BrineSinkU = 0.04f, BrineCrestU = 0.26f, BrineFootU = 0.2f, BrineDepthU = 0.7f, BrineRimU = 0.55f, BrineRimSlope = 1.4f, BrineBankU = 0.12f, BrineBank = 0.9f, BrineLobe = 0.15f;
        public static readonly string[] BrineLiquids = { "brine-teal", "brine-rust", "brine-rose", "brine-jade" };

        public int BrineLiquid(ObjectInstance o, int pool)
        {
            if (w.Materials == null) return LiquidOf(o);
            int id = w.Materials.Liquid(BrineLiquids[(int)(Rng.Mix(VentSeed(o) ^ (ulong)pool * 0x9E3779B97F4A7C15UL) % (ulong)BrineLiquids.Length)]);
            return id < 0 ? LiquidOf(o) : id;
        }

        public string BrineFault(ObjectInstance o) => SaltFault(o) ?? VentFault(o);
    }
}
