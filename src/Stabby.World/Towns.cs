using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class TownPlan
    {
        public int Town, Landmark;
        public float X, Z, Ux, Uz, Radius, PlazaM, LaneM, LaneWidthM;
        public readonly List<ObjectInstance> Members = new List<ObjectInstance>();
    }

    public sealed partial class ObjectField
    {
        public const int TownTries = 24, TownRing = 8;
        public const float TownYardM = 8f, TownGapM = 1f, TownSetbackM = 1.5f, TownSlotM = 2f, TownJitterM = 0.8f, TownReachM = 0.85f * TownClearM;

        readonly object townLock = new object();
        TownPlan[] townPlans;
        Dictionary<int, List<ObjectInstance>> townChunks;
        bool[] townMember;

        public TownPlan[] TownPlans()
        {
            var found = townPlans;
            if (found != null) return found;
            lock (townLock)
            {
                if (townPlans != null) return townPlans;
                var plans = new List<TownPlan>();
                var byChunk = new Dictionary<int, List<ObjectInstance>>();
                float span = WorldChunks.Size * cm;
                if (lib.Towns.Count > 0)
                {
                    var marks = LandmarkTowns();
                    ulong root = Rng.Stream(w.Config.Seed, "towns").NextU64();
                    for (int k = 0; k < marks.Count; k++)
                    {
                        var plan = PlanTown(k, marks[k].X * cm, marks[k].Y * cm, new Rng(Rng.Mix(root ^ ((ulong)(k + 1) * 0x9E3779B97F4A7C15UL))), plans);
                        if (plan == null) continue;
                        plans.Add(plan);
                        foreach (var o in plan.Members)
                        {
                            int c = Math.Min(across - 1, (int)(o.Z / span)) * across + Math.Min(across - 1, (int)(o.X / span));
                            if (!byChunk.TryGetValue(c, out var bin)) byChunk[c] = bin = new List<ObjectInstance>();
                            bin.Add(o);
                        }
                    }
                }
                townChunks = byChunk;
                return townPlans = plans.ToArray();
            }
        }

        List<LandmarkSketch> LandmarkTowns()
        {
            var list = new List<LandmarkSketch>();
            foreach (var l in Towns) if (l.Kind == "town") list.Add(l);
            return list;
        }

        public bool IsTownMember(int def) => townMember != null && townMember[def];

        void InitTowns()
        {
            townMember = new bool[lib.Objects.Count];
            foreach (var t in lib.Towns)
                foreach (var s in t.Sites)
                {
                    int d = lib.Objects.FindIndex(o => o.Id == s.ObjectId);
                    if (d >= 0) townMember[d] = true;
                }
        }

        static float TownRadius(TownDef t) => Math.Max(t.PlazaM, t.LaneM) + 0.5f * t.LaneWidthM + TownSetbackM + TownYardM;

        TownPlan PlanTown(int landmark, float lx, float lz, Rng rng, List<TownPlan> placed)
        {
            int ti = (int)(rng.NextU32() % (uint)lib.Towns.Count);
            var t = lib.Towns[ti];
            float radius = TownRadius(t), best = float.MaxValue, bx = 0, bz = 0;
            for (int k = 0; k <= TownTries; k++)
            {
                float ox = rng.Range(-1f, 1f), oz = rng.Range(-1f, 1f);
                if (k > 0 && ox * ox + oz * oz > 1) continue;
                float x = lx + (k == 0 ? 0 : ox * t.SeekM), z = lz + (k == 0 ? 0 : oz * t.SeekM);
                float score = TownScore(t, x, z, radius, placed);
                if (score < best) { best = score; bx = x; bz = z; }
            }
            if (best == float.MaxValue) return null;
            float ux = rng.Range(-1f, 1f), uz = rng.Range(-1f, 1f), ul = (float)Math.Sqrt(ux * ux + uz * uz);
            if (ul < 1e-3f) { ux = 1; uz = 0; ul = 1; }
            ux /= ul; uz /= ul;
            var plan = new TownPlan { Town = ti, Landmark = landmark, X = bx, Z = bz, Ux = ux, Uz = uz, Radius = radius, PlazaM = t.PlazaM, LaneM = t.LaneM, LaneWidthM = t.LaneWidthM };
            float px = -uz, pz = ux;
            foreach (var s in t.Sites)
            {
                int d = lib.Objects.FindIndex(o => o.Id == s.ObjectId);
                int want = s.Min + (int)(rng.NextU32() % (uint)(s.Max - s.Min + 1));
                var pl = lib.Objects[d].Placement;
                if (s.Place == TownSite.Centre)
                {
                    float qa = rng.Range(-1f, 1f), qb = rng.Range(-1f, 1f);
                    if (want > 0) TryMember(plan, d, bx, bz, pl.SizeMinM, qa, qb);
                    continue;
                }
                var slots = new List<float>();
                if (s.Place == TownSite.Plaza)
                {
                    float r = footprint[d] * pl.SizeMaxM;
                    for (int side = -1; side <= 1; side += 2)
                        for (int a = -1; a <= 1; a++) { slots.Add(a * 0.7f * r); slots.Add(side * (t.PlazaM + 0.6f * r)); }
                }
                else
                {
                    float step = footprint[d] * 2 * pl.SizeMaxM + TownSlotM, back = 0.5f * t.LaneWidthM + TownSetbackM + 0.32f * pl.SizeMaxM;
                    for (float a = t.PlazaM + 0.5f * step; a <= t.LaneM; a += step)
                        for (int dir = -1; dir <= 1; dir += 2)
                            for (int side = -1; side <= 1; side += 2) { slots.Add(dir * a); slots.Add(side * back); }
                }
                for (int k = slots.Count / 2 - 1; k > 0; k--)
                {
                    int j = (int)(rng.NextU32() % (uint)(k + 1));
                    float a0 = slots[2 * k], a1 = slots[2 * k + 1];
                    slots[2 * k] = slots[2 * j]; slots[2 * k + 1] = slots[2 * j + 1];
                    slots[2 * j] = a0; slots[2 * j + 1] = a1;
                }
                int got = 0;
                for (int k = 0; k < slots.Count && got < want; k += 2)
                {
                    float along = slots[k] + rng.Range(-TownJitterM, TownJitterM), off = slots[k + 1] + Math.Sign(slots[k + 1]) * rng.Range(0f, TownJitterM);
                    float size = rng.Range(pl.SizeMinM, pl.SizeMaxM);
                    float mx = bx + ux * along + px * off, mz = bz + uz * along + pz * off;
                    float fx = s.Place == TownSite.Plaza ? bx - mx : -px * Math.Sign(off), fz = s.Place == TownSite.Plaza ? bz - mz : -pz * Math.Sign(off);
                    Facing(fx, fz, out float qa, out float qb);
                    if (TryMember(plan, d, mx, mz, size, qa, qb)) got++;
                }
            }
            return plan.Members.Count > 0 ? plan : null;
        }

        static void Facing(float fx, float fz, out float qa, out float qb)
        {
            float fl = (float)Math.Sqrt(fx * fx + fz * fz);
            if (fl < 1e-4f) { qa = 1; qb = 0; return; }
            float ux = fz / fl, uz = -fx / fl;
            qa = (float)Math.Sqrt(Math.Max(0, 0.5f * (1 + ux)));
            qb = (uz > 0 ? -1f : 1f) * (float)Math.Sqrt(Math.Max(0, 0.5f * (1 - ux)));
        }

        bool TryMember(TownPlan plan, int d, float x, float z, float size, float qa, float qb)
        {
            float r = footprint[d] * size;
            foreach (var m in plan.Members)
            {
                float dx = m.X - x, dz = m.Z - z, g = r + footprint[m.Def] * m.Size + TownGapM;
                if (dx * dx + dz * dz < g * g) return false;
            }
            if (!Accept(d, x, z, size, qa, qb, out var o, town: true)) return false; 
            o.Variant = (int)(Rng.Mix(key[d] ^ ((ulong)(uint)(int)Math.Floor(x * 4) << 32) ^ (uint)(int)Math.Floor(z * 4)) % (ulong)lib.Objects[d].Variants);
            plan.Members.Add(o);
            return true;
        }

        float TownScore(TownDef t, float x, float z, float radius, List<TownPlan> placed)
        {
            if (x - radius < 0 || z - radius < 0 || x + radius >= worldM || z + radius >= worldM) return float.MaxValue; 
            foreach (var p in placed)
            {
                float dx = p.X - x, dz = p.Z - z, g = p.Radius + radius + t.SeekM;
                if (dx * dx + dz * dz < g * g) return float.MaxValue;
            }
            int b = w.Biome[Cell(z) * n + Cell(x)];
            if (b < 0 || !t.Biomes.Contains(w.BiomeIds[b])) return float.MaxValue; 
            float y0 = GroundAt(detail, cm, x, z), lo = y0, hi = y0;
            for (int ring = 1; ring <= 2; ring++)
                for (int k = 0; k < TownRing; k++)
                {
                    FanDir(k, 0.70710678f, out float ux, out float uz);
                    float rx = x + ux * radius * ring * 0.5f, rz = z + uz * radius * ring * 0.5f;
                    int i = Cell(rz) * n + Cell(rx);
                    if (w.Sea[i] || w.Water.LakeId[i] >= 0) return float.MaxValue;
                    float g = GroundAt(detail, cm, rx, rz);
                    lo = Math.Min(lo, g); hi = Math.Max(hi, g);
                }
            if (hi - lo > t.ReliefM) return float.MaxValue; 
            float water = TownWater(x, z, t.WaterM);
            if (water > t.WaterM) return float.MaxValue; 
            foreach (var f in edits)
            {
                if (f.Shape == FeatureEdit.Boulder || f.Shape == FeatureEdit.Spring) continue;
                float ext = f.Crusts ? f.CrustExtent : 0f;
                float cx = Math.Max(Math.Min(f.MinX, f.Ax - ext), Math.Min(x, Math.Max(f.MaxX, f.Ax + ext)));
                float cz = Math.Max(Math.Min(f.MinZ, f.Az - ext), Math.Min(z, Math.Max(f.MaxZ, f.Az + ext)));
                if ((cx - x) * (cx - x) + (cz - z) * (cz - z) < radius * radius) return float.MaxValue;
            }
            return hi - lo + 0.02f * water;
        }

        public float TownWater(float x, float z, float reach)
        {
            float best = float.MaxValue;
            for (int p = 0; p < pax.Count; p++)
            {
                float ax = Math.Min(pax[p], pbx[p]), bx = Math.Max(pax[p], pbx[p]), az = Math.Min(paz[p], pbz[p]), bz = Math.Max(paz[p], pbz[p]);
                if (x < ax - reach || x > bx + reach || z < az - reach || z > bz + reach) continue;
                best = Math.Min(best, Math.Max(0, Distance(p, x, z) - phw[p]));
            }
            int cells = (int)Math.Ceiling(reach / cm), gx = Cell(x), gz = Cell(z);
            for (int dz = -cells; dz <= cells; dz++)
                for (int dx = -cells; dx <= cells; dx++)
                {
                    int ax = gx + dx, az = gz + dz;
                    if (ax < 0 || az < 0 || ax >= n || az >= n || w.Water.LakeId[az * n + ax] < 0) continue;
                    best = Math.Min(best, (float)Math.Sqrt(dx * dx + dz * dz) * cm);
                }
            return best;
        }

        public bool InTownGround(float x, float z, float r, bool whole)
        {
            foreach (var p in TownPlans())
            {
                float dx = x - p.X, dz = z - p.Z;
                float reach = p.Radius + r;
                if (dx * dx + dz * dz >= reach * reach) continue;
                if (whole) return true;
                float pr = p.PlazaM + r;
                if (dx * dx + dz * dz < pr * pr) return true;
                float along = Math.Max(-p.LaneM, Math.Min(p.LaneM, dx * p.Ux + dz * p.Uz)), ex = dx - along * p.Ux, ez = dz - along * p.Uz, hw = 0.5f * p.LaneWidthM + r;
                if (ex * ex + ez * ez < hw * hw) return true;
            }
            return false;
        }

        void EmitTown(int d, int chunk, List<ObjectInstance> list, ref int ordinal)
        {
            TownPlans();
            if (!townChunks.TryGetValue(chunk, out var bin)) return;
            foreach (var m in bin)
            {
                if (m.Def != d) continue;
                var o = m;
                o.Id = ObjectIds.Make(chunk, d, ordinal++);
                list.Add(o);
            }
        }
    }
}
