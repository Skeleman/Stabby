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

    public static class WaterTint
    {
        public static void Apply(GeneratedWorld w)
        {
            var hy = w.Water;
            foreach (var lake in hy.Lakes) lake.Mix = default;
            foreach (var r in hy.Rivers) r.Mix.Clear();
            if (w.Biome == null || w.BiomeIds == null || w.Materials == null || w.Climate == null) return;
            var slots = w.Materials.TintSlots();
            if (slots.Length == 0) return;
            int n = w.Config.Size;
            var slotOf = new int[w.BiomeIds.Length];
            for (int b = 0; b < slotOf.Length; b++) slotOf[b] = -1;
            for (int t = 0; t < slots.Length && t < LiquidDef.TintSlots; t++)
                foreach (var id in w.Materials.Liquids[slots[t]].Biomes)
                {
                    int b = Array.IndexOf(w.BiomeIds, id);
                    if (b >= 0) slotOf[b] = t;
                }
            var stack = Drainage.Stack(hy.Receiver, n);
            var mix = new WaterMix[n * n];
            var weight = new float[n * n];
            var acc = new float[n * n];
            var precip = w.Climate.PrecipitationMm;
            for (int t = 0; t < slots.Length && t < LiquidDef.TintSlots; t++)
            {
                for (int i = 0; i < n * n; i++)
                {
                    int b = w.Biome[i];
                    weight[i] = b >= 0 && slotOf[b] == t ? Hydrology.Runoff(w.Config, w.Sea[i], precip[i]) : 0;
                }
                Drainage.Accumulate(stack, hy.Receiver, weight, acc);
                for (int i = 0; i < n * n; i++)
                {
                    float q = hy.DischargeM3s[i];
                    mix[i][t] = q > 0 ? Math.Min(1f, acc[i] / q) : 0;
                }
            }
            for (int i = 0; i < n * n; i++)
            {
                float sum = mix[i].Sum;
                if (sum > 1) mix[i] = WaterMix.Lerp(default, mix[i], 1 / sum);
            }
            foreach (var lake in hy.Lakes)
                if (lake.Outlet >= 0) lake.Mix = mix[lake.Outlet];
            foreach (var r in hy.Rivers)
                for (int k = 0; k < r.Cells.Count; k++)
                {
                    int i = r.Cells[k];
                    if (hy.LakeId[i] >= 0) r.Mix.Add(hy.Lakes[hy.LakeId[i]].Mix);
                    else if (w.Sea[i] && k > 0) r.Mix.Add(r.Mix[k - 1]);
                    else r.Mix.Add(mix[i]);
                }
        }
    }
}
