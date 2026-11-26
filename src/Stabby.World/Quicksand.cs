using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed partial class ObjectField
    {
        public const int QuicksandPool = -3;
        public const float QuicksandChance = 0.8f, QuicksandSpreadM = 30f, QuicksandMinR = 1.5f, QuicksandMaxR = 5f;
        public const float QuicksandMinDepthM = 0.3f, QuicksandMaxDepthM = 0.5f, QuicksandRiverM = 3f, QuicksandWetM = 40f, QuicksandShoreM = 2f, QuicksandSteadyM = 1.2f;
        readonly object quicksandLock = new object();
        float[] quicksandPools;

        public float[] QuicksandPools()
        {
            lock (quicksandLock) return quicksandPools ?? (quicksandPools = PlaceQuicksand());
        }

        public bool SandAt(float x, float z)
        {
            if (w.Materials == null || w.Biome == null) return false;
            int c = Cell(z) * n + Cell(x);
            if (w.Sea[c] || w.Biome[c] < 0) return false;
            return w.Materials.Surface.CoatOf(w.BiomeIds[w.Biome[c]]) == SurfaceDetail.Sand || GroundAt(detail, cm, x, z) <= QuicksandShoreM;
        }

        public bool DampAt(float x, float z)
        {
            if (RiverGap(x, z) <= QuicksandWetM) return true;
            for (int p = 0; p < 8; p++)
            {
                FanDir(p, 0.70710678f, out float ux, out float uz);
                float px = x + ux * QuicksandWetM, pz = z + uz * QuicksandWetM;
                if (px < 0 || pz < 0 || px >= worldM || pz >= worldM) continue;
                int i = Cell(pz) * n + Cell(px);
                if (!w.Sea[i] && w.Water.LakeId[i] >= 0) return true;
            }
            return false;
        }

        float[] PlaceQuicksand()
        {
            var list = new List<float>();
            if (w.Materials == null || w.Biome == null) return list.ToArray();
            var keep = SoftKeep();
            var bogs = BogPools();
            for (int k = 0; k < bogs.Length; k += BogStride) { keep.Add(bogs[k]); keep.Add(bogs[k + 1]); keep.Add(BogReach(bogs[k + 2], bogs[k + 3], bogs[k + 5] + BogHeadM)); }
            ulong stream = Rng.Stream(w.Config.Seed, "quicksand-pools").NextU64();
            for (int cz = 0; cz < n; cz++)
                for (int cx = 0; cx < n; cx++)
                {
                    float ox = (cx + 0.5f) * cm, oz = (cz + 0.5f) * cm;
                    if (w.Sea[cz * n + cx] || !SandAt(ox, oz) || !DampAt(ox, oz)) continue;
                    var rng = new Rng(Rng.Mix(stream ^ (ulong)(cz * n + cx + 1) * 0x9E3779B97F4A7C15UL));
                    int count = rng.Range(3, 8);
                    if (rng.NextFloat() >= QuicksandChance) continue;
                    for (int p = 0; p < count; p++)
                    {
                        float x = ox + rng.Range(-QuicksandSpreadM, QuicksandSpreadM), z = oz + rng.Range(-QuicksandSpreadM, QuicksandSpreadM);
                        float r = rng.Range(QuicksandMinR, QuicksandMaxR), depth = rng.Range(QuicksandMinDepthM, QuicksandMaxDepthM);
                        if (x < 0 || z < 0 || x >= worldM || z >= worldM || !DampAt(x, z) || !SandRing(x, z, r)) continue;
                        SoftAt(QuicksandPool, x, z, r, depth, c => true, QuicksandRiverM, QuicksandSteadyM, keep, list);
                    }
                }
            return list.ToArray();
        }

        public float RimSpan(float x, float z, float r)
        {
            float low = float.MaxValue, high = float.MinValue;
            for (int p = 0; p < 8; p++)
            {
                FanDir(p, 0.70710678f, out float ux, out float uz);
                float g = GroundAt(detail, cm, x + ux * (r + BogLipM), z + uz * (r + BogLipM));
                low = Math.Min(low, g); high = Math.Max(high, g);
            }
            return high - low;
        }

        bool SandRing(float x, float z, float r)
        {
            if (!SandAt(x, z)) return false;
            for (int p = 0; p < 8; p++)
            {
                FanDir(p, 0.70710678f, out float ux, out float uz);
                float px = x + ux * (r + BogLipM), pz = z + uz * (r + BogLipM);
                if (px < 0 || pz < 0 || px >= worldM || pz >= worldM || !SandAt(px, pz)) return false;
            }
            return true;
        }

        bool NearQuicksand(FeatureEdit e)
        {
            var b = QuicksandPools();
            for (int k = 0; k < b.Length; k += BogStride)
            {
                float reach = BogReach(b[k + 2], b[k + 3], b[k + 5] + BogHeadM) + BogGapM;
                if (e.MinX < b[k] + reach && e.MaxX > b[k] - reach && e.MinZ < b[k + 1] + reach && e.MaxZ > b[k + 1] - reach) return true;
            }
            return false;
        }

        public FeatureEdit[] QuicksandEdits()
        {
            var b = QuicksandPools();
            var list = new FeatureEdit[b.Length / BogStride];
            for (int k = 0, e = 0; k < b.Length; k += BogStride, e++)
                list[e] = SoftEdit(b[k], b[k + 1], b[k + 2], b[k + 3], b[k + 4], b[k + 5] + BogHeadM, 0f);
            return list;
        }
    }
}
