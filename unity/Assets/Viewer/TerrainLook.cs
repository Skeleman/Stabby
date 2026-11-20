using System.Collections.Generic;
using Stabby.World;
using Unity.Mathematics;

namespace Stabby.Viewer
{
    public static class TerrainLook
    {
        public static float3 Sea => new float3(0.16f, 0.36f, 0.55f);
        public static float3 Lake => new float3(0.22f, 0.47f, 0.68f);
        public static float3 River => new float3(0.20f, 0.45f, 0.78f);
        public static float3 Sand => new float3(0.78f, 0.72f, 0.52f);
        public static float3 Rock => new float3(0.47f, 0.44f, 0.41f);

        public static float3 ToLinear(float3 c) => math.select(math.pow((c + 0.055f) / 1.055f, 2.4f), c / 12.92f, c <= 0.04045f);

        public static float3 Shade(float3 biome, float h, float ny)
        {
            if (h <= 0) return ToLinear(math.lerp(Sand, Sand * 0.6f, math.saturate(-h / 30f)));
            float slope = math.sqrt(math.max(0, 1 - ny * ny)) / math.max(ny, 0.05f);
            var c = math.lerp(biome, Rock, math.saturate((slope - 0.45f) / 0.4f));
            c = math.lerp(c, Sand, math.saturate(1f - h / 6f));
            return ToLinear(c);
        }

        public static float3 Bilinear(float3 a, float3 b, float3 c, float3 d, float tx, float ty) =>
            math.lerp(math.lerp(a, b, tx), math.lerp(c, d, tx), ty);

        public static float3[] BaseColors(GeneratedWorld w, IReadOnlyList<BiomeDef> defs)
        {
            var colors = new float3[w.Height.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                uint u = w.Biome != null && w.Biome[i] >= 0 ? defs[w.Biome[i]].Color : 0x8DAA5Eu;
                colors[i] = w.Sea[i] ? Sand : new float3((u >> 16) & 255, (u >> 8) & 255, u & 255) / 255f;
            }
            return colors;
        }
    }
}
