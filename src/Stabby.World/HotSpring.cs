using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class Structures
    {
        public const float SpringU = 1 / 20f, SpringLift = 3f, SpringCrest = 0.15f, SpringDepth = 2.4f, SpringRim = 1.2f;
        public const int Sinter = 8, SpringWater = 9, PoolStride = 7;
        const int RoundTerrace = 3, RoundPool = 4;
        static readonly float[] SpringSeats = { 0, 0 };
        public const float SpringSlopeMin = 0.05f, SpringSlopeMax = 0.24f;

        public static float SpringSlope(ulong key) => Rng.Stream(key, "hot-spring-slope").Range(SpringSlopeMin, SpringSlopeMax);

        public static void SpringPools(ulong key, List<float> o)
        {
            o.Clear();
            var rng = Rng.Stream(key, "hot-spring");
            int count = rng.Range(3, 6);
            float s = SpringSlope(key), gap = rng.Range(3.2f, 3.6f), rmax = 0;
            var rs = new float[count];
            for (int k = 0; k < count; k++) { rs[k] = rng.Range(1.6f, 2.4f) * (1 - 0.06f * k); rmax = Math.Max(rmax, rs[k]); }
            float drift = rng.Range(-0.6f, 0.6f), x = 0;
            for (int k = 0; k < count; k++)
            {
                float z = (k - 0.5f * (count - 1)) * gap, r = rs[k], rout = r + SpringRim;
                x = Math.Max(-10.6f + rout, Math.Min(10.6f - rout, x + drift + rng.Range(-1f, 1f)));
                z = Math.Max(-10.6f + rout, Math.Min(10.6f - rout, z));
                float level = SpringLift + s * 0.95f * rmax + 0.2f - s * z;
                o.Add(x); o.Add(z); o.Add(r); o.Add(rout); o.Add(level); o.Add(level + SpringCrest); o.Add(level - SpringDepth);
            }
        }

        static void HotSpring(StructureLayout l, ulong key, in Frame f)
        {
            var pools = new List<float>();
            SpringPools(key, pools);
            var rng = Rng.Stream(key, "hot-spring-rocks");
            for (int k = 0; k < pools.Count; k += PoolStride)
            {
                float x = pools[k], z = pools[k + 1], rout = pools[k + 3], crest = pools[k + 5], bottom = pools[k + 6];
                int lo = 0, hi = Tiers - 1;
                int at = l.Pieces.Count;
                Round(l, key, f, x, bottom, z, 2 * rout, crest - bottom, 2 * rout, Sinter, GV, RoundTerrace, lo, hi);
                if (l.Pieces.Count == at) continue;
                ulong shared = l.Pieces[at].Key;
                int water = l.Pieces.Count;
                Round(l, key, f, x, pools[k + 4] - 0.01f, z, 2 * rout, 0.01f, 2 * rout, SpringWater, GV, RoundPool, lo, hi);
                if (l.Pieces.Count > water) { var p = l.Pieces[water]; p.Key = shared; l.Pieces[water] = p; }
                Col(l, f, x, z, rout, bottom, crest);
            }
            if (pools.Count == 0) return;
            float tx = pools[0], tz = pools[1], tr = pools[3], s = SpringSlope(key);
            int rocks = rng.Range(2, 4);
            for (int i = 0; i < rocks; i++)
            {
                int a = rng.Range(9, 16);
                float d = tr + rng.Range(0.1f, 0.6f), sz = rng.Range(0.6f, 1.1f);
                float px = tx + Cos16(a) * d, pz = tz + Sin16(a) * d, ground = SpringLift - s * pz;
                int yaw = rng.Range(0, 16);
                Rock(l, rng.NextU64(), f, px, ground - 0.35f * sz, pz, sz * 1.3f, sz * 0.8f, sz, Cos16(yaw), Sin16(yaw), 0, 1);
            }
        }

        static float[] SpringMesh(ref Rng rng, int shape, int tier, out int[] idx, out float[] lift)
        {
            int sides = tier == 0 ? 32 : tier == 1 ? 16 : 8;
            var dc = new float[sides];
            var ds = new float[sides];
            for (int i = 0; i < sides; i++) RoundDir(i, sides, out dc[i], out ds[i]);
            var rn = RoundNoise(ref rng, sides, 0.6f, 0.7f);
            if (shape == RoundPool)
            {
                var u = new float[3 * (sides + 1)];
                lift = new float[sides + 1];
                idx = new int[3 * sides];
                u[1] = 1; lift[0] = 4;
                for (int i = 0; i < sides; i++)
                {
                    u[3 + 3 * i] = 0.97f * rn[i] * dc[i]; u[4 + 3 * i] = 1; u[5 + 3 * i] = 0.97f * rn[i] * ds[i];
                    lift[1 + i] = 7;
                    idx[3 * i] = 0; idx[3 * i + 1] = 1 + (i + 1) % sides; idx[3 * i + 2] = 1 + i;
                }
                return u;
            }
            var crest = RoundNoise(ref rng, sides, 0.06f, SpringCrest);
            var lobe = RoundNoise(ref rng, sides, 0f, 0.08f);
            var sag = RoundNoise(ref rng, sides, 0f, 0.25f);
            const int rings = 7;
            float span = SpringCrest + SpringDepth;
            var w = new float[3 * (1 + rings * sides)];
            lift = new float[1 + rings * sides];
            w[1] = 2 * (SpringDepth - 0.6f) / span - 1; lift[0] = 4;
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < sides; i++)
                {
                    float r, h;
                    switch (j)
                    {
                        case 0: r = 0.55f * rn[i]; h = -0.45f; break;
                        case 1: r = 0.92f * rn[i]; h = -0.08f; break;
                        case 2: r = rn[i]; h = crest[i]; break;
                        case 3: r = rn[i] + 0.05f; h = 0.5f * crest[i] - 0.03f; break;
                        case 4: r = rn[i] + 0.12f + 0.5f * lobe[i]; h = -0.4f - sag[i]; break;
                        case 5: r = rn[i] + 0.2f + lobe[i]; h = -1.1f - 0.5f * sag[i]; break;
                        default: r = 1f; h = -SpringDepth; break;
                    }
                    int v = 1 + j * sides + i;
                    w[3 * v] = r * dc[i]; w[3 * v + 1] = 2 * (h + SpringDepth) / span - 1; w[3 * v + 2] = r * ds[i];
                    lift[v] = 5 + j;
                }
            var id = new List<int>(3 * sides * (2 * rings - 1));
            for (int i = 0; i < sides; i++) { id.Add(0); id.Add(1 + (i + 1) % sides); id.Add(1 + i); }
            for (int j = 0; j + 1 < rings; j++)
                for (int i = 0; i < sides; i++)
                {
                    int a = 1 + j * sides + i, b = 1 + j * sides + (i + 1) % sides, c = b + sides, d = a + sides;
                    id.Add(a); id.Add(c); id.Add(d);
                    id.Add(a); id.Add(b); id.Add(c);
                }
            idx = id.ToArray();
            return w;
        }
    }
}
