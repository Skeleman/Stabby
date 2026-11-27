using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed partial class ObjectField
    {
        public const int VolcanoPool = -6;
        readonly object volcanoPoolLock = new object();
        LiquidPool[] volcanoPools;
        VolcanoSketch[] volcanoOwners;

        public LiquidPool[] VolcanoPools()
        {
            lock (volcanoPoolLock)
            {
                if (volcanoPools == null) BuildVolcanoPools();
                return volcanoPools;
            }
        }

        public VolcanoSketch VolcanoOf(LiquidPool p)
        {
            var pools = VolcanoPools();
            for (int k = 0; k < pools.Length; k++) if (pools[k] == p) return volcanoOwners[k];
            return null;
        }

        void BuildVolcanoPools()
        {
            var list = new List<LiquidPool>();
            var owners = new List<VolcanoSketch>();
            if (w.Sketch != null)
            {
                int water = w.Materials == null ? 0 : Math.Max(0, w.Materials.Liquid("water")), lava = w.Materials == null ? 0 : Math.Max(0, w.Materials.Liquid("lava"));
                foreach (var v in w.Sketch.Volcanoes)
                    foreach (var e in edits)
                    {
                        if (!Volcanoes.IsCrater(v, e)) continue;
                        float bottom = e.Ay - e.H - e.R, top = e.Ay + e.H + e.R;
                        var p = Pool(VolcanoPool, v.Active ? lava : water, v.X, v.Z, Volcanoes.CarveRadius(v) + Volcanoes.RingPadM, Math.Min(v.RimLow - Volcanoes.TopPadM, bottom + v.Fill * (top - Volcanoes.TopPadM - bottom)), bottom - PoolPadM, top + PoolPadM);
                        if (!p.Leaks && p.WetCount > 0 && p.Level - p.Floor >= PoolMinDepthM) { list.Add(p); owners.Add(v); }
                        break;
                    }
            }
            volcanoOwners = owners.ToArray();
            volcanoPools = list.ToArray();
        }

        public bool InCrater(float x, float z, float pad)
        {
            if (w.Sketch == null) return false;
            foreach (var v in w.Sketch.Volcanoes)
            {
                float gap = v.Rc * (1f + Volcanoes.CraterNoise) + pad;
                if ((v.X - x) * (v.X - x) + (v.Z - z) * (v.Z - z) < gap * gap) return true;
            }
            return false;
        }
    }
}
