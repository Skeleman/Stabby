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
    }

    public static class Biomes
    {
        public static int[] Classify(IReadOnlyList<BiomeDef> defs, ClimateField climate, bool[] water)
        {
            int count = climate.TemperatureC.Length;
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                if (water[i]) { result[i] = -1; continue; }
                float t = climate.TemperatureC[i], p = climate.PrecipitationMm[i];
                int best = 0; float bestScore = float.MinValue;
                for (int b = 0; b < defs.Count; b++)
                {
                    var d = defs[b];
                    float st = Math.Min(t - d.TempMinC, d.TempMaxC - t) / (d.TempMaxC - d.TempMinC);
                    float sp = Math.Min(p - d.PrecipMinMm, d.PrecipMaxMm - p) / (d.PrecipMaxMm - d.PrecipMinMm);
                    float score = Math.Min(st, sp);
                    if (score > bestScore) { bestScore = score; best = b; }
                }
                result[i] = best;
            }
            return result;
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
            }
            return errors;
        }
    }
}
