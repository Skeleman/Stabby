using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed partial class ObjectField
    {
        public const int CavePool = -2;
        readonly object cavePoolLock = new object();
        LiquidPool[] cavePools;

        public LiquidPool[] CavePools()
        {
            lock (cavePoolLock) return cavePools ??= BuildCavePools();
        }

        LiquidPool[] BuildCavePools()
        {
            var rooms = new List<FeatureEdit>();
            foreach (int k in caveCarves)
                if (edits[k].Shape == FeatureEdit.CaveRoom) rooms.Add(edits[k]);
            var list = new List<LiquidPool>();
            if (rooms.Count == 0) return list.ToArray();
            int water = w.Materials == null ? 0 : Math.Max(0, w.Materials.Liquid("water"));
            foreach (var e in edits)
            {
                if (!Underground.PoolBasin(rooms, e)) continue;
                float top = e.Ay + e.H + e.R, floor = top - Underground.PoolTopM, reach = e.W - e.T * 2f * e.H + e.R * ((float)Math.Sqrt(1f + e.T * e.T) - 1f) + 2f * e.R;
                var p = Pool(CavePool, water, e.Ax, e.Az, reach, floor - Underground.PoolDipM, e.Ay - e.H - e.R - Underground.PoolSealM, top);
                if (!p.Leaks && p.WetCount > 0 && p.Level - p.Floor >= PoolMinDepthM) list.Add(p);
            }
            return list.ToArray();
        }
    }
}
