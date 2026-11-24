using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed partial class ObjectField
    {
        public const int BogStride = 6;
        public const float BogCellM = 180f, BogChance = 0.55f, BogSpreadM = 16f, BogMinR = 3f, BogMaxR = 7f, BogMinDepthM = 0.35f, BogMaxDepthM = 0.6f;
        public const float BogRiverM = 6f, BogSiteM = 12f, BogRoadM = 6f, BogTownM = 200f, BogGapM = 2f, BogSteadyM = 0.8f, BogDipM = 0.12f, BogTopM = 0.3f;
        public const float BogRound = 0.3f, BogBank = 1.5f, BogLobe = 0.15f, BogLumpM = 0.12f, BogLipM = 0.5f, BogHeadM = 1.2f, BogPeatM = 0.8f, BogPeatFadeM = 0.6f;
        readonly object bogLock = new object();
        float[] bogPools;

        public float[] BogPools()
        {
            lock (bogLock) return bogPools ?? (bogPools = PlaceBogs());
        }

        public static float BogMargin(float r) => r + BogPeatM;

        public static float BogReach(float r, float level, float top) => r + BogBank * (top - BogRound - level) + BogRound;

        float[] PlaceBogs()
        {
            var list = new List<float>();
            int wet = w.BiomeIds == null ? -1 : Array.IndexOf(w.BiomeIds, "wetland");
            if (w.Biome == null || wet < 0) return list.ToArray();
            var keep = new List<float>();
            var pools = new List<float>();
            foreach (var o in Discoveries())
            {
                if (!IsVent(o)) { keep.Add(o.X); keep.Add(o.Z); keep.Add(SiteRadius(o)); continue; }
                SpringPoolsAt(o, pools);
                for (int k = 0; k < pools.Count; k += SpringStride) { keep.Add(pools[k]); keep.Add(pools[k + 1]); keep.Add(pools[k + 3]); }
            }
            ulong stream = Rng.Stream(w.Config.Seed, "bog-pools").NextU64();
            int cells = (int)Math.Ceiling(worldM / BogCellM);
            for (int cz = 0; cz < cells; cz++)
                for (int cx = 0; cx < cells; cx++)
                {
                    var rng = new Rng(Rng.Mix(stream ^ (ulong)(cz * cells + cx + 1) * 0x9E3779B97F4A7C15UL));
                    float chance = rng.NextFloat(), ox = (cx + rng.NextFloat()) * BogCellM, oz = (cz + rng.NextFloat()) * BogCellM;
                    int count = rng.Range(1, 4);
                    if (chance >= BogChance || ox >= worldM || oz >= worldM || w.Biome[Cell(oz) * n + Cell(ox)] != wet) continue;
                    for (int p = 0; p < count; p++)
                    {
                        float x = ox + rng.Range(-BogSpreadM, BogSpreadM), z = oz + rng.Range(-BogSpreadM, BogSpreadM), r = rng.Range(BogMinR, BogMaxR), depth = rng.Range(BogMinDepthM, BogMaxDepthM);
                        BogAt(x, z, r, depth, wet, keep, list);
                    }
                }
            return list.ToArray();
        }

        void BogAt(float x, float z, float r, float depth, int wet, List<float> keep, List<float> list)
        {
            float probe = BogReach(r, 0f, BogSteadyM + BogDipM + BogTopM + BogHeadM);
            if (x - probe < 0 || z - probe < 0 || x + probe >= worldM || z + probe >= worldM || w.Biome[Cell(z) * n + Cell(x)] != wet) return;
            int c = Cell(z) * n + Cell(x);
            if (w.Sea[c] || w.Water.LakeId[c] >= 0 || RiverGap(x, z) <= BogRiverM) return;
            float low = GroundAt(detail, cm, x, z), high = low;
            for (int ring = 0; ring < 2; ring++)
                for (int p = 0; p < 8; p++)
                {
                    FanDir(p, 0.70710678f, out float ux, out float uz);
                    float rr = ring == 0 ? r + BogLipM : probe, px = x + ux * rr, pz = z + uz * rr;
                    int i = Cell(pz) * n + Cell(px);
                    if (w.Sea[i] || w.Water.LakeId[i] >= 0 || RiverGap(px, pz) <= BogRiverM) return;
                    float g = GroundAt(detail, cm, px, pz);
                    low = Math.Min(low, g); high = Math.Max(high, g);
                }
            if (high - low > BogSteadyM) return;
            float level = low - BogDipM, top = high + BogTopM, rout = BogReach(r, level, top + BogHeadM);
            foreach (var f in edits)
                if (f.Shape != FeatureEdit.Boulder && f.Shape != FeatureEdit.Spring && x + rout >= f.MinX && x - rout <= f.MaxX && z + rout >= f.MinZ && z - rout <= f.MaxZ) return;
            for (int k = 0; k < keep.Count; k += 3)
            {
                float dx = keep[k] - x, dz = keep[k + 1] - z, gap = keep[k + 2] + rout + BogSiteM;
                if (dx * dx + dz * dz < gap * gap) return;
            }
            for (int k = 0; k < list.Count; k += BogStride)
            {
                float dx = list[k] - x, dz = list[k + 1] - z, gap = BogReach(list[k + 2], list[k + 3], list[k + 5] + BogHeadM) + rout + BogGapM;
                if (dx * dx + dz * dz < gap * gap) return;
            }
            foreach (var t in Towns)
            {
                float dx = t.X * cm - x, dz = t.Y * cm - z;
                if (dx * dx + dz * dz < BogTownM * BogTownM) return;
            }
            if (RoadSide(x, z, out _, out _) < rout + BogRoadM || UnderMesa(x, z, rout)) return;
            var basin = Pool(BogPool, 0, x, z, rout, level, level - depth - BogHeadM, top + BogHeadM, new[] { BogEdit(x, z, r, level, level - depth, top + BogHeadM) });
            if (basin.Leaks || basin.WetCount == 0 || !(basin.Level - basin.Floor >= PoolMinDepthM)) return;
            list.Add(x); list.Add(z); list.Add(r); list.Add(level); list.Add(level - depth); list.Add(top);
        }

        bool NearBog(FeatureEdit e)
        {
            var b = BogPools();
            for (int k = 0; k < b.Length; k += BogStride)
            {
                float reach = BogReach(b[k + 2], b[k + 3], b[k + 5] + BogHeadM) + BogGapM;
                if (e.MinX < b[k] + reach && e.MaxX > b[k] - reach && e.MinZ < b[k + 1] + reach && e.MaxZ > b[k + 1] - reach) return true;
            }
            return false;
        }

        public FeatureEdit[] BogEdits()
        {
            var b = BogPools();
            var list = new FeatureEdit[b.Length / BogStride];
            for (int k = 0, e = 0; k < b.Length; k += BogStride, e++)
                list[e] = BogEdit(b[k], b[k + 1], b[k + 2], b[k + 3], b[k + 4], b[k + 5] + BogHeadM);
            return list;
        }

        static FeatureEdit BogEdit(float x, float z, float r, float level, float floor, float top)
        {
            float seed = (float)((Rng.Mix(((ulong)(uint)(int)(x * 64f) << 32 | (uint)(int)(z * 64f)) ^ 0xB0C5UL) >> 40) / 16777216.0);
            return Disc(FeatureEdit.Carve, x, z, top, floor, BogReach(r, level, top) - BogRound, BogRound, -BogBank, -BogLobe, BogLumpM, seed, -r, 0);
        }
    }
}
