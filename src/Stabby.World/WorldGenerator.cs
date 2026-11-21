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
            var f = new Fnv();
            f.Mix(OutputFingerprint());
            var k = Sketch;
            if (k == null) { f.Mix(0); return f.H; }
            f.Mix((ulong)k.Size);
            Masses(f, k.Landmasses); Masses(f, k.Islets); Masses(f, k.Lakes);
            f.Mix((ulong)k.Ranges.Count);
            foreach (var r in k.Ranges) { f.Fs(r.X); f.Fs(r.Y); f.F(r.Strength); f.F(r.Width); }
            f.Mix((ulong)k.Corridors.Count);
            foreach (var r in k.Corridors) { f.Fs(r.X); f.Fs(r.Y); f.F(r.Width); }
            f.Mix((ulong)k.Regions.Count);
            foreach (var r in k.Regions) { f.S(r.Biome); f.F(r.X); f.F(r.Y); f.F(r.Radius); }
            f.Mix((ulong)k.Landmarks.Count);
            foreach (var r in k.Landmarks) { f.S(r.Kind); f.F(r.X); f.F(r.Y); }
            f.Mix((ulong)k.Shores.Count);
            foreach (var r in k.Shores) { f.Mix(r.Cliff ? 1UL : 2UL); f.F(r.X); f.F(r.Y); f.F(r.Radius); }
            f.Mix((ulong)k.Highlands.Count);
            foreach (var r in k.Highlands) { f.Mix(r.Plateau ? 1UL : 2UL); f.F(r.X); f.F(r.Y); f.F(r.Radius); }
            f.Bs(k.Land); f.Fs(k.Uplift); f.Bs(k.Landmark); f.Fs(k.Valley); f.Fs(k.Coast);
            f.Fs(k.ValleyRun); f.Fs(k.Cliff); f.Fs(k.Beach); f.Fs(k.Plateau);
            return f.H;
        }

        public ulong OutputFingerprint()
        {
            var f = new Fnv();
            f.Mix(WorldConfig.GeneratorVersion);
            f.Mix(Config.Seed); f.Mix((ulong)Config.Size); f.F(Config.CellMeters);
            f.Bs(Sea); f.Fs(Height);
            f.Fs(Climate.TemperatureC); f.Fs(Climate.PrecipitationMm);
            f.Is(Water.Receiver); f.Fs(Water.WaterSurface); f.Fs(Water.DischargeM3s); f.Is(Water.LakeId); f.Bs(Water.IsRiver);
            f.Mix((ulong)Water.Lakes.Count);
            foreach (var l in Water.Lakes) { f.F(l.Level); f.Mix((ulong)l.Outlet); f.Mix((ulong)l.Cells.Count); }
            f.Mix((ulong)Water.Rivers.Count);
            foreach (var r in Water.Rivers)
            {
                f.Mix((ulong)r.Cells.Count);
                for (int k = 0; k < r.Cells.Count; k++) { f.Mix((ulong)r.Cells[k]); f.F(r.Surface[k]); f.F(r.Width[k]); f.F(r.Depth[k]); }
            }
            f.Is(Biome);
            if (BiomeIds == null) f.Mix(0);
            else { f.Mix((ulong)BiomeIds.Length); foreach (var id in BiomeIds) f.S(id); }
            return f.H;
        }

        static void Masses(Fnv f, List<LandmassSketch> list)
        {
            f.Mix((ulong)list.Count);
            foreach (var m in list)
            {
                f.F(m.X); f.F(m.Y); f.F(m.Radius);
                f.Mix((ulong)m.Lobes.Count);
                foreach (var l in m.Lobes) { f.F(l.X); f.F(l.Y); f.F(l.Radius); }
            }
        }

        sealed class Fnv
        {
            public ulong H = 14695981039346656037UL;
            public void Mix(ulong v) { H ^= v; H *= 1099511628211UL; }
            public void F(float v) => Mix((ulong)BitConverter.SingleToInt32Bits(v));
            public void S(string v) { if (v == null) { Mix(0); return; } Mix((ulong)v.Length + 1); foreach (char c in v) Mix(c); }
            public void Fs(float[] a) { if (a == null) { Mix(0); return; } Mix((ulong)a.Length + 1); foreach (float v in a) F(v); }
            public void Is(int[] a) { if (a == null) { Mix(0); return; } Mix((ulong)a.Length + 1); foreach (int v in a) Mix((ulong)v); }
            public void Bs(bool[] a) { if (a == null) { Mix(0); return; } Mix((ulong)a.Length + 1); foreach (bool v in a) Mix(v ? 1UL : 2UL); }
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
            w.Biome = Biomes.Classify(c, biomes, climate, h, sea, water, wet);
            w.BiomeIds = new string[biomes.Count];
            for (int b = 0; b < biomes.Count; b++) w.BiomeIds[b] = biomes[b].Id;
            Biomes.ApplyRegions(c, biomes, sketch, wet, w.Biome);
            stage?.Invoke("biomes");
            return w;
        }
    }
}
