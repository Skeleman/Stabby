using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public struct WaterMix
    {
        public float A, B, C;

        public float this[int k]
        {
            get => k == 0 ? A : k == 1 ? B : C;
            set { if (k == 0) A = value; else if (k == 1) B = value; else C = value; }
        }

        public float Sum => A + B + C;

        public static WaterMix Lerp(WaterMix a, WaterMix b, float t) =>
            new WaterMix { A = a.A + (b.A - a.A) * t, B = a.B + (b.B - a.B) * t, C = a.C + (b.C - a.C) * t };
    }

    public struct LakeInflow
    {
        public int Cell;
        public float Reach, Discharge;
        public WaterMix Mix;
    }

    public static class WaterTint
    {
        public const float InflowReachMinCells = 2f, InflowReachWidths = 8f, InflowReachMaxCells = 10f, InflowCoreCells = 0.75f;

        static void Inflows(GeneratedWorld w, WaterMix[] mix)
        {
            var hy = w.Water;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            foreach (var r in hy.Rivers)
                for (int k = 1; k < r.Cells.Count; k++)
                {
                    int i = r.Cells[k], p = r.Cells[k - 1];
                    if (hy.LakeId[i] < 0 || hy.LakeId[p] >= 0 || w.Sea[p]) continue;
                    var lake = hy.Lakes[hy.LakeId[i]];
                    if (i == lake.Outlet) continue;
                    float q = hy.DischargeM3s[p], reach = Math.Min(InflowReachMaxCells, Math.Max(InflowReachMinCells, InflowReachWidths * r.Width[k - 1] / cm));
                    if (lake.Outlet >= 0) reach = Math.Min(reach, Distance(n, i, lake.Outlet));
                    int at = lake.Inflows.FindIndex(f => f.Cell == i);
                    if (at < 0) { lake.Inflows.Add(new LakeInflow { Cell = i, Reach = reach, Discharge = q, Mix = mix[p] }); continue; }
                    var o = lake.Inflows[at];
                    float sum = o.Discharge + q;
                    lake.Inflows[at] = new LakeInflow { Cell = i, Reach = Math.Max(o.Reach, reach), Discharge = sum, Mix = sum > 0 ? WaterMix.Lerp(o.Mix, mix[p], q / sum) : o.Mix };
                }
        }

        static float Distance(int n, int a, int b)
        {
            float dx = a % n - b % n, dy = a / n - b / n;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public static WaterMix MixAt(Lake lake, int n, int cell) => MixAt(lake, n, cell % n, cell / n);

        public static WaterMix MixAt(Lake lake, int n, float x, float y)
        {
            foreach (var f in lake.Inflows)
                if (f.Cell % n == x && f.Cell / n == y) return f.Mix;
            WaterMix acc = default;
            float sum = 0, near = 0;
            foreach (var f in lake.Inflows)
            {
                float dx = x - f.Cell % n, dy = y - f.Cell / n, d2 = dx * dx + dy * dy, d = (float)Math.Sqrt(d2);
                float t = d <= InflowCoreCells ? 1 : Grid.Smoothstep(0, 1, (f.Reach - d) / Math.Max(f.Reach - InflowCoreCells, 1e-3f));
                if (t <= 0) continue;
                float wgt = t / d2;
                acc = new WaterMix { A = acc.A + f.Mix.A * wgt, B = acc.B + f.Mix.B * wgt, C = acc.C + f.Mix.C * wgt };
                sum += wgt;
                near = Math.Max(near, t);
            }
            return sum > 0 ? WaterMix.Lerp(lake.Mix, WaterMix.Lerp(default, acc, 1 / sum), near) : lake.Mix;
        }

        public static void Apply(GeneratedWorld w) => Apply(w, Field(w));

        public static WaterMix[] Field(GeneratedWorld w)
        {
            if (w.Biome == null || w.BiomeIds == null || w.Materials == null || w.Climate == null) return null;
            var slots = w.Materials.TintSlots();
            if (slots.Length == 0) return null;
            var hy = w.Water;
            int n = w.Config.Size, used = Math.Min(slots.Length, LiquidDef.TintSlots);
            var slotOf = new int[w.BiomeIds.Length];
            for (int b = 0; b < slotOf.Length; b++) slotOf[b] = -1;
            for (int t = 0; t < used; t++)
                foreach (var id in w.Materials.Liquids[slots[t]].Biomes)
                {
                    int b = Array.IndexOf(w.BiomeIds, id);
                    if (b >= 0) slotOf[b] = t;
                }
            var stack = Drainage.Stack(hy.Receiver, n);
            var mix = new WaterMix[n * n];
            var precip = w.Climate.PrecipitationMm;
            for (int i = 0; i < n * n; i++)
            {
                int b = w.Biome[i], t = b >= 0 ? slotOf[b] : -1;
                if (t >= 0) mix[i][t] = Hydrology.Runoff(w.Config, w.Sea[i], precip[i]);
            }
            var recv = hy.Receiver;
            for (int s = stack.Length - 1; s >= 0; s--)
            {
                int i = stack[s], r = recv[i];
                if (r == i) continue;
                mix[r].A += mix[i].A; mix[r].B += mix[i].B; mix[r].C += mix[i].C;
            }
            for (int i = 0; i < n * n; i++)
            {
                float q = hy.DischargeM3s[i];
                var a = mix[i];
                var m = q > 0 ? new WaterMix { A = Math.Min(1f, a.A / q), B = Math.Min(1f, a.B / q), C = Math.Min(1f, a.C / q) } : default;
                float sum = m.Sum;
                mix[i] = sum > 1 ? WaterMix.Lerp(default, m, 1 / sum) : m;
            }
            return mix;
        }

        public static void Apply(GeneratedWorld w, WaterMix[] mix)
        {
            var hy = w.Water;
            foreach (var lake in hy.Lakes) { lake.Mix = default; lake.Inflows.Clear(); }
            foreach (var r in hy.Rivers) r.Mix.Clear();
            if (mix == null) return;
            int n = w.Config.Size;
            foreach (var lake in hy.Lakes)
                if (lake.Outlet >= 0) lake.Mix = mix[lake.Outlet];
            Inflows(w, mix);
            foreach (var r in hy.Rivers)
                for (int k = 0; k < r.Cells.Count; k++)
                {
                    int i = r.Cells[k];
                    if (hy.LakeId[i] >= 0) r.Mix.Add(MixAt(hy.Lakes[hy.LakeId[i]], n, i));
                    else if (w.Sea[i] && k > 0) r.Mix.Add(r.Mix[k - 1]);
                    else r.Mix.Add(mix[i]);
                }
        }
    }
}
