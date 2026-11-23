using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class WorldChecks
    {
        public const float TintTolerance = 1e-3f;

        static void CheckTint(GeneratedWorld w, List<string> e)
        {
            if (w.Biome == null || w.Materials == null || w.Materials.TintSlots().Length == 0) return;
            var hy = w.Water;
            int slots = w.Materials.TintSlots().Length;
            foreach (var lake in hy.Lakes)
                if (!InRange(lake.Mix, slots)) { e.Add($"lake {lake.Id} tint mix out of range"); break; }
            foreach (var s in hy.Rivers)
            {
                if (s.Mix.Count != s.Cells.Count) { e.Add($"river {s.Id} has {s.Mix.Count} tint mixes for {s.Cells.Count} cells"); break; }
                bool bad = false;
                for (int k = 0; k < s.Cells.Count && !bad; k++)
                {
                    int i = s.Cells[k];
                    if (!InRange(s.Mix[k], slots)) { e.Add($"river {s.Id} tint mix out of range at point {k}"); bad = true; }
                    else if (hy.LakeId[i] >= 0 && !Same(s.Mix[k], hy.Lakes[hy.LakeId[i]].Mix)) { e.Add($"river {s.Id} enters lake {hy.LakeId[i]} with a different tint"); bad = true; }
                    else if (k > 0 && !w.Sea[i] && hy.LakeId[i] < 0)
                    {
                        float q = hy.DischargeM3s[i], joined = q > 0 ? Math.Max(0, q - hy.DischargeM3s[s.Cells[k - 1]]) / q : 0;
                        for (int t = 0; t < slots && !bad; t++)
                        {
                            float jump = Math.Abs(s.Mix[k][t] - s.Mix[k - 1][t]);
                            if (jump > joined + TintTolerance) { e.Add($"river {s.Id} tint jumps {jump:F3} at point {k}, inflow allows {joined:F3}"); bad = true; }
                        }
                    }
                }
                if (bad) break;
            }
        }

        static bool InRange(WaterMix m, int slots)
        {
            for (int t = 0; t < LiquidDef.TintSlots; t++)
            {
                float v = m[t];
                if (!(v >= 0 && v <= 1 + TintTolerance) || (t >= slots && v != 0)) return false;
            }
            return m.Sum <= 1 + TintTolerance;
        }

        static bool Same(WaterMix a, WaterMix b) => a.A == b.A && a.B == b.B && a.C == b.C;
    }
}
