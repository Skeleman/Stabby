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

        sealed class SiteIndex
        {
            public readonly List<ObjectInstance> Items = new List<ObjectInstance>();
            public ObjectInstance[] Order = Array.Empty<ObjectInstance>();
            public int[] Start = Array.Empty<int>();
            public float X0, Z0, Inv, Radius;
            public int Nx, Nz;
        }

        [ThreadStatic] static SiteIndex nearScratch;

        SiteIndex SitesNear(int cx, int cz)
        {
            if (siteChunks < 0) return null;
            var near = nearScratch ?? (nearScratch = new SiteIndex());
            var items = near.Items;
            items.Clear();
            for (int z = Math.Max(0, cz - siteChunks); z <= Math.Min(across - 1, cz + siteChunks); z++)
                for (int x = Math.Max(0, cx - siteChunks); x <= Math.Min(across - 1, cx + siteChunks); x++)
                    items.AddRange(Sites(x, z));
            if (items.Count == 0) return null;
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue, radius = 0;
            foreach (var s in items)
            {
                x0 = Math.Min(x0, s.X); x1 = Math.Max(x1, s.X);
                z0 = Math.Min(z0, s.Z); z1 = Math.Max(z1, s.Z);
                var box = siteBox[s.Def];
                int v = s.Variant * 4;
                float ex = Math.Max(Math.Abs(box[v]), Math.Abs(box[v + 1])), ez = Math.Max(Math.Abs(box[v + 2]), Math.Abs(box[v + 3]));
                radius = Math.Max(radius, (float)Math.Sqrt(ex * ex + ez * ez) * Math.Abs(s.Size));
            }
            float cell = Math.Max(Math.Max(16f, radius + SiteMarginM + SiteBandM), Math.Max(x1 - x0, z1 - z0) / 1024f);
            int nx = (int)((x1 - x0) / cell) + 1, nz = (int)((z1 - z0) / cell) + 1;
            near.X0 = x0; near.Z0 = z0; near.Inv = 1f / cell; near.Radius = radius; near.Nx = nx; near.Nz = nz;
            if (near.Start.Length < nx * nz + 1) near.Start = new int[nx * nz + 1];
            if (near.Order.Length < items.Count) near.Order = new ObjectInstance[items.Count];
            var start = near.Start;
            Array.Clear(start, 0, nx * nz + 1);
            foreach (var s in items) start[CellOf(near, s.X, s.Z) + 1]++;
            for (int i = 0; i < nx * nz; i++) start[i + 1] += start[i];
            foreach (var s in items) near.Order[start[CellOf(near, s.X, s.Z)]++] = s;
            for (int i = nx * nz; i > 0; i--) start[i] = start[i - 1];
            start[0] = 0;
            return near;
        }

        static int CellOf(SiteIndex n, float x, float z)
        {
            int ix = Math.Min(Math.Max((int)((x - n.X0) * n.Inv), 0), n.Nx - 1);
            int iz = Math.Min(Math.Max((int)((z - n.Z0) * n.Inv), 0), n.Nz - 1);
            return iz * n.Nx + ix;
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

        bool Cleared(SiteIndex near, float x, float z, float r)
        {
            if (near == null) return false;
            float gap = float.MaxValue;
            float reach = (r + SiteMarginM + SiteBandM + near.Radius + 1f) * 1.001f;
            int ix0 = (int)Math.Floor((x - reach - near.X0) * near.Inv), ix1 = (int)Math.Floor((x + reach - near.X0) * near.Inv);
            int iz0 = (int)Math.Floor((z - reach - near.Z0) * near.Inv), iz1 = (int)Math.Floor((z + reach - near.Z0) * near.Inv);
            if (ix1 < 0 || iz1 < 0 || ix0 >= near.Nx || iz0 >= near.Nz) return false;
            ix0 = Math.Max(ix0, 0); iz0 = Math.Max(iz0, 0); ix1 = Math.Min(ix1, near.Nx - 1); iz1 = Math.Min(iz1, near.Nz - 1);
            for (int iz = iz0; iz <= iz1; iz++)
                for (int k = near.Start[iz * near.Nx + ix0], e = near.Start[iz * near.Nx + ix1 + 1]; k < e; k++)
                    gap = Math.Min(gap, SiteGap(near.Order[k], x, z) - r);
            if (gap >= SiteMarginM + SiteBandM) return false;
            if (gap < SiteMarginM) return true;
            float t = (gap - SiteMarginM) / SiteBandM + 0.45f * Noise.Fbm(siteSeed, x / SiteNoiseM, z / SiteNoiseM, 2);
            float keep = Grid.Smoothstep(0.1f, 0.9f, t);
            float roll = Noise.Hash(siteSeed ^ 0x5bd1e995u, (int)Math.Floor(x * 8), (int)Math.Floor(z * 8)) * (1f / 4294967296f);
            return roll >= keep;
        }
    }
}
