using System;
using System.Collections.Generic;
using System.Threading;

namespace Stabby.World
{
    public sealed partial class ObjectField
    {
        public const float SiteMarginM = 2f, SiteBandM = 6f, SiteNoiseM = 5f, BoulderReach = 1.25f;

        float[][] siteBox;
        float siteReach;
        int siteChunks;
        uint siteSeed;
        ObjectInstance[][] sites;
        object[] siteGates;
        int siteComputes;

        void InitSites()
        {
            int defs = lib.Objects.Count;
            siteBox = new float[defs][];
            sites = new ObjectInstance[across * across][];
            siteGates = new object[across * across];
            siteSeed = Rng.Stream(w.Config.Seed, "site-clearing").NextU32();
            float natural = 0;
            for (int d = 0; d < defs; d++)
            {
                var def = lib.Objects[d];
                if (seats[d] == null)
                {
                    natural = Math.Max(natural, BoulderReach * footprint[d] * Math.Max(def.Placement.SizeMaxM, 0));
                    continue;
                }
                if (def.Placement.PerHectare <= 0 && def.Placement.Discovery <= 0) continue;
                var box = new float[def.Variants * 4];
                for (int v = 0; v < def.Variants; v++)
                {
                    float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                    for (int pi = 0; pi < def.Parts.Count; pi++)
                    {
                        var p = def.Parts[pi];
                        var cols = shapes.Columns[d][pi];
                        if (cols == null) continue;
                        float rs = Math.Min(p.ScaleX, p.ScaleZ);
                        foreach (var c in cols[v])
                        {
                            float cx = p.OffsetX + c.X * p.ScaleX, cz = p.OffsetZ + c.Z * p.ScaleZ, r = c.R * rs;
                            x0 = Math.Min(x0, cx - r); x1 = Math.Max(x1, cx + r);
                            z0 = Math.Min(z0, cz - r); z1 = Math.Max(z1, cz + r);
                        }
                    }
                    if (x0 > x1) { x0 = z0 = -footprint[d]; x1 = z1 = footprint[d]; }
                    if (Pooled(def.Placement.Discovery))
                    {
                        var pools = new List<float>();
                        LocalPools(def.Placement.Discovery, ObjectMeshes.Key(def.Id, 0, v), pools);
                        for (int k = 0; k < pools.Count; k += Structures.PoolStride)
                        {
                            float px = pools[k] * Structures.SpringU, pz = pools[k + 1] * Structures.SpringU, pr = pools[k + 3] * Structures.SpringU;
                            x0 = Math.Min(x0, px - pr); x1 = Math.Max(x1, px + pr);
                            z0 = Math.Min(z0, pz - pr); z1 = Math.Max(z1, pz + pr);
                        }
                    }
                    box[v * 4] = x0; box[v * 4 + 1] = x1; box[v * 4 + 2] = z0; box[v * 4 + 3] = z1;
                    float ex = Math.Max(-x0, x1), ez = Math.Max(-z0, z1);
                    siteReach = Math.Max(siteReach, (float)Math.Sqrt(ex * ex + ez * ez) * def.Placement.SizeMaxM);
                }
                siteBox[d] = box;
            }
            float pad = siteReach + SiteMarginM + SiteBandM + natural;
            siteChunks = siteReach > 0 ? (int)Math.Ceiling(pad / (WorldChunks.Size * cm)) : -1;
        }

        public bool IsSite(int def) => siteBox[def] != null;

        public int SiteComputes => siteComputes;

        public ObjectInstance[] Sites(int cx, int cz)
        {
            int c = cz * across + cx;
            var s = Volatile.Read(ref sites[c]);
            if (s != null) return s;
            var gate = Volatile.Read(ref siteGates[c]) ?? Interlocked.CompareExchange(ref siteGates[c], new object(), null) ?? siteGates[c];
            lock (gate)
            {
                s = sites[c];
                if (s != null) return s;
                var list = new List<ObjectInstance>();
                PlaceInto(cx, cz, false, list, null, true);
                Interlocked.Increment(ref siteComputes);
                s = list.ToArray();
                Volatile.Write(ref sites[c], s);
                return s;
            }
        }

