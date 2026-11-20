using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class GeneratedWorld
    {
        public WorldConfig Config;
        public WorldSketch Sketch;
        public bool[] Sea;
        public float[] Height;
        public ClimateField Climate;
        public Hydrology Water;
        public int[] Biome;
        public string[] BiomeIds;

        public ulong Fingerprint()
        {
            ulong h = 14695981039346656037UL;
            void Mix(ulong v) { h ^= v; h *= 1099511628211UL; }
            void F(float v) => Mix((ulong)BitConverter.SingleToInt32Bits(v));
            void Fs(float[] a) { if (a == null) { Mix(0); return; } Mix((ulong)a.Length); foreach (float v in a) F(v); }
            void Is(int[] a) { if (a == null) { Mix(0); return; } Mix((ulong)a.Length); foreach (int v in a) Mix((ulong)v); }
            void Bs(bool[] a) { if (a == null) { Mix(0); return; } Mix((ulong)a.Length); foreach (bool v in a) Mix(v ? 1UL : 2UL); }
            Mix(WorldConfig.GeneratorVersion);
            Mix(Config.Seed); Mix((ulong)Config.Size); F(Config.CellMeters);
            Bs(Sketch?.Land); Fs(Sketch?.Uplift); Bs(Sketch?.Landmark);
            Bs(Sea); Fs(Height);
            Fs(Climate.TemperatureC); Fs(Climate.PrecipitationMm);
            Is(Water.Receiver); Fs(Water.WaterSurface); Fs(Water.DischargeM3s); Is(Water.LakeId); Bs(Water.IsRiver);
            Mix((ulong)Water.Lakes.Count);
            foreach (var l in Water.Lakes) { F(l.Level); Mix((ulong)l.Outlet); Mix((ulong)l.Cells.Count); }
            Mix((ulong)Water.Rivers.Count);
            foreach (var r in Water.Rivers)
            {
                Mix((ulong)r.Cells.Count);
                for (int k = 0; k < r.Cells.Count; k++) { Mix((ulong)r.Cells[k]); F(r.Surface[k]); F(r.Width[k]); F(r.Depth[k]); }
            }
            Is(Biome);
            return h;
        }
    }

    public static class WorldGenerator
    {
        public static GeneratedWorld Generate(WorldConfig c, IReadOnlyList<BiomeDef> biomes, WorldSketch sketch = null, Action<string> stage = null)
        {
            sketch = sketch ?? WorldSketch.Generate(c);
            stage?.Invoke("sketch");
            var sea = Erosion.SeaOf(sketch.Land);
            var h = Erosion.InitialHeight(c, sketch);
            bool terrainOnly = c.UniformRainMm > 0;
            var provisional = terrainOnly ? ClimateField.Uniform(c, h, c.UniformRainMm) : ClimateField.Compute(c, h, sea);
            Erosion.Run(c, sketch, h, provisional.PrecipitationMm);
            Erosion.FlattenTableland(c, sketch, h);
            Erosion.CarveLakes(c, sketch, h);
            stage?.Invoke("erosion");
            var climate = terrainOnly ? ClimateField.Uniform(c, h, c.UniformRainMm) : ClimateField.Compute(c, h, sea);
            stage?.Invoke("climate");
            var water = Hydrology.Build(c, h, sea, climate.PrecipitationMm, sketch.Landmark);
            stage?.Invoke("hydrology");

            var w = new GeneratedWorld { Config = c, Sketch = sketch, Sea = sea, Height = h, Climate = climate, Water = water };
            if (terrainOnly) return w;
            var wet = new bool[h.Length];
            for (int i = 0; i < h.Length; i++) wet[i] = sea[i] || water.LakeId[i] >= 0;
            w.Biome = Biomes.Classify(biomes, climate, wet);
            w.BiomeIds = new string[biomes.Count];
            for (int b = 0; b < biomes.Count; b++) w.BiomeIds[b] = biomes[b].Id;
            Biomes.ApplyRegions(c, biomes, sketch, wet, w.Biome);
            stage?.Invoke("biomes");
            return w;
        }
    }
}
