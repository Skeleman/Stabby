using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        public Vector4 DetailAmp, DetailFreq;
    }

    public sealed class FieldLook
    {
        public int Res, N;
        public float TexelM, CellM;
        public ushort[][] Slopes;
        public ushort[] Biome;
    }

    public static class TerrainLook
    {
        public const float BandStepM = 0.25f;

        public const int FieldTexelsPerCell = 4, FieldMax = 2048;

        static Texture2D strataTexture, slopeTexture, biomeTexture;

        public static float3 Water => new float3(0.16f, 0.36f, 0.55f);
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
            Shader.SetGlobalVector("_StabbyDetailAmp", s.DetailAmp);
            Shader.SetGlobalVector("_StabbyDetailFreq", s.DetailFreq);
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

        public static FieldLook Field(TerrainDetail detail, int n, float cm, float3[] colors, float[] offsets)
        {
            int res = Math.Min(FieldMax, (n - 1) * FieldTexelsPerCell + 1);
            float tc = (n - 1f) / (res - 1), tm = tc * cm;
            var h = new float[res * res];
            Parallel.For(0, res, k => { for (int i = 0; i < res; i++) h[k * res + i] = detail.HeightAt(i * tc, k * tc); });
            var level = new float2[res * res];
            Parallel.For(0, res, k =>
            {
                int k0 = Math.Max(k - 1, 0), k1 = Math.Min(k + 1, res - 1);
                for (int i = 0; i < res; i++)
                {
                    int i0 = Math.Max(i - 1, 0), i1 = Math.Min(i + 1, res - 1);
                    level[k * res + i] = new float2((h[k * res + i1] - h[k * res + i0]) / ((i1 - i0) * tm), (h[k1 * res + i] - h[k0 * res + i]) / ((k1 - k0) * tm));
                }
            });
            var mips = new List<ushort[]>();
            for (int size = res, prev = res; ; prev = size, size = Math.Max(1, size >> 1))
            {
                if (size != prev)
                {
                    int p = prev, cur = size;
                    var src = level;
                    var dst = new float2[cur * cur];
                    Parallel.For(0, cur, k =>
                    {
                        int k0 = Math.Min(2 * k, p - 1), k1 = Math.Min(2 * k + 1, p - 1);
                        for (int i = 0; i < cur; i++)
                        {
                            int i0 = Math.Min(2 * i, p - 1), i1 = Math.Min(2 * i + 1, p - 1);
                            dst[k * cur + i] = 0.25f * (src[k0 * p + i0] + src[k0 * p + i1] + src[k1 * p + i0] + src[k1 * p + i1]);
                        }
                    });
                    level = dst;
                }
                var px = new ushort[level.Length * 2];
                for (int q = 0; q < level.Length; q++) { px[2 * q] = (ushort)math.f32tof16(level[q].x); px[2 * q + 1] = (ushort)math.f32tof16(level[q].y); }
                mips.Add(px);
                if (size == 1) break;
            }
            var biome = new ushort[n * n * 4];
            for (int q = 0; q < n * n; q++)
            {
                biome[4 * q] = (ushort)math.f32tof16(colors[q].x);
                biome[4 * q + 1] = (ushort)math.f32tof16(colors[q].y);
                biome[4 * q + 2] = (ushort)math.f32tof16(colors[q].z);
                biome[4 * q + 3] = (ushort)math.f32tof16(offsets[q]);
            }
            return new FieldLook { Res = res, N = n, TexelM = tm, CellM = cm, Slopes = mips.ToArray(), Biome = biome };
        }

        public static void ApplyField(FieldLook f)
        {
            if (slopeTexture != null) UnityEngine.Object.Destroy(slopeTexture);
            if (biomeTexture != null) UnityEngine.Object.Destroy(biomeTexture);
            slopeTexture = new Texture2D(f.Res, f.Res, TextureFormat.RGHalf, f.Slopes.Length, true) { name = "Slopes", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            for (int m = 0; m < f.Slopes.Length; m++) slopeTexture.SetPixelData(f.Slopes[m], m);
            slopeTexture.Apply(false, true);
            biomeTexture = new Texture2D(f.N, f.N, TextureFormat.RGBAHalf, false, true) { name = "Biomes", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            biomeTexture.SetPixelData(f.Biome, 0);
            biomeTexture.Apply(false, true);
            Shader.SetGlobalTexture("_StabbySlope", slopeTexture);
            Shader.SetGlobalTexture("_StabbyBiome", biomeTexture);
            Shader.SetGlobalVector("_StabbyFieldUV", new Vector4(1f / (f.TexelM * f.Res), 0.5f / f.Res, 1f / (f.CellM * f.N), 0.5f / f.N));
            Shader.SetGlobalVector("_StabbySand", new Vector4(Sand.x, Sand.y, Sand.z, 0));
        }

        public static StrataLook Strata(GeneratedWorld w, MaterialLibrary lib)
        {
            var ids = lib.Bands(BandStepM);
            var bands = new float3[ids.Length];
            for (int k = 0; k < ids.Length; k++) bands[k] = Unpack(lib.Strata[ids[k]].Color);
            var d = lib.Surface;
            return new StrataLook
            {
                Offsets = lib.Offsets(w), Bands = bands, PeriodM = lib.PeriodM,
                DetailAmp = new Vector4(d.StrataWobbleM, d.CoatPatchAmp, d.CoatGrainAmp, d.RockGrainAmp),
                DetailFreq = new Vector4(1f / d.StrataWobbleWavelengthM, 1f / d.CoatPatchWavelengthM, 1f / d.CoatGrainWavelengthM, 1f / d.RockGrainWavelengthM),
            };
        }
    }
}