        [ThreadStatic] static List<ObjectInstance> nearScratch;

        List<ObjectInstance> SitesNear(int cx, int cz)
        {
            if (siteChunks < 0) return null;
            var near = nearScratch ?? (nearScratch = new List<ObjectInstance>());
            near.Clear();
            for (int z = Math.Max(0, cz - siteChunks); z <= Math.Min(across - 1, cz + siteChunks); z++)
                for (int x = Math.Max(0, cx - siteChunks); x <= Math.Min(across - 1, cx + siteChunks); x++)
                    near.AddRange(Sites(x, z));
            return near.Count > 0 ? near : null;
        }

        public float SiteGap(ObjectInstance s, float x, float z)
        {
            var box = siteBox[s.Def];
            int v = s.Variant * 4;
            var inv = s;
            inv.Qx = -s.Qx; inv.Qy = -s.Qy; inv.Qz = -s.Qz;
            Rotate(inv, x - s.X, 0, z - s.Z, out float lx, out _, out float lz);
            float dx = Math.Max(Math.Max(box[v] * s.Size - lx, lx - box[v + 1] * s.Size), 0);
            float dz = Math.Max(Math.Max(box[v + 2] * s.Size - lz, lz - box[v + 3] * s.Size), 0);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        readonly object footLock = new object();
        float[][] siteFeet;

        bool InSiteFoot(FeatureEdit e, float x0, float z0)
        {
            float span = WorldChunks.Size * cm;
            var feet = SiteFeet()[(int)(z0 / span + 0.5f) * across + (int)(x0 / span + 0.5f)];
            if (feet == null) return false;
            for (int k = 0; k < feet.Length; k += 4)
                if (e.MaxX >= feet[k] && e.MinX <= feet[k + 2] && e.MaxZ >= feet[k + 1] && e.MinZ <= feet[k + 3]) return true;
            return false;
        }

        float[][] SiteFeet()
        {
            lock (footLock)
            {
                if (siteFeet != null) return siteFeet;
                float span = WorldChunks.Size * cm;
                var bins = new List<float>[across * across];
                foreach (var o in Discoveries())
                {
                    int kind = lib.Objects[o.Def].Placement.Discovery;
                    if (kind == WolfDen || Pooled(kind)) continue;
                    float r = footprint[o.Def] * o.Size;
                    int ax = Math.Max(0, (int)((o.X - r) / span)), bx = Math.Min(across - 1, (int)((o.X + r) / span));
                    int az = Math.Max(0, (int)((o.Z - r) / span)), bz = Math.Min(across - 1, (int)((o.Z + r) / span));
                    for (int cz = az; cz <= bz; cz++)
                        for (int cx = ax; cx <= bx; cx++)
                        {
                            var bin = bins[cz * across + cx] ?? (bins[cz * across + cx] = new List<float>());
                            bin.Add(o.X - r); bin.Add(o.Z - r); bin.Add(o.X + r); bin.Add(o.Z + r);
                        }
                }
                var feet = new float[bins.Length][];
                for (int k = 0; k < bins.Length; k++) feet[k] = bins[k]?.ToArray();
                return siteFeet = feet;
            }
        }

        bool Cleared(List<ObjectInstance> near, float x, float z, float r)
        {
            if (near == null) return false;
            float gap = float.MaxValue;
            foreach (var s in near) gap = Math.Min(gap, SiteGap(s, x, z) - r);
            if (gap >= SiteMarginM + SiteBandM) return false;
            if (gap < SiteMarginM) return true;
            float t = (gap - SiteMarginM) / SiteBandM + 0.45f * Noise.Fbm(siteSeed, x / SiteNoiseM, z / SiteNoiseM, 2);
            float keep = Grid.Smoothstep(0.1f, 0.9f, t);
            float roll = Noise.Hash(siteSeed ^ 0x5bd1e995u, (int)Math.Floor(x * 8), (int)Math.Floor(z * 8)) * (1f / 4294967296f);
            return roll >= keep;
        }
    }
}
