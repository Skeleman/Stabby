using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static class Cenotes
    {
        public const float MinD = 6f, MaxD = 15f, MinDrop = 8f, MaxDrop = 20f, MinPool = 8f, MaxPool = 14f, MinLean = 1.5f, MaxLean = 3f, MinGrade = 0.6f, MaxGrade = 0.7f;
        public const float ReliefM = 6f, GapM = 1500f, StepM = 48f, ClearM = 12f, DryM = 40f, HeadM = 1f, SealM = 1f, RimM = 1f, Blend = 3f;
        public const int Tries = 400;
        public static readonly string[] Biomes = { "temperate-forest", "temperate-rain", "shrubland", "savanna", "tropical-forest" };
        static readonly float[] Cos = { 1f, 0.9238795f, 0.7071068f, 0.3826834f, 0f, -0.3826834f, -0.7071068f, -0.9238795f, -1f, -0.9238795f, -0.7071068f, -0.3826834f, 0f, 0.3826834f, 0.7071068f, 0.9238795f };

        public static float Reach => MaxD * (1f + FeatureEdit.CenoteLobe) + MaxLean + FeatureEdit.CenoteRoughM + FeatureEdit.CenoteGrainM + FeatureEdit.CenoteLedgeM + Blend + 1f;

        public static void Place(GeneratedWorld w, TerrainDetail detail, List<FeatureEdit> list)
        {
            if (detail == null || w.Height == null || w.Biome == null) return;
            var r = Rng.Stream(w.Config.Seed, "cenotes");
            ulong seed = r.NextU64();
            int n = w.Config.Size, want = n >= 1024 ? r.Range(2, 5) : 1, tc = Features.TileCells, tiles = n / tc;
            float cm = w.Config.CellMeters;
            var biomes = Landforms.BiomeMask(w, Biomes);
            int reach = Math.Max(1, (int)Math.Ceiling(Reach / cm)), pad = Landforms.Pad(w, Reach), step = Math.Max(1, (int)Math.Round(StepM / cm));
            int dry = reach + Math.Max(2, (int)Math.Ceiling(DryM / cm));
            var picks = new List<KeyValuePair<float, int>>();
            for (int ty = 0; ty < tiles; ty++)
                for (int tx = 0; tx < tiles; tx++)
                    for (int y = ty * tc + pad; y < (ty + 1) * tc - pad; y += step)
                        for (int x = tx * tc + pad; x < (tx + 1) * tc - pad; x += step)
                        {
                            int i = y * n + x, b = w.Biome[i];
                            if (b < 0 || b >= biomes.Length || !biomes[b] || !Features.Dry(w, i)) continue;
                            float lo = float.MaxValue, hi = float.MinValue;
                            for (int dy = -reach; dy <= reach; dy++)
                                for (int dx = -reach; dx <= reach; dx++)
                                {
                                    float h = w.Height[i + dy * n + dx];
                                    lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                                }
                            if (hi - lo > ReliefM) continue;
                            var q = new Rng(Rng.Mix(seed + (ulong)i));
                            picks.Add(new KeyValuePair<float, int>(0.5f * q.NextFloat() - (hi - lo) / ReliefM, i));
                        }
            picks.Sort((a, b) => b.Key.CompareTo(a.Key) != 0 ? b.Key.CompareTo(a.Key) : a.Value.CompareTo(b.Value));
            var placed = new List<FeatureEdit>();
            for (int k = 0; k < picks.Count && k < Tries && placed.Count < want; k++)
            {
                int i = picks[k].Value, x = i % n, y = i / n;
                float px = x * cm, pz = y * cm;
                bool near = false;
                foreach (var c in placed) if ((c.Ax - px) * (c.Ax - px) + (c.Az - pz) * (c.Az - pz) < GapM * GapM) near = true;
                if (near || Landforms.Crowded(w, px, pz, Reach) || !Landforms.DryAround(w, x, y, dry)) continue;
                var q = new Rng(Rng.Mix(seed ^ (ulong)i * 0x9E3779B97F4A7C15UL));
                var e = Build(detail, px, pz, cm, ref q);
                if (e.Ay - e.F > ReliefM + 2f || !Features.Inside(e, cm) || Clashes(e, list)) continue;
                list.Add(e);
                placed.Add(e);
            }
        }

        static FeatureEdit Build(TerrainDetail detail, float cx, float cz, float cm, ref Rng q)
        {
            float d = q.Range(MinD, MaxD), drop = q.Range(MinDrop, MaxDrop), pool = q.Range(MinPool, MaxPool), lean = q.Range(MinLean, MaxLean), grade = q.Range(MinGrade, MaxGrade), s = q.NextFloat();
            var e = new FeatureEdit { Kind = FeatureEdit.Carve, Shape = FeatureEdit.Cenote, R = Blend, D = d, N = lean, T = grade, S = s, Ax = cx, Az = cz };
            float wall = e.CenoteWall + Blend, rim = float.MaxValue, hi = float.MinValue, low = float.MaxValue, ux = 1f, uz = 0f;
            for (int k = 0; k < Cos.Length; k++)
            {
                float c = Cos[k], sn = Cos[(k + 12) % Cos.Length];
                float g = Ground(detail, cx + (d + RimM) * c, cz + (d + RimM) * sn, cm);
                rim = Math.Min(rim, g);
                if (g < low) { low = g; ux = c; uz = sn; }
                for (int ring = 0; ring <= 4; ring++) hi = Math.Max(hi, Ground(detail, cx + wall * ring / 4f * c, cz + wall * ring / 4f * sn, cm));
            }
            e.F = rim;
            e.P = rim - drop;
            e.By = e.P - pool;
            float len = Run(e, hi + 2f);
            for (float u = 0f; u <= len + Blend; u += 2f) hi = Math.Max(hi, Ground(detail, cx + ux * u, cz + uz * u, cm));
            e.Ay = hi + 2f;
            len = Run(e, e.Ay);
            e.Bx = cx + ux * len;
            e.Bz = cz + uz * len;
            return e;
        }

        static float Run(FeatureEdit e, float top) => FeatureEdit.CenoteFoot * e.D + (top - e.P + FeatureEdit.CenoteToeM) / e.T + 2f;

        static float Ground(TerrainDetail detail, float x, float z, float cm) => detail.HeightAt(x / cm, z / cm);

        static bool Clashes(FeatureEdit e, List<FeatureEdit> list)
        {
            foreach (var f in list)
                if (e.MinX - ClearM < f.MaxX && e.MaxX + ClearM > f.MinX && e.MinZ - ClearM < f.MaxZ && e.MaxZ + ClearM > f.MinZ) return true;
            return false;
        }
    }

    public sealed partial class ObjectField
    {
        public const int CenotePool = -5;
        readonly object cenotePoolLock = new object();
        LiquidPool[] cenotePools;

        public LiquidPool[] CenotePools()
        {
            lock (cenotePoolLock) return cenotePools ??= BuildCenotePools();
        }

        void EmitCenote(int d, int chunk, float x0, float z0, float sx, float sz, List<ObjectInstance> list, ref int ordinal)
        {
            var def = lib.Objects[d];
            var pl = def.Placement;
            var ground = new DetailGround { Detail = detail };
            for (int k = 0; k < edits.Length; k++)
            {
                var e = edits[k];
                if (e.Shape != FeatureEdit.Cenote || e.MaxX < x0 || e.MinX >= x0 + sx || e.MaxZ < z0 || e.MinZ >= z0 + sz) continue;
                e.CenoteAxis(out float ux, out float uz);
                float wet = FeatureEdit.CenoteFoot * e.D + FeatureEdit.CenoteToeM / e.T, run = e.D - wet;
                var rng = new Rng(Rng.Mix(cenoteStream ^ (ulong)(k + 1) * 0x9E3779B97F4A7C15UL ^ key[d]));
                for (int m = 0; m < pl.PerCenote; m++)
                {
                    float a = rng.NextFloat(), b = rng.Range(-1f, 1f), grow = rng.NextFloat(), qa = rng.Range(-1f, 1f), qb = rng.Range(-1f, 1f);
                    int variant = (int)(rng.NextU32() % (uint)def.Variants);
                    float du = run * (1f - a * a), u = e.D - du, v = b * FeatureEdit.CenoteSpread * du * 0.8f;
                    float px = e.Ax + ux * u - uz * v, pz = e.Az + uz * u + ux * v;
                    if (px < x0 || pz < z0 || px >= x0 + sx || pz >= z0 + sz) continue;
                    float y = Walk.Surface(ground, RockAt(px, pz), Features.LatticeStepM, px, pz, e.Ay, e.P);
                    if (!(y > e.P + 0.3f) || Math.Abs(y - e.CenoteRamp(u, v)) > 1f) continue;
                    float size = pl.SizeMinM + (pl.SizeMaxM - pl.SizeMinM) * grow;
                    var o = new ObjectInstance { Def = d, Variant = variant, X = px, Y = y - pl.Sink * size, Z = pz, Size = size };
                    Yaw(qa, qb, false, ref o);
                    o.Id = ObjectIds.Make(chunk, d, ordinal++);
                    list.Add(o);
                }
            }
        }

        public bool CenoteFoot(float x, float z, out float foot)
        {
            foot = 0f;
            foreach (var e in edits)
            {
                if (e.Shape != FeatureEdit.Cenote || x < e.MinX || x > e.MaxX || z < e.MinZ || z > e.MaxZ || !e.Under(x, z)) continue;
                foot = Walk.Surface(new DetailGround { Detail = detail }, RockAt(x, z), Features.LatticeStepM, x, z, e.Ay, e.By);
                return true;
            }
            return false;
        }

        LiquidPool[] BuildCenotePools()
        {
            var list = new List<LiquidPool>();
            int water = w.Materials == null ? 0 : Math.Max(0, w.Materials.Liquid("water"));
            foreach (var e in edits)
            {
                if (e.Shape != FeatureEdit.Cenote) continue;
                var p = Pool(CenotePool, water, e.Ax, e.Az, e.CenoteWall, e.P, e.By - Cenotes.SealM, e.P + Cenotes.HeadM);
                if (!p.Leaks && p.WetCount > 0 && p.Level - p.Floor >= PoolMinDepthM) list.Add(p);
            }
            return list.ToArray();
        }
    }
}
