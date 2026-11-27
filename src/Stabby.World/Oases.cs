using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed partial class ObjectField
    {
        public const int OasisStride = 6, OasisTries = 12;
        public const float OasisCellM = 640f, OasisChance = 0.6f, OasisMinR = 5f, OasisMaxR = 10f, OasisMinDepthM = 0.7f, OasisMaxDepthM = 1.3f;
        public const float OasisRiverM = 80f, OasisSiteM = 30f, OasisRoadM = 12f, OasisTownM = 250f, OasisGapM = 40f, OasisSteadyM = 1.2f, OasisDipM = 0.15f, OasisTopM = 0.3f;
        public const float OasisRound = 0.4f, OasisBank = 2f, OasisLobe = 0.18f, OasisLumpM = 0.1f, OasisLipM = 1f, OasisHeadM = 1.2f, OasisBasinM = 36f, OasisSinkM = 0.25f;
        public const float OasisRingM = 12f, OasisTag = 1000f;
        readonly object oasisLock = new object();
        float[] oasisPools;

        public float[] OasisPools()
        {
            lock (oasisLock) return oasisPools ?? (oasisPools = PlaceOases());
        }

        public static float OasisReach(float r, float level, float top) => r + OasisBank * (top - OasisRound - level) + OasisRound;

        public static float OasisMargin(float r) => r + OasisRingM;

        bool OasisBiome(int c, int hot, int cold, int steppe)
        {
            int b = w.Biome[c];
            return b >= 0 && (b == hot || b == cold || b == steppe);
        }

        float[] PlaceOases()
        {
            var list = new List<float>();
            if (w.Biome == null || w.BiomeIds == null) return list.ToArray();
            int hot = Array.IndexOf(w.BiomeIds, "desert"), cold = Array.IndexOf(w.BiomeIds, "cold-desert"), steppe = Array.IndexOf(w.BiomeIds, "steppe");
            if (hot < 0 && cold < 0 && steppe < 0) return list.ToArray();
            var keep = new List<float>();
            var pools = new List<float>();
            foreach (var o in Discoveries())
            {
                if (!IsVent(o)) { keep.Add(o.X); keep.Add(o.Z); keep.Add(SiteRadius(o)); continue; }
                SpringPoolsAt(o, pools);
                for (int k = 0; k < pools.Count; k += SpringStride) { keep.Add(pools[k]); keep.Add(pools[k + 1]); keep.Add(pools[k + 3]); }
            }
            ulong stream = Rng.Stream(w.Config.Seed, "oasis-pools").NextU64();
            int cells = (int)Math.Ceiling(worldM / OasisCellM);
            var tx = new float[OasisTries];
            var tz = new float[OasisTries];
            var score = new float[OasisTries];
            for (int cz = 0; cz < cells; cz++)
                for (int cx = 0; cx < cells; cx++)
                {
                    var rng = new Rng(Rng.Mix(stream ^ (ulong)(cz * cells + cx + 1) * 0x9E3779B97F4A7C15UL));
                    float chance = rng.NextFloat(), r = rng.Range(OasisMinR, OasisMaxR), depth = rng.Range(OasisMinDepthM, OasisMaxDepthM);
                    for (int t = 0; t < OasisTries; t++)
                    {
                        tx[t] = (cx + rng.NextFloat()) * OasisCellM; tz[t] = (cz + rng.NextFloat()) * OasisCellM;
                        score[t] = OasisBasin(tx[t], tz[t], r, hot, cold, steppe);
                    }
                    if (chance >= OasisChance) continue;
                    for (int pass = 0; pass < OasisTries; pass++)
                    {
                        int best = -1;
                        for (int t = 0; t < OasisTries; t++) if (score[t] >= OasisSinkM && (best < 0 || score[t] > score[best])) best = t;
                        if (best < 0) break;
                        score[best] = float.NegativeInfinity;
                        if (OasisAt(tx[best], tz[best], r, depth, keep, list)) break;
                    }
                }
            return list.ToArray();
        }

        float OasisBasin(float x, float z, float r, int hot, int cold, int steppe)
        {
            float probe = OasisMargin(r) + OasisBasinM;
            if (x - probe < 0 || z - probe < 0 || x + probe >= worldM || z + probe >= worldM) return float.NegativeInfinity;
            int c = Cell(z) * n + Cell(x);
            if (!OasisBiome(c, hot, cold, steppe) || w.Sea[c] || w.Water.LakeId[c] >= 0) return float.NegativeInfinity;
            float centre = GroundAt(detail, cm, x, z), sum = 0f;
            for (int p = 0; p < 8; p++)
            {
                FanDir(p, 0.70710678f, out float ux, out float uz);
                float px = x + ux * OasisBasinM, pz = z + uz * OasisBasinM;
                int i = Cell(pz) * n + Cell(px);
                if (!OasisBiome(i, hot, cold, steppe) || w.Sea[i] || w.Water.LakeId[i] >= 0) return float.NegativeInfinity;
                sum += GroundAt(detail, cm, px, pz);
            }
            return sum / 8f - centre;
        }

        bool OasisAt(float x, float z, float r, float depth, List<float> keep, List<float> list)
        {
            float probe = OasisReach(r, 0f, OasisSteadyM + OasisDipM + OasisTopM + OasisHeadM);
            float low = GroundAt(detail, cm, x, z), high = low;
            for (int ring = 0; ring < 3; ring++)
                for (int p = 0; p < 8; p++)
                {
                    FanDir(p, 0.70710678f, out float ux, out float uz);
                    float rr = ring == 0 ? r + OasisLipM : ring == 1 ? probe : OasisRiverM, px = x + ux * rr, pz = z + uz * rr;
                    if (RiverGap(px, pz) <= OasisRiverM - rr) return false;
                    if (ring == 2) continue;
                    float g = GroundAt(detail, cm, px, pz);
                    low = Math.Min(low, g); high = Math.Max(high, g);
                }
            if (RiverGap(x, z) <= OasisRiverM || high - low > OasisSteadyM) return false;
            float level = low - OasisDipM, top = high + OasisTopM, rout = OasisReach(r, level, top + OasisHeadM), clear = OasisMargin(rout);
            foreach (var f in edits)
                if (f.Shape != FeatureEdit.Boulder && f.Shape != FeatureEdit.Spring && x + clear >= f.MinX && x - clear <= f.MaxX && z + clear >= f.MinZ && z - clear <= f.MaxZ) return false;
            for (int k = 0; k < keep.Count; k += 3)
            {
                float dx = keep[k] - x, dz = keep[k + 1] - z, gap = keep[k + 2] + clear + OasisSiteM;
                if (dx * dx + dz * dz < gap * gap) return false;
            }
            for (int k = 0; k < list.Count; k += OasisStride)
            {
                float dx = list[k] - x, dz = list[k + 1] - z, gap = OasisMargin(OasisReach(list[k + 2], list[k + 3], list[k + 5] + OasisHeadM)) + clear + OasisGapM;
                if (dx * dx + dz * dz < gap * gap) return false;
            }
            foreach (var t in Towns)
            {
                float dx = t.X * cm - x, dz = t.Y * cm - z;
                if (dx * dx + dz * dz < OasisTownM * OasisTownM) return false;
            }
            if (RoadSide(x, z, out _, out _) < clear + OasisRoadM || UnderMesa(x, z, clear)) return false;
            var basin = Pool(OasisPool, 0, x, z, rout, level, level - depth - OasisHeadM, top + OasisHeadM, new[] { OasisEdit(x, z, r, level, level - depth, top + OasisHeadM) });
            if (basin.Leaks || basin.WetCount == 0 || !(basin.Level - basin.Floor >= OasisMinDepthM * 0.5f)) return false;
            list.Add(x); list.Add(z); list.Add(r); list.Add(level); list.Add(level - depth); list.Add(top);
            return true;
        }

        public bool NearOasis(FeatureEdit e)
        {
            var b = OasisPools();
            for (int k = 0; k < b.Length; k += OasisStride)
            {
                float reach = OasisMargin(OasisReach(b[k + 2], b[k + 3], b[k + 5] + OasisHeadM));
                if (e.MinX < b[k] + reach && e.MaxX > b[k] - reach && e.MinZ < b[k + 1] + reach && e.MaxZ > b[k + 1] - reach) return true;
            }
            return false;
        }

        public FeatureEdit[] OasisEdits()
        {
            var b = OasisPools();
            var list = new FeatureEdit[b.Length / OasisStride];
            for (int k = 0, e = 0; k < b.Length; k += OasisStride, e++)
                list[e] = OasisEdit(b[k], b[k + 1], b[k + 2], b[k + 3], b[k + 4], b[k + 5] + OasisHeadM);
            return list;
        }

        public float OasisGround(FeatureEdit e, float x, float z)
        {
            var g = new DetailGround { Detail = detail };
            return Walk.Surface(g, new EditArray { Items = new[] { e } }, PoolSlopeM, x, z, e.MaxY, e.MinY);
        }

        static FeatureEdit OasisEdit(float x, float z, float r, float level, float floor, float top)
        {
            float seed = (float)((Rng.Mix(((ulong)(uint)(int)(x * 64f) << 32 | (uint)(int)(z * 64f)) ^ 0x0A515UL) >> 40) / 16777216.0);
            return Disc(FeatureEdit.Carve, x, z, top, floor, OasisReach(r, level, top) - OasisRound, OasisRound, -OasisBank, -OasisLobe, OasisLumpM, seed, -(r + OasisTag), 0);
        }

        public bool InOasis(float x, float z, float pad)
        {
            var b = OasisPools();
            for (int k = 0; k < b.Length; k += OasisStride)
            {
                float dx = x - b[k], dz = z - b[k + 1], reach = OasisMargin(OasisReach(b[k + 2], b[k + 3], b[k + 5] + OasisHeadM)) + pad;
                if (dx * dx + dz * dz < reach * reach) return true;
            }
            return false;
        }

        public float OasisFoot(float x, float z)
        {
            var b = OasisPools();
            for (int k = 0; k < b.Length; k += OasisStride)
            {
                float dx = x - b[k], dz = z - b[k + 1], reach = OasisMargin(OasisReach(b[k + 2], b[k + 3], b[k + 5] + OasisHeadM));
                if (dx * dx + dz * dz < reach * reach) return OasisGround(OasisEdit(b[k], b[k + 1], b[k + 2], b[k + 3], b[k + 4], b[k + 5] + OasisHeadM), x, z);
            }
            return GroundAt(detail, cm, x, z);
        }

        public static int OasisKind(string s) => s == "palm" ? OasisPalm : s == "reeds" ? OasisReeds : s == "grass" ? OasisGrass : s == "shrub" ? OasisShrub : 0;

        public const int OasisPalm = 1, OasisReeds = 2, OasisGrass = 3, OasisShrub = 4, OasisPalmTries = 8;
        public const float OasisPalmGapM = 3f, OasisPalmRiseM = 0.1f, OasisShrubRiseM = 0.1f;

        void EmitOasis(int d, int chunk, float x0, float z0, float sx, float sz, List<ObjectInstance> list, ref int ordinal)
        {
            var def = lib.Objects[d];
            var pl = def.Placement;
            int kind = OasisKind(pl.Oasis);
            var b = OasisPools();
            var palms = new List<float>();
            for (int k = 0, idx = 0; k < b.Length; k += OasisStride, idx++)
            {
                float ox = b[k], oz = b[k + 1], r = b[k + 2], level = b[k + 3], top = b[k + 5] + OasisHeadM;
                float outer = OasisMargin(OasisReach(r, level, top)) + 4f;
                if (ox + outer < x0 || ox - outer >= x0 + sx || oz + outer < z0 || oz - outer >= z0 + sz) continue;
                var e = OasisEdit(ox, oz, r, level, b[k + 4], top);
                var rng = new Rng(Rng.Mix(oasisStream ^ (ulong)(idx + 1) * 0x9E3779B97F4A7C15UL ^ key[d]));
                float lo, hi, inner, far;
                int count, tries = 1;
                if (kind == OasisPalm) { count = 3 + (int)(rng.NextU32() % 5u); tries = OasisPalmTries; inner = r + 1.5f; far = r + 0.8f * OasisRingM; lo = level + OasisPalmRiseM; hi = float.MaxValue; }
                else if (kind == OasisReeds) { count = (int)(1.6f * r); inner = r - 2f; far = r + 2f; lo = level - 0.35f; hi = level + 0.2f; }
                else if (kind == OasisGrass) { count = (int)(3f * r); inner = r - 0.5f; far = r + 4f; lo = level + 0.05f; hi = level + 0.6f; }
                else { count = 2 + (int)(rng.NextU32() % 4u); tries = 3; inner = r + 4f; far = r + OasisRingM + 3f; lo = level + OasisShrubRiseM; hi = float.MaxValue; }
                palms.Clear();
                for (int m = 0; m < count; m++)
                    for (int t = 0; t < tries; t++)
                    {
                        float ux = rng.Range(-1f, 1f), uz = rng.Range(-1f, 1f), at = rng.NextFloat(), grow = rng.NextFloat(), qa = rng.Range(-1f, 1f), qb = rng.Range(-1f, 1f);
                        int variant = (int)(rng.NextU32() % (uint)def.Variants);
                        float ul = (float)Math.Sqrt(ux * ux + uz * uz);
                        if (ul < 0.1f || ul > 1f) continue;
                        float rr = inner + (far - inner) * at, px = ox + ux / ul * rr, pz = oz + uz / ul * rr;
                        float y = OasisGround(e, px, pz);
                        if (y < lo || y > hi) continue;
                        if (kind == OasisPalm)
                        {
                            bool close = false;
                            for (int j = 0; j < palms.Count && !close; j += 2)
                            {
                                float dx = palms[j] - px, dz = palms[j + 1] - pz;
                                close = dx * dx + dz * dz < OasisPalmGapM * OasisPalmGapM;
                            }
                            if (close) continue;
                            palms.Add(px); palms.Add(pz);
                        }
                        if (px >= x0 && pz >= z0 && px < x0 + sx && pz < z0 + sz)
                        {
                            float size = pl.SizeMinM + (pl.SizeMaxM - pl.SizeMinM) * grow;
                            var o = new ObjectInstance { Def = d, Variant = variant, X = px, Y = y - pl.Sink * size, Z = pz, Size = size };
                            Yaw(qa, qb, false, ref o);
                            o.Id = ObjectIds.Make(chunk, d, ordinal++);
                            list.Add(o);
                        }
                        break;
                    }
            }
        }
    }
}
