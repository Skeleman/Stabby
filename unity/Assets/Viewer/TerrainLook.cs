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
        public float[] Hardness;
        public float PeriodM;
        public byte[] Coat;
        public int N;
        public float CellM;
        public Vector4 Surface, Wobble, RockA, RockB, RockC;
        public Vector4[] CoatA, CoatB;
        public Vector4[] Water;
    }

    public sealed class FieldLook
    {
        public int Res, N;
        public float TexelM, CellM;
        public ushort[][] Slopes;
        public float[][] Bed;
        public ushort[] Biome;
    }

    public static class TerrainLook
    {
        public const float BandStepM = 0.25f;

        public const int FieldTexelsPerCell = 4, FieldMax = 2048;

        static Texture2D strataTexture, coatTexture, slopeTexture, bedTexture, biomeTexture;

        public static float3 Sand => new float3(0.86f, 0.80f, 0.62f);

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
                px[k] = new Color32((byte)c.x, (byte)c.y, (byte)c.z, (byte)math.round(s.Hardness[k] * 255f));
            }
            strataTexture.SetPixels32(px);
            strataTexture.Apply(true);
            if (coatTexture != null) UnityEngine.Object.Destroy(coatTexture);
            coatTexture = new Texture2D(s.N, s.N, TextureFormat.RGBA32, false, true) { name = "Coats", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            coatTexture.SetPixelData(s.Coat, 0);
            coatTexture.Apply(false, true);
            Shader.SetGlobalTexture("_StabbyStrata", strataTexture);
            Shader.SetGlobalTexture("_StabbyCoat", coatTexture);
            Shader.SetGlobalFloat("_StabbyStrataPeriod", s.PeriodM);
            Shader.SetGlobalVector("_StabbyCoatUV", new Vector4(1f / (s.CellM * s.N), 0.5f / s.N, 0, 0));
            Shader.SetGlobalVector("_StabbySurface", s.Surface);
            Shader.SetGlobalVector("_StabbyStrataWobble", s.Wobble);
            Shader.SetGlobalVector("_StabbyRockA", s.RockA);
            Shader.SetGlobalVector("_StabbyRockB", s.RockB);
            Shader.SetGlobalVector("_StabbyRockC", s.RockC);
            Shader.SetGlobalVectorArray("_StabbyCoatA", s.CoatA);
            Shader.SetGlobalVectorArray("_StabbyCoatB", s.CoatB);
            Shader.SetGlobalVector("_StabbyWaterDeep", s.Water[0]);
            Shader.SetGlobalVector("_StabbyWaterShallow", s.Water[1]);
            Shader.SetGlobalVector("_StabbyWaterWave", s.Water[2]);
            Shader.SetGlobalVector("_StabbyWaterSwell", s.Water[3]);
            Shader.SetGlobalVector("_StabbyWaterFlow", s.Water[4]);
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
            return new FieldLook { Res = res, N = n, TexelM = tm, CellM = cm, Slopes = mips.ToArray(), Bed = Mips(h, res), Biome = biome };
        }

        static float[][] Mips(float[] top, int res)
        {
            var mips = new List<float[]> { top };
            for (int p = res; p > 1;)
            {
                int cur = Math.Max(1, p >> 1), prev = p;
                var src = mips[mips.Count - 1];
                var dst = new float[cur * cur];
                Parallel.For(0, cur, k =>
                {
                    int k0 = Math.Min(2 * k, prev - 1), k1 = Math.Min(2 * k + 1, prev - 1);
                    for (int i = 0; i < cur; i++)
                    {
                        int i0 = Math.Min(2 * i, prev - 1), i1 = Math.Min(2 * i + 1, prev - 1);
                        dst[k * cur + i] = 0.25f * (src[k0 * prev + i0] + src[k0 * prev + i1] + src[k1 * prev + i0] + src[k1 * prev + i1]);
                    }
                });
                mips.Add(dst);
                p = cur;
            }
            return mips.ToArray();
        }

        public static void ApplyField(FieldLook f)
        {
            if (slopeTexture != null) UnityEngine.Object.Destroy(slopeTexture);
            if (bedTexture != null) UnityEngine.Object.Destroy(bedTexture);
            if (biomeTexture != null) UnityEngine.Object.Destroy(biomeTexture);
            slopeTexture = new Texture2D(f.Res, f.Res, TextureFormat.RGHalf, f.Slopes.Length, true) { name = "Slopes", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            for (int m = 0; m < f.Slopes.Length; m++) slopeTexture.SetPixelData(f.Slopes[m], m);
            slopeTexture.Apply(false, true);
            bedTexture = new Texture2D(f.Res, f.Res, TextureFormat.RFloat, f.Bed.Length, true) { name = "Bed", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            for (int m = 0; m < f.Bed.Length; m++) bedTexture.SetPixelData(f.Bed[m], m);
            bedTexture.Apply(false, true);
            biomeTexture = new Texture2D(f.N, f.N, TextureFormat.RGBAHalf, false, true) { name = "Biomes", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            biomeTexture.SetPixelData(f.Biome, 0);
            biomeTexture.Apply(false, true);
            Shader.SetGlobalTexture("_StabbySlope", slopeTexture);
            Shader.SetGlobalTexture("_StabbyBed", bedTexture);
            Shader.SetGlobalTexture("_StabbyBiome", biomeTexture);
            Shader.SetGlobalVector("_StabbyFieldUV", new Vector4(1f / (f.TexelM * f.Res), 0.5f / f.Res, 1f / (f.CellM * f.N), 0.5f / f.N));
            Shader.SetGlobalVector("_StabbySand", new Vector4(Sand.x, Sand.y, Sand.z, 0));
        }

        public static StrataLook Strata(GeneratedWorld w, MaterialLibrary lib)
        {
            var ids = lib.Bands(BandStepM);
            var bands = new float3[ids.Length];
            var hard = new float[ids.Length];
            for (int k = 0; k < ids.Length; k++) { bands[k] = Unpack(lib.Strata[ids[k]].Color); hard[k] = lib.Strata[ids[k]].Hardness; }
            var d = lib.Surface;
            var r = d.Rock;
            float wind = math.radians(d.WindDeg);
            var coatA = new Vector4[d.Coats.Length];
            var coatB = new Vector4[d.Coats.Length];
            for (int k = 0; k < d.Coats.Length; k++)
            {
                var c = d.Coats[k];
                coatA[k] = new Vector4(c.BumpAmp, 1f / c.BumpWavelengthM, c.GrainAmp, 1f / c.GrainWavelengthM);
                coatB[k] = new Vector4(c.FeatureAmp, 1f / c.FeatureWavelengthM, c.Parallax, c.AlbedoVar);
            }
            return new StrataLook
            {
                Offsets = lib.Offsets(w), Bands = bands, Hardness = hard, PeriodM = lib.PeriodM,
                Coat = d.CoatWeights(w), N = w.Config.Size, CellM = w.Config.CellMeters,
                Surface = new Vector4(d.FadePixels, 1f / d.ParallaxFootprintM, math.sin(wind), math.cos(wind)),
                Wobble = new Vector4(d.StrataWobbleM, 1f / d.StrataWobbleWavelengthM, d.CoatPatchAmp, 1f / d.CoatPatchWavelengthM),
                RockA = new Vector4(r.GrainAmp, 1f / r.GrainWavelengthM, r.BumpAmp, 1f / r.BumpWavelengthM),
                RockB = new Vector4(1f / r.BedSpacingM, r.BedDepthM, 1f / r.JointSpacingM, r.JointDepthM),
                RockC = new Vector4(r.Parallax, 0, 0, 0),
                CoatA = coatA, CoatB = coatB,
                Water = WaterLook(lib.Water),
            };
        }

        static Vector4[] WaterLook(WaterDef w)
        {
            float3 deep = ToLinear(Unpack(w.DeepColor)), shallow = ToLinear(Unpack(w.ShallowColor));
            return new[]
            {
                new Vector4(deep.x, deep.y, deep.z, 1f / w.ClarityM),
                new Vector4(shallow.x, shallow.y, shallow.z, 1f / w.ShoreM),
                new Vector4(1f / w.WaveScaleM, w.WaveSpeed, w.WaveStrength, w.FoamWidthM),
                new Vector4(1f / w.SwellScaleM, w.SwellStrength, 1f / w.BedFieldM, 0),
                new Vector4(w.FlowSpeed, 1f / w.RapidsSlope, w.LakeSwell, 0),
            };
        }
    }
}
