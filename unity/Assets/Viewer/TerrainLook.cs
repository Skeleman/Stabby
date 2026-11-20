using System.Collections.Generic;
using Stabby.World;
using Unity.Mathematics;
using UnityEngine;

namespace Stabby.Viewer
{
    public sealed class StrataLook
    {
        public float[] Offsets;
        public float3[] Bands;
        public float PeriodM;
    }

    public static class TerrainLook
    {
        public const float BandStepM = 0.25f;

        static Texture2D strataTexture;

        public static float3 Sea => new float3(0.16f, 0.36f, 0.55f);
        public static float3 Lake => new float3(0.22f, 0.47f, 0.68f);
        public static float3 River => new float3(0.20f, 0.45f, 0.78f);
        public static float3 Sand => new float3(0.78f, 0.72f, 0.52f);

        public static float3 ToLinear(float3 c) => math.select(math.pow((c + 0.055f) / 1.055f, 2.4f), c / 12.92f, c <= 0.04045f);

        public static float3 Unpack(uint u) => new float3((u >> 16) & 255, (u >> 8) & 255, u & 255) / 255f;

        public static float4 Shade(float3 biome, float h, float ny)
        {
            if (h <= 0) return new float4(ToLinear(math.lerp(Sand, Sand * 0.6f, math.saturate(-h / 30f))), 0);
            float slope = math.sqrt(math.max(0, 1 - ny * ny)) / math.max(ny, 0.05f), shore = math.saturate(1f - h / 6f);
            var c = math.lerp(biome, Sand, shore);
            return new float4(ToLinear(c), math.saturate((slope - 0.25f) / 0.35f) * (1 - shore));
        }

        public static void ApplyStrata(StrataLook s)
        {
            if (strataTexture == null) strataTexture = new Texture2D(s.Bands.Length, 1, TextureFormat.RGBA32, true, true) { name = "Strata", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            else strataTexture.Reinitialize(s.Bands.Length, 1);
            var px = new Color32[s.Bands.Length];
            for (int k = 0; k < px.Length; k++)
            {
                var c = ToLinear(s.Bands[k]) * 255f;
                px[k] = new Color32((byte)c.x, (byte)c.y, (byte)c.z, 255);
            }
            strataTexture.SetPixels32(px);
            strataTexture.Apply(true);
            Shader.SetGlobalTexture("_StabbyStrata", strataTexture);
            Shader.SetGlobalFloat("_StabbyStrataPeriod", s.PeriodM);
        }

        public static float3 Bilinear(float3 a, float3 b, float3 c, float3 d, float tx, float ty) =>
            math.lerp(math.lerp(a, b, tx), math.lerp(c, d, tx), ty);

        public static float Bilinear(float a, float b, float c, float d, float tx, float ty) =>
            math.lerp(math.lerp(a, b, tx), math.lerp(c, d, tx), ty);

        public static float3[] BaseColors(GeneratedWorld w, IReadOnlyList<BiomeDef> defs)
        {
            var colors = new float3[w.Height.Length];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = w.Sea[i] ? Sand : Unpack(w.Biome != null && w.Biome[i] >= 0 ? defs[w.Biome[i]].Color : 0x8DAA5Eu);
            return colors;
        }

        public static StrataLook Strata(GeneratedWorld w, MaterialLibrary lib)
        {
            var ids = lib.Bands(BandStepM);
            var bands = new float3[ids.Length];
            for (int k = 0; k < ids.Length; k++) bands[k] = Unpack(lib.Strata[ids[k]].Color);
            return new StrataLook { Offsets = lib.Offsets(w), Bands = bands, PeriodM = lib.PeriodM };
        }
    }
}
