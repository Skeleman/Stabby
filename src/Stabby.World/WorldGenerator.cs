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

        // Fingerprint over quantised outputs, for determinism checks.
        public ulong Fingerprint()
        {
            ulong h = 14695981039346656037UL;
            void Mix(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
            Mix(WorldConfig.GeneratorVersion);
            foreach (float v in Height) Mix((long)Math.Round(v * 1000));
            foreach (float v in Climate.PrecipitationMm) Mix((long)Math.Round(v));
            foreach (int b in Biome) Mix(b);
            Mix(Water.Lakes.Count);
            foreach (var r in Water.Rivers) foreach (int cell in r.Cells) Mix(cell);
            return h;
        }
    }

    // Pipeline: sketch -> provisional climate -> erosion (terrain and drainage
    // together) -> final climate -> hydrology -> biomes.
    public static class WorldGenerator
    {
        public static GeneratedWorld Generate(WorldConfig c, IReadOnlyList<BiomeDef> biomes, WorldSketch sketch = null)
        {
            sketch = sketch ?? WorldSketch.Generate(c);
            var sea = Erosion.SeaOf(sketch.Land);
            var h = Erosion.InitialHeight(c, sketch);
            var provisional = ClimateField.Compute(c, h, sea);
            Erosion.Run(c, sketch, h, provisional.PrecipitationMm);
            var climate = ClimateField.Compute(c, h, sea);
            var water = Hydrology.Build(c, h, sea, climate.PrecipitationMm);

            var wet = new bool[h.Length];
            for (int i = 0; i < h.Length; i++) wet[i] = sea[i] || water.LakeId[i] >= 0;
            return new GeneratedWorld
            {
                Config = c, Sketch = sketch, Sea = sea, Height = h,
                Climate = climate, Water = water, Biome = Biomes.Classify(biomes, climate, wet),
            };
        }
    }
}
