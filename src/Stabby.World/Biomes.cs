using System;
using System.Collections.Generic;

namespace Stabby.World
{
    // Loaded from content/biomes.json by the host; the library never reads files.
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
        // Picks the biome whose temperature/precipitation window the cell sits
        // deepest inside. Outside every window, the nearest one still wins, so
        // no cell is left unclassified.
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
