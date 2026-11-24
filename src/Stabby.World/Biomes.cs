using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class BiomeDef
    {
        public string Id;
        public string Name;
        public float TempMinC, TempMaxC;
        public float PrecipMinMm, PrecipMaxMm;
        public uint Color;
        public float HeightMinM = float.NaN, HeightMaxM = float.NaN;
        public float WaterWithinM = float.NaN, WaterRiseM = float.NaN, CoastWithinM = float.NaN;

        public bool Conditional => !float.IsNaN(HeightMinM) || !float.IsNaN(HeightMaxM) || !float.IsNaN(WaterWithinM) || !float.IsNaN(CoastWithinM);
    }

    public static class Biomes
    {
        public static int[] Classify(WorldConfig c, IReadOnlyList<BiomeDef> defs, ClimateField climate, float[] height, bool[] sea, Hydrology hy, bool[] water)
        {
            int count = climate.TemperatureC.Length;
            bool needWater = false, needCoast = false;
            foreach (var d in defs) { needWater |= !float.IsNaN(d.WaterWithinM); needCoast |= !float.IsNaN(d.CoastWithinM); }
            float[] toWater = null, toCoast = null, waterLevel = null;
            if (needWater)
            {
                var src = new bool[count];
                for (int i = 0; i < count; i++) src[i] = hy.IsRiver[i] || hy.LakeId[i] >= 0;
                waterLevel = (float[])hy.WaterSurface.Clone();
                toWater = Distance(c.Size, src, c.CellMeters, waterLevel);
            }
            if (needCoast) toCoast = Distance(c.Size, sea, c.CellMeters);
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                if (water[i]) { result[i] = -1; continue; }
                float t = climate.TemperatureC[i], p = climate.PrecipitationMm[i];
                int best = -1; float bestScore = float.MinValue;
                for (int b = 0; b < defs.Count; b++)
                {
                    var d = defs[b];
                    if (!d.Conditional) continue;
                    if (!float.IsNaN(d.HeightMinM) && height[i] < d.HeightMinM) continue;
                    if (!float.IsNaN(d.HeightMaxM) && height[i] > d.HeightMaxM) continue;
                    if (!float.IsNaN(d.WaterWithinM) && toWater[i] > d.WaterWithinM) continue;
                    if (!float.IsNaN(d.WaterRiseM) && height[i] - waterLevel[i] > d.WaterRiseM) continue;
                    if (!float.IsNaN(d.CoastWithinM) && toCoast[i] > d.CoastWithinM) continue;
                    float score = Score(d, t, p);
                    if (score >= 0 && score > bestScore) { bestScore = score; best = b; }
                }
                if (best < 0)
                    for (int b = 0; b < defs.Count; b++)
                    {
                        if (defs[b].Conditional) continue;
                        float score = Score(defs[b], t, p);
                        if (score > bestScore) { bestScore = score; best = b; }
                    }
                result[i] = Math.Max(0, best);
            }
            return result;
        }

        static float Score(BiomeDef d, float t, float p)
        {
            float st = Math.Min(t - d.TempMinC, d.TempMaxC - t) / (d.TempMaxC - d.TempMinC);
            float sp = Math.Min(p - d.PrecipMinMm, d.PrecipMaxMm - p) / (d.PrecipMaxMm - d.PrecipMinMm);
            return Math.Min(st, sp);
        }

        public static float[] Distance(int n, bool[] source, float cellMeters, float[] carry = null)
        {
            const float Diag = 1.41421356f;
            var d = new float[n * n];
            for (int i = 0; i < d.Length; i++) d[i] = source[i] ? 0 : float.MaxValue;
            void Relax(int i, int j, float step)
            {
                float v = d[j] + step;
                if (v >= d[i]) return;
                d[i] = v;
                if (carry != null) carry[i] = carry[j];
            }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    if (x > 0) Relax(i, i - 1, 1);
                    if (y > 0)
                    {
                        Relax(i, i - n, 1);
                        if (x > 0) Relax(i, i - n - 1, Diag);
                        if (x < n - 1) Relax(i, i - n + 1, Diag);
                    }
                }
            for (int y = n - 1; y >= 0; y--)
                for (int x = n - 1; x >= 0; x--)
                {
                    int i = y * n + x;
                    if (x < n - 1) Relax(i, i + 1, 1);
                    if (y < n - 1)
                    {
                        Relax(i, i + n, 1);
                        if (x < n - 1) Relax(i, i + n + 1, Diag);
                        if (x > 0) Relax(i, i + n - 1, Diag);
                    }
                }
            for (int i = 0; i < d.Length; i++) d[i] = d[i] == float.MaxValue ? float.MaxValue : d[i] * cellMeters;
            return d;
        }

        public const float RegionCore = 0.6f;

        public static void ApplyRegions(WorldConfig c, IReadOnlyList<BiomeDef> defs, WorldSketch s, bool[] water, int[] biome)
        {
            int n = c.Size;
            uint seed = Rng.Stream(c.Seed, "region-edge").NextU32();
            foreach (var r in s.Regions)
            {
                int id = IndexOf(defs, r.Biome);
                if (id < 0) continue;
                int x0 = Math.Max(0, (int)(r.X - r.Radius)), x1 = Math.Min(n - 1, (int)(r.X + r.Radius) + 1);
                int y0 = Math.Max(0, (int)(r.Y - r.Radius)), y1 = Math.Min(n - 1, (int)(r.Y + r.Radius) + 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = y * n + x;
                        if (water[i]) continue;
                        float dx = x - r.X, dy = y - r.Y;
                        float d = (float)Math.Sqrt(dx * dx + dy * dy) / r.Radius;
                        float edge = 0.8f + 0.2f * Noise.Fbm(seed, x * 12f / n, y * 12f / n, 3);
                        if (d < Math.Max(RegionCore, edge)) biome[i] = id;
                    }
            }
        }

        public static int IndexOf(IReadOnlyList<BiomeDef> defs, string id)
        {
            for (int b = 0; b < defs.Count; b++) if (defs[b].Id == id) return b;
            return -1;
        }

        public static List<string> Validate(IReadOnlyList<BiomeDef> defs)
        {
            var errors = new List<string>();
            var ids = new HashSet<string>();
            foreach (var d in defs)
            {
                if (string.IsNullOrEmpty(d.Id)) errors.Add("biome without id");
                else if (!ids.Add(d.Id)) errors.Add("duplicate biome id " + d.Id);
                if (d.TempMaxC <= d.TempMinC) errors.Add(d.Id + ": tempMaxC must exceed tempMinC");
                if (d.PrecipMaxMm <= d.PrecipMinMm) errors.Add(d.Id + ": precipMaxMm must exceed precipMinMm");
                if (d.PrecipMinMm < 0) errors.Add(d.Id + ": precipMinMm below 0");
                if (d.HeightMaxM <= d.HeightMinM) errors.Add(d.Id + ": heightMaxM must exceed heightMinM");
                if (d.WaterWithinM <= 0) errors.Add(d.Id + ": waterWithinM must be above 0");
                if (d.WaterRiseM <= 0) errors.Add(d.Id + ": waterRiseM must be above 0");
                if (!float.IsNaN(d.WaterRiseM) && float.IsNaN(d.WaterWithinM)) errors.Add(d.Id + ": waterRiseM needs waterWithinM");
                if (d.CoastWithinM <= 0) errors.Add(d.Id + ": coastWithinM must be above 0");
            }
            return errors;
        }
    }
}
