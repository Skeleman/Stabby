using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float SpringU = 1 / 20f, SpringLift = 3f, SpringCrest = 0.3f, SpringDepth = 1f, SpringRim = 1.6f;
        public const int PoolStride = 4;
        static readonly float[] SpringSeats = { 0, 0 };
        public const float SpringSlopeMin = 0.05f, SpringSlopeMax = 0.24f;

        public static float SpringSlope(ulong key) => Rng.Stream(key, "hot-spring-slope").Range(SpringSlopeMin, SpringSlopeMax);

        public static void SpringPools(ulong key, List<float> o)
        {
            o.Clear();
            var rng = Rng.Stream(key, "hot-spring");
            int count = rng.Range(3, 5);
            var rs = new float[count];
            var zs = new float[count];
            for (int k = 0; k < count; k++) rs[k] = rng.Range(2.4f, 3.2f) * (1 - 0.06f * k);
            for (int k = 1; k < count; k++) zs[k] = zs[k - 1] + rs[k - 1] + rs[k] + rng.Range(1.8f, 2.2f);
            float mid = 0.5f * (zs[0] + zs[count - 1]), x = 0;
            for (int k = 0; k < count; k++)
            {
                x = Math.Max(-1.5f, Math.Min(1.5f, x + rng.Range(-1f, 1f)));
                o.Add(x); o.Add(zs[k] - mid); o.Add(rs[k]); o.Add(rs[k] + SpringRim);
            }
        }

        public const float VentLift = 0.4f, MudRim = 1.2f;
        static readonly float[] VentSeats = { 0, 0 };

        public static void GeyserPools(ulong key, List<float> o)
        {
            o.Clear();
            var rng = Rng.Stream(key, "geyser");
            float r = rng.Range(1.0f, 1.4f);
            o.Add(0); o.Add(0); o.Add(r); o.Add(rng.Range(7f, 9f));
        }

        public static void GeyserShape(ulong key, out float mound, out float top, out float cone, out float coneTop)
        {
            var rng = Rng.Stream(key, "geyser-shape");
            mound = rng.Range(0.5f, 0.9f);
            top = rng.Range(3.5f, 4.5f);
            cone = rng.Range(1.6f, 2.6f);
            coneTop = rng.Range(1.2f, 1.8f);
        }

        public static void GeyserCycle(ulong seed, out float interval, out float duration, out float offset)
        {
            var rng = Rng.Stream(seed, "geyser-cycle");
            interval = rng.Range(40f, 110f);
            duration = rng.Range(8f, 18f);
            offset = rng.NextFloat() * interval;
        }

        public static void MudPools(ulong key, List<float> o)
        {
            o.Clear();
            var rng = Rng.Stream(key, "mud-pot");
            int count = rng.Range(1, 4);
            float r0 = rng.Range(1.6f, 2.6f);
            o.Add(0); o.Add(0); o.Add(r0); o.Add(r0 + MudRim);
            for (int k = 1; k < count; k++)
            {
                float r = rng.Range(1.0f, 2.0f);
                int a = rng.Range(0, 16);
                int from = 0;
                for (int t = 0; t < 6; t++)
                {
                    from = PoolStride * rng.Range(0, o.Count / PoolStride);
                    float d = o[from + 2] + r + rng.Range(0.8f, 1.6f), x = o[from] + d * Cos16(a), z = o[from + 1] + d * Sin16(a);
                    bool clear = true;
                    for (int j = 0; j < o.Count; j += PoolStride)
                    {
                        float dx = o[j] - x, dz = o[j + 1] - z, g = o[j + 2] + r + 0.8f;
                        if (dx * dx + dz * dz < g * g) clear = false;
                    }
                    if (clear) { o.Add(x); o.Add(z); o.Add(r); o.Add(r + MudRim); break; }
                    a += 7;
                }
            }
        }

        static void VentRocks(StructureLayout l, ulong key, in Frame f, List<float> pools, string stream)
        {
            float side = 0;
            for (int k = 0; k < pools.Count; k += PoolStride) side = Math.Max(side, (float)Math.Sqrt(pools[k] * pools[k] + pools[k + 1] * pools[k + 1]) + pools[k + 3]);
            var rng = Rng.Stream(key, stream);
            int rocks = rng.Range(2, 5);
            for (int i = 0; i < rocks; i++)
            {
                float sz = rng.Range(0.5f, 1.0f), d = Math.Min(side + rng.Range(0.3f, 1.5f) + 0.6f * sz, 9.6f - 1.3f * sz);
                int yaw = rng.Range(0, 16), a = rng.Range(0, 16);
                Rock(l, rng.NextU64(), f, d * Cos16(a), VentLift - 0.35f * sz, d * Sin16(a), sz * 1.3f, sz * 0.8f, sz, Cos16(yaw), Sin16(yaw), 0, i == 0 ? 2 : 1);
            }
        }

        static void Geyser(StructureLayout l, ulong key, in Frame f)
        {
            var pools = new List<float>();
            GeyserPools(key, pools);
            VentRocks(l, key, f, pools, "geyser-rocks");
        }

        static void MudPot(StructureLayout l, ulong key, in Frame f)
        {
            var pools = new List<float>();
            MudPools(key, pools);
            VentRocks(l, key, f, pools, "mud-pot-rocks");
        }

        static void HotSpring(StructureLayout l, ulong key, in Frame f)
        {
            var pools = new List<float>();
            SpringPools(key, pools);
            if (pools.Count == 0) return;
            float side = 0, s = SpringSlope(key);
            for (int k = 0; k < pools.Count; k += PoolStride) side = Math.Max(side, Math.Abs(pools[k]) + pools[k + 3]);
            var rng = Rng.Stream(key, "hot-spring-rocks");
            int rocks = rng.Range(2, 4);
            for (int i = 0; i < rocks; i++)
            {
                float sz = rng.Range(0.6f, 1.1f), px = (rng.NextFloat() < 0.5f ? -1 : 1) * (side + rng.Range(0.3f, 0.8f) + 0.6f * sz), pz = rng.Range(-3f, 3f);
                int yaw = rng.Range(0, 16);
                Rock(l, rng.NextU64(), f, px, SpringLift - s * pz - 0.35f * sz, pz, sz * 1.3f, sz * 0.8f, sz, Cos16(yaw), Sin16(yaw), 0, i == 0 ? 2 : 1);
                Touch(l, f, px, SpringLift - s * pz - 0.35f * sz, pz, 0.55f * sz);
            }
        }
    }
}
