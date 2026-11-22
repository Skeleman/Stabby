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
