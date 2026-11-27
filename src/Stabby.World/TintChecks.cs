using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static partial class WorldChecks
    {
        public const float TintTolerance = 1e-3f;

        static void CheckLakeCover(GeneratedWorld w, List<string> e)
        {
            var hy = w.Water;
            int n = w.Config.Size;
            foreach (var s in hy.Rivers)
            {
                int id = Hydrology.DrainsTo(hy, s);
                if (id < 0) continue;
                float level = hy.Lakes[id].Level;
                foreach (int i in s.Cells)
                {
                    if (w.Sea[i] || hy.LakeId[i] >= 0 || w.Height[i] >= level || (hy.LakeCover != null && hy.LakeCover[i] == id)) continue;
                    int x = i % n, y = i / n;
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = x + Grid.DX[k], ny = y + Grid.DY[k];
                        if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                        int o = ny * n + nx;
                        if (hy.LakeId[o] == id || (hy.LakeCover != null && hy.LakeCover[o] == id)) { e.Add($"river {s.Id} cell {i} sits below lake {id} without cover"); return; }
                    }
                }
            }
        }

        static void CheckTint(GeneratedWorld w, List<string> e)
        {
            if (w.Biome == null || w.Materials == null || w.Materials.TintSlots().Length == 0) return;
            var hy = w.Water;
            int slots = w.Materials.TintSlots().Length;
            foreach (var lake in hy.Lakes)
                if (!InRange(lake.Mix, slots)) { e.Add($"lake {lake.Id} tint mix out of range"); break; }
            int n = w.Config.Size;
            var entering = new Dictionary<int, float>();
            foreach (var s in hy.Rivers)
                for (int k = 1; k < s.Cells.Count; k++)
                    if (Joins(w, s, k))
                    {
                        entering.TryGetValue(s.Cells[k], out float sum);
                        entering[s.Cells[k]] = sum + hy.DischargeM3s[s.Cells[k - 1]];
                    }
            foreach (var s in hy.Rivers)
            {
                if (s.Mix.Count != s.Cells.Count) { e.Add($"river {s.Id} has {s.Mix.Count} tint mixes for {s.Cells.Count} cells"); break; }
                bool bad = false;
                for (int k = 0; k < s.Cells.Count && !bad; k++)
                {
                    int i = s.Cells[k];
                    if (!InRange(s.Mix[k], slots)) { e.Add($"river {s.Id} tint mix out of range at point {k}"); bad = true; }
                    else if (hy.LakeId[i] >= 0 && !Same(s.Mix[k], WaterTint.MixAt(hy.Lakes[hy.LakeId[i]], n, i))) { e.Add($"river {s.Id} enters lake {hy.LakeId[i]} with a different tint"); bad = true; }
                    else if (k > 0 && Joins(w, s, k))
                    {
                        float q = entering[i], joined = q > 0 ? Math.Max(0, q - hy.DischargeM3s[s.Cells[k - 1]]) / q : 0;
                        for (int t = 0; t < slots && !bad; t++)
                        {
                            float jump = Math.Abs(s.Mix[k][t] - s.Mix[k - 1][t]);
                            if (jump > joined + TintTolerance) { e.Add($"river {s.Id} tint jumps {jump:F3} entering lake {hy.LakeId[i]}, inflow allows {joined:F3}"); bad = true; }
                        }
                    }
                    else if (k > 0 && !w.Sea[i] && hy.LakeId[i] < 0)
                    {
                        float q = hy.DischargeM3s[i], joined = q > 0 ? Math.Max(0, q - hy.DischargeM3s[s.Cells[k - 1]]) / q : 0;
                        if (k == 1 && q > 0 && hy.Deltas.Exists(d => d.Apex == s.Cells[0])) joined = Math.Min(1, SideInflow(w, i, s.Cells[0]) / q);
                        for (int t = 0; t < slots && !bad; t++)
                        {
                            float jump = Math.Abs(s.Mix[k][t] - s.Mix[k - 1][t]);
                            if (jump > joined + TintTolerance) { e.Add($"river {s.Id} tint jumps {jump:F3} at point {k},inflow allows {joined:F3}"); bad = true; }
                        }
                    }
                }
                if (bad) break;
            }
        }

        static float SideInflow(GeneratedWorld w, int i, int apex)
        {
            var hy = w.Water;
            int n = w.Config.Size, x = i % n, y = i / n;
            float sum = 0;
            for (int k = 0; k < 8; k++)
            {
                int j = (y + Grid.DY[k]) * n + x + Grid.DX[k];
                if (j != apex && !w.Sea[j] && hy.Receiver[j] == i) sum += hy.DischargeM3s[j];
            }
            return sum + Hydrology.Runoff(w.Config, w.Sea[i], w.Climate.PrecipitationMm[i]);
        }

        static bool Joins(GeneratedWorld w, RiverSegment s, int k)
        {
            var hy = w.Water;
            int i = s.Cells[k], p = s.Cells[k - 1];
            return hy.LakeId[i] >= 0 && hy.LakeId[p] < 0 && !w.Sea[p] && hy.Lakes[hy.LakeId[i]].Outlet != i;
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
