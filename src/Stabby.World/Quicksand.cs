using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Stabby.World
{
    public sealed partial class ObjectField
    {
        public const int QuicksandPool = -3;
        public const float QuicksandChance = 0.8f, QuicksandSpreadM = 30f, QuicksandMinR = 1.5f, QuicksandMaxR = 5f;
        public const float QuicksandMinDepthM = 0.5f, QuicksandMaxDepthM = 0.6f, QuicksandRiverM = 3f, QuicksandWetM = 40f, QuicksandShoreM = 2f, QuicksandSteadyM = 0.1f, QuicksandDipM = 0.025f, QuicksandGapM = 0.025f;
        readonly object quicksandLock = new object();
        float[] quicksandPools;
        bool[] sandBiomes;

        public float[] QuicksandPools()
        {
            var q = Volatile.Read(ref quicksandPools);
            if (q != null) return q;
            lock (quicksandLock)
            {
                if (quicksandPools == null) Volatile.Write(ref quicksandPools, PlaceQuicksand());
                return quicksandPools;
            }
        }

        public bool SandAt(float x, float z)
        {
            if (w.Materials == null || w.Biome == null) return false;
            int c = Cell(z) * n + Cell(x);
            if (w.Sea[c] || w.Biome[c] < 0) return false;
            return SandBiomes()[w.Biome[c]] || GroundAt(detail, cm, x, z) <= QuicksandShoreM;
        }

        bool[] SandBiomes()
        {
            var s = Volatile.Read(ref sandBiomes);
            if (s != null) return s;
            s = new bool[w.BiomeIds.Length];
            for (int b = 0; b < s.Length; b++) s[b] = w.Materials.Surface.CoatOf(w.BiomeIds[b]) == SurfaceDetail.Sand;
            Volatile.Write(ref sandBiomes, s);
            return s;
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
            var near = WetNear((int)Math.Ceiling(QuicksandWetM / cm) + 1);
            var rows = new List<float>[n];
            Parallel.For(0, n, cz =>
            {
                List<float> row = null;
                for (int cx = 0; cx < n; cx++)
                {
                    float ox = (cx + 0.5f) * cm, oz = (cz + 0.5f) * cm;
                    if (!near[cz * n + cx] || w.Sea[cz * n + cx] || !SandAt(ox, oz) || !DampAt(ox, oz)) continue;
                    var rng = new Rng(Rng.Mix(stream ^ (ulong)(cz * n + cx + 1) * 0x9E3779B97F4A7C15UL));
                    int count = rng.Range(3, 8);
                    if (rng.NextFloat() >= QuicksandChance) continue;
                    for (int p = 0; p < count; p++)
                    {
                        float x = ox + rng.Range(-QuicksandSpreadM, QuicksandSpreadM), z = oz + rng.Range(-QuicksandSpreadM, QuicksandSpreadM);
                        float r = rng.Range(QuicksandMinR, QuicksandMaxR), depth = rng.Range(QuicksandMinDepthM, QuicksandMaxDepthM);
                        if (x < 0 || z < 0 || x >= worldM || z >= worldM) continue;
                        row ??= new List<float>();
                        row.Add(x); row.Add(z); row.Add(r); row.Add(depth);
                    }
                }
                rows[cz] = row;
            });
            var tries = new List<float>();
            foreach (var row in rows) if (row != null) tries.AddRange(row);
            var fits = new bool[tries.Count / 4];
            Parallel.For(0, fits.Length, t => fits[t] = DampAt(tries[4 * t], tries[4 * t + 1]) && SandRing(tries[4 * t], tries[4 * t + 1], tries[4 * t + 2]));
            for (int t = 0; t < fits.Length; t++)
                if (fits[t]) SoftAt(QuicksandPool, tries[4 * t], tries[4 * t + 1], tries[4 * t + 2], tries[4 * t + 3], c => true, QuicksandRiverM, QuicksandSteadyM, QuicksandDipM, true, keep, list);
            return list.ToArray();
        }

        bool[] WetNear(int reach)
        {
            var wet = new bool[n * n];
            for (int i = 0; i < wet.Length; i++)
            {
                wet[i] = !w.Sea[i] && w.Water.LakeId[i] >= 0;
                for (int k = blockStart[i]; !wet[i] && k < blockStart[i + 1]; k++) wet[i] = blockItems[k] >= 0;
            }
            var row = new bool[n * n];
            for (int z = 0; z < n; z++)
                for (int x = 0, last = -reach - 1; x < n + reach; x++)
                {
                    if (x < n && wet[z * n + x]) last = x;
                    int at = x - reach;
                    if (at >= 0) row[z * n + at] = x - last <= 2 * reach;
                }
            var near = new bool[n * n];
            for (int x = 0; x < n; x++)
                for (int z = 0, last = -reach - 1; z < n + reach; z++)
                {
                    if (z < n && row[z * n + x]) last = z;
                    int at = z - reach;
                    if (at >= 0) near[at * n + x] = z - last <= 2 * reach;
                }
            return near;
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
                list[e] = SoftEdit(b[k], b[k + 1], b[k + 2], b[k + 3], b[k + 4], b[k + 5] + BogHeadM, 0f).Finer();
            return list;
        }
    }
}
