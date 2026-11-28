using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class PrecipLook
    {
        public int Count;
        public float FallMs, LengthM, WidthM, SwayM, Alpha;
        public Rgb Color;

        public static void Validate(List<string> e, string name, PrecipLook p)
        {
            if (p == null) { e.Add(name + ": required"); return; }
            if (!(p.Count >= 0 && p.Count <= 200000)) e.Add($"{name}.count {p.Count} outside 0..200000");
            if (!(p.FallMs > 0 && p.FallMs <= 40)) e.Add($"{name}.fallMs {p.FallMs} outside 0..40");
            if (!(p.LengthM > 0 && p.LengthM <= 4)) e.Add($"{name}.lengthM {p.LengthM} outside 0..4");
            if (!(p.WidthM > 0 && p.WidthM <= 0.5f)) e.Add($"{name}.widthM {p.WidthM} outside 0..0.5");
            if (!(p.SwayM >= 0 && p.SwayM <= 4)) e.Add($"{name}.swayM {p.SwayM} outside 0..4");
            if (!(p.Alpha >= 0 && p.Alpha <= 1)) e.Add($"{name}.alpha {p.Alpha} outside 0..1");
            SkyDef.CheckColor(e, name + ".color", p.Color);
        }
    }

    public sealed class WeatherDef
    {
        public float SpellS = 900, CoverMin = 0.25f, CoverMax = 0.9f, WetFrom = 0.6f, SnowBelowC = 0.5f, SleetBandC = 1.5f, MinPx = 4;
        public PrecipLook Rain = new PrecipLook { Count = 24000, FallMs = 8, LengthM = 0.9f, WidthM = 0.01f, Alpha = 0.3f, Color = new Rgb(0.62f, 0.66f, 0.7f) };
        public PrecipLook Snow = new PrecipLook { Count = 12000, FallMs = 1, LengthM = 0.05f, WidthM = 0.05f, SwayM = 0.4f, Alpha = 0.85f, Color = new Rgb(0.9f, 0.91f, 0.93f) };

        public void At(ulong seed, double clockS, out float cover, out float precip)
        {
            ulong key = Rng.Stream(seed, "weather").NextU64();
            float v = 0.7f * Smooth(key, clockS / SpellS) + 0.3f * Smooth(Rng.Mix(key), clockS * 3 / SpellS);
            cover = CoverMin + (CoverMax - CoverMin) * v;
            precip = Step(WetFrom, 1, v);
        }

        public float SnowFraction(float temperatureC) => 1 - Step(SnowBelowC - SleetBandC, SnowBelowC + SleetBandC, temperatureC);

        static float Smooth(ulong key, double t)
        {
            double k = Math.Floor(t);
            float f = (float)(t - k), a = Value(key, (long)k), b = Value(key, (long)k + 1);
            f = f * f * (3 - 2 * f);
            return a + (b - a) * f;
        }

        static float Value(ulong key, long k) => (Rng.Mix(key ^ (ulong)k * 0x9E3779B97F4A7C15UL) >> 40) * (1f / 16777216f);

        static float Step(float a, float b, float x)
        {
            float t = (x - a) / (b - a);
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            return t * t * (3 - 2 * t);
        }

        public static void Validate(List<string> e, WeatherDef w)
        {
            if (w == null) { e.Add("sky.weather: required"); return; }
            if (!(w.SpellS >= 10 && w.SpellS <= 86400)) e.Add($"sky.weather.spellS {w.SpellS} outside 10..86400");
            if (!(w.CoverMin >= 0 && w.CoverMin <= w.CoverMax && w.CoverMax <= 1)) e.Add($"sky.weather.coverMin {w.CoverMin} / coverMax {w.CoverMax} must satisfy 0 <= min <= max <= 1");
            if (!(w.WetFrom >= 0 && w.WetFrom < 1)) e.Add($"sky.weather.wetFrom {w.WetFrom} outside 0..1");
            if (!(w.SnowBelowC >= -30 && w.SnowBelowC <= 10)) e.Add($"sky.weather.snowBelowC {w.SnowBelowC} outside -30..10");
            if (!(w.SleetBandC > 0 && w.SleetBandC <= 10)) e.Add($"sky.weather.sleetBandC {w.SleetBandC} outside 0..10");
            if (!(w.MinPx >= 0.25f && w.MinPx <= 8)) e.Add($"sky.weather.minPx {w.MinPx} outside 0.25..8");
            PrecipLook.Validate(e, "sky.weather.rain", w.Rain);
            PrecipLook.Validate(e, "sky.weather.snow", w.Snow);
        }
    }
}
