using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stabby.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Stabby.Viewer
{
    public sealed class StrataLook
    {
        public float[] Offsets;
        public float3[] Bands;
        public float[] Hardness;
        public float PeriodM;
        public byte[] Coat, Forest;
        public float[] WaterLevel;
        public int N;
        public float CellM;
        public Vector4 Surface, Detail, Wobble, RockA, RockB, RockC, Look, CoatRough;
        public Vector4[] CoatA, CoatB;
        public Vector4[] Liquids;
        public Vector4 Tints;
        public Vector4[] Sinter;
        public Vector4[] Kit, KitSrgb;
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

        static Texture2D strataTexture, coatTexture, forestTexture, levelTexture;

        public static float3 Sand => new float3(0.78f, 0.72f, 0.60f);

        public static float3 ToLinear(float3 c) => math.select(math.pow((c + 0.055f) / 1.055f, 2.4f), c / 12.92f, c <= 0.04045f);

        public static float3 Unpack(uint u) => new float3((u >> 16) & 255, (u >> 8) & 255, u & 255) / 255f;

        public static float4 Shade(float3 biome, float h, float ny)
        {
            if (h <= 0) return new float4(ToLinear(math.lerp(Sand, Sand * 0.6f, math.saturate(-h / 30f))), 0);
            float slope = math.sqrt(math.max(0, 1 - ny * ny)) / math.max(ny, 0.05f), shore = math.smoothstep(2.8f, 1.2f, h);
            var c = math.lerp(biome, Sand, shore);
            return new float4(ToLinear(c), math.saturate((slope - 0.25f) / 0.35f) * (1 - shore));
        }

        static Vector4 Swell(SwellParams p) => new Vector4(p.Height, p.Length, p.ShorePeriod, p.ShoreHeight);

        public static void ApplyStrata(StrataLook s)
        {
            foreach (string _ in StrataSteps(s)) { }
        }

        public static IEnumerable<string> StrataSteps(StrataLook s)
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
            yield return "strata bands";
            if (coatTexture != null) UnityEngine.Object.Destroy(coatTexture);
            coatTexture = new Texture2D(s.N, s.N, TextureFormat.RGBA32, false, true) { name = "Coats", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            coatTexture.SetPixelData(s.Coat, 0);
            coatTexture.Apply(false, true);
            yield return "strata coats";
            if (forestTexture != null) UnityEngine.Object.Destroy(forestTexture);
            forestTexture = new Texture2D(s.N, s.N, TextureFormat.RGBA32, false, true) { name = "Forest", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            forestTexture.SetPixelData(s.Forest, 0);
            forestTexture.Apply(false, true);
            yield return "strata forest";
            Shader.SetGlobalTexture("_StabbyStrata", strataTexture);
            Shader.SetGlobalTexture("_StabbyCoat", coatTexture);
            Shader.SetGlobalTexture("_StabbyForest", forestTexture);
            if (levelTexture != null) UnityEngine.Object.Destroy(levelTexture);
            levelTexture = new Texture2D(s.N, s.N, TextureFormat.RFloat, false, true) { name = "WaterLevel", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            levelTexture.SetPixelData(s.WaterLevel, 0);
            levelTexture.Apply(false, true);
            yield return "strata water level";
            Shader.SetGlobalTexture("_StabbyWaterLevel", levelTexture);
            Shader.SetGlobalFloat("_StabbyStrataPeriod", s.PeriodM);
            Shader.SetGlobalVector("_StabbyCoatUV", new Vector4(1f / (s.CellM * s.N), 0.5f / s.N, 0, 0));
            Shader.SetGlobalVector("_StabbySurface", s.Surface);
            Shader.SetGlobalVector("_StabbyDetail", s.Detail);
            Shader.SetGlobalVector("_StabbyStrataWobble", s.Wobble);
            Shader.SetGlobalVector("_StabbyRockA", s.RockA);
            Shader.SetGlobalVector("_StabbyRockB", s.RockB);
            Shader.SetGlobalVector("_StabbyRockC", s.RockC);
            Shader.SetGlobalVector("_StabbyLook", s.Look);
            Shader.SetGlobalVector("_StabbyCoatRough", s.CoatRough);
            Shader.SetGlobalVectorArray("_StabbyCoatA", s.CoatA);
            Shader.SetGlobalVectorArray("_StabbyCoatB", s.CoatB);
            Shader.SetGlobalVectorArray("_StabbyLiquid", s.Liquids);
            Shader.SetGlobalVector("_StabbySwellSea", Swell(WaterSwell.Sea));
            Shader.SetGlobalVector("_StabbySwellLake", Swell(WaterSwell.Lake));
            Shader.SetGlobalVector("_StabbyTintSlot", s.Tints);
            Shader.SetGlobalVector("_StabbySinterA", s.Sinter[0]);
            Shader.SetGlobalVector("_StabbySinterB", s.Sinter[1]);
            Shader.SetGlobalVector("_StabbySinterC", s.Sinter[2]);
            Shader.SetGlobalVector("_StabbyMudSurface", s.Sinter[3]);
            Shader.SetGlobalVector("_StabbyMudClay", s.Sinter[4]);
            Shader.SetGlobalVector("_StabbyMudGrey", s.Sinter[5]);
            Shader.SetGlobalVector("_StabbyPeatWet", s.Sinter[6]);
            Shader.SetGlobalVector("_StabbyPeatDark", s.Sinter[7]);
            Shader.SetGlobalVector("_StabbyPeatFibre", s.Sinter[8]);
            Shader.SetGlobalVector("_StabbyIce", s.Sinter[9]);
            Shader.SetGlobalVector("_StabbyRoad", s.Sinter[10]);
            Shader.SetGlobalVector("_StabbyRut", s.Sinter[11]);
            Shader.SetGlobalVector("_StabbyObsidian", s.Sinter[12]);
            Shader.SetGlobalVectorArray("_StabbyKit", s.Kit);
            Shader.SetGlobalVectorArray("_StabbyKitSrgb", s.KitSrgb);
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
                var lv = level;
                int side = size;
                Parallel.For(0, side, k =>
                {
                    for (int q = k * side, end = q + side; q < end; q++) { px[2 * q] = (ushort)math.f32tof16(lv[q].x); px[2 * q + 1] = (ushort)math.f32tof16(lv[q].y); }
                });
                mips.Add(px);
                if (size == 1) break;
            }
            var biome = new ushort[n * n * 4];
            Parallel.For(0, n, y =>
            {
                for (int q = y * n, end = q + n; q < end; q++)
                {
                    biome[4 * q] = (ushort)math.f32tof16(colors[q].x);
                    biome[4 * q + 1] = (ushort)math.f32tof16(colors[q].y);
                    biome[4 * q + 2] = (ushort)math.f32tof16(colors[q].z);
                    biome[4 * q + 3] = (ushort)math.f32tof16(offsets[q]);
                }
            });
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

        public static void CreateField(FieldLook f)
        {
            if (slopeField != null) UnityEngine.Object.Destroy(slopeField);
            if (bedField != null) UnityEngine.Object.Destroy(bedField);
            if (biomeField != null) UnityEngine.Object.Destroy(biomeField);
            slopeField = Field("Slopes", f.Res, GraphicsFormat.R16G16_SFloat, f.Slopes.Length, FilterMode.Trilinear, 4);
            bedField = Field("Bed", f.Res, GraphicsFormat.R32_SFloat, f.Bed.Length, FilterMode.Trilinear, 1);
            biomeField = Field("Biomes", f.N, GraphicsFormat.R16G16B16A16_SFloat, 1, FilterMode.Bilinear, 1);
            Shader.SetGlobalTexture("_StabbySlope", slopeField);
            Shader.SetGlobalTexture("_StabbyBed", bedField);
            Shader.SetGlobalTexture("_StabbyBiome", biomeField);
        }

        public static void FieldGlobals(FieldLook f)
        {
            Shader.SetGlobalVector("_StabbyFieldUV", new Vector4(1f / (f.TexelM * f.Res), 0.5f / f.Res, 1f / (f.CellM * f.N), 0.5f / f.N));
            Shader.SetGlobalVector("_StabbySand", new Vector4(Sand.x, Sand.y, Sand.z, 0));
        }

        public sealed class FieldUpload
        {
            readonly FieldLook f;
            readonly int budget;
            int part, mip, row;
            Texture2D stage;

            public FieldUpload(FieldLook field, int budgetBytes)
            {
                f = field;
                budget = budgetBytes;
            }

            public bool Step()
            {
                if (part == 0)
                {
                    CreateField(f);
                    part = 1;
                    return false;
                }
                if (part == 1 && mip == 0 && row == 0) FieldGlobals(f);
                int spent = 0;
                while (part <= 3)
                {
                    int bytes = part == 1 ? Slice(f.Slopes[mip], 2, slopeField, f.Slopes.Length)
                        : part == 2 ? Slice(f.Bed[mip], 1, bedField, f.Bed.Length)
                        : Slice(f.Biome, 4, biomeField, 1);
                    if ((spent += bytes) >= budget) return false;
                }
                if (stage != null) UnityEngine.Object.Destroy(stage);
                stage = null;
                return true;
            }

            int Slice<T>(T[] src, int channels, RenderTexture target, int mips) where T : struct
            {
                int bpp = channels * Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<T>();
                int w = Math.Max(1, target.width >> mip), rows = Math.Clamp(budget / (w * bpp), 1, w), n = Math.Min(rows, w - row);
                var format = target.graphicsFormat;
                if (stage == null) stage = new Texture2D(w, n, format, TextureCreationFlags.DontInitializePixels) { name = "Field stage" };
                else if (stage.width != w || stage.height != n || stage.graphicsFormat != format) stage.Reinitialize(w, n, format, false);
                stage.SetPixelData(src, 0, row * w * channels);
                stage.Apply(false, false);
                Graphics.CopyTexture(stage, 0, 0, 0, 0, w, n, target, 0, mip, 0, row);
                row += n;
                if (row >= w)
                {
                    row = 0;
                    if (++mip >= mips) { mip = 0; part++; }
                }
                return n * w * bpp;
            }
        }

        static RenderTexture slopeField, bedField, biomeField;

        static RenderTexture Field(string name, int res, GraphicsFormat format, int mips, FilterMode filter, int aniso)
        {
            var d = new RenderTextureDescriptor(res, res, format, GraphicsFormat.None) { useMipMap = mips > 1, autoGenerateMips = false, mipCount = mips, msaaSamples = 1 };
            var rt = new RenderTexture(d) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = filter, anisoLevel = aniso };
            rt.Create();
            return rt;
        }

        public static StrataLook Strata(GeneratedWorld w, MaterialLibrary lib, IReadOnlyList<BiomeDef> defs, float[] offsets = null)
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
            var coatR = Vector4.zero;
            for (int k = 0; k < d.Coats.Length; k++)
            {
                var c = d.Coats[k];
                coatA[k] = new Vector4(c.BumpAmp, 1f / c.BumpWavelengthM, c.GrainAmp, 0);
                coatB[k] = new Vector4(c.FeatureAmp, 1f / c.FeatureWavelengthM, c.Parallax, c.AlbedoVar);
                coatR[k] = c.Roughness;
            }
            float[] off = offsets, water = null;
            byte[] coat = null, forest = null;
            Parallel.Invoke(
                () => off ??= lib.Offsets(w),
                () => coat = d.CoatWeights(w),
                () => forest = SurfaceDetail.ForestWeights(w, w.Objects, defs),
                () => water = WaterLevel(w));
            return new StrataLook
            {
                Offsets = off, Bands = bands, Hardness = hard, PeriodM = lib.PeriodM,
                Coat = coat, Forest = forest, WaterLevel = water, N = w.Config.Size, CellM = w.Config.CellMeters,
                Surface = new Vector4(d.FadePixels, 1f / d.ParallaxFootprintM, math.sin(wind), math.cos(wind)),
                Detail = new Vector4(1f / lib.Look.DetailWavelengthM, lib.Look.DetailLacunarity, lib.Look.DetailOctaves, lib.Look.DetailGain),
                Wobble = new Vector4(d.StrataWobbleM, 1f / d.StrataWobbleWavelengthM, d.CoatPatchAmp, 1f / d.CoatPatchWavelengthM),
                RockA = new Vector4(r.GrainAmp, 0, r.BumpAmp, 1f / r.BumpWavelengthM),
                RockB = new Vector4(1f / r.BedSpacingM, r.BedDepthM, 1f / r.JointSpacingM, r.JointDepthM),
                RockC = new Vector4(r.Parallax, 1f / MaterialLibrary.RockTypePeriodM, 1, 0),
                Look = new Vector4(lib.Look.Saturation, lib.Look.Roughness, lib.Look.Specular, lib.Look.RoughnessVar),
                CoatRough = coatR,
                CoatA = coatA, CoatB = coatB,
                Liquids = LiquidLook(lib),
                Tints = TintLook(lib),
                Sinter = SinterLook(lib.Sinter, lib.Mud, lib.Peat, lib.Find("ice"), lib.Find("dirt-road"), lib.Find("rut-dirt"), lib.Find("obsidian")),
                Kit = KitLook(lib, true),
                KitSrgb = KitLook(lib, false),
            };
        }

        public static float[] WaterLevel(GeneratedWorld w)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            var level = new float[n * n];
            Parallel.For(0, n, y =>
            {
                for (int x = 0; x < n; x++)
                    level[y * n + x] = math.max(0, WaterGeometry.LakeHeight(w, x, y));
            });
            foreach (var r in w.Water.Rivers)
                for (int k = 0; k < r.Cells.Count; k++)
                {
                    float s = Hydrology.Stage(w.Config, r, k) + WaterGeometry.RiverLift;
                    int reach = (int)math.ceil(math.max(r.Width[k] * 0.5f, 6f) / cm);
                    int cx = (int)math.round(r.X[k]), cy = (int)math.round(r.Y[k]);
                    for (int y = math.max(cy - reach, 0); y <= math.min(cy + reach, n - 1); y++)
                        for (int x = math.max(cx - reach, 0); x <= math.min(cx + reach, n - 1); x++)
                            level[y * n + x] = math.max(level[y * n + x], s);
                }
            return level;
        }

        static Vector4[] SinterLook(SinterDef d, MudDef m, PeatDef p, LibraryMaterial ice, LibraryMaterial road, LibraryMaterial rut, LibraryMaterial obsidian)
        {
            Vector4 C(uint c, float w) { var l = ToLinear(Unpack(c)); return new Vector4(l.x, l.y, l.z, w); }
            return new[] { C(d.White, 0), C(d.Ochre, d.Roughness), C(d.Orange, 0), C(m.Surface, m.Roughness), C(m.Clay, 0), C(m.Grey, 0), C(p.Wet, p.Roughness), C(p.Dark, 0), C(p.Fibre, 0), C(ice?.Color ?? 0xcfe2ecu, ice?.Translucency ?? 0), C(road?.Color ?? 0x8a7860u, 0.85f), C(rut?.Color ?? 0x5e5040u, 0.7f), C(obsidian?.Color ?? 0x17141cu, 0) };
        }

        static Vector4[] KitLook(MaterialLibrary lib, bool linear)
        {
            var c = lib.KitPalette();
            var v = new Vector4[c.Length];
            for (int i = 0; i < c.Length; i++)
            {
                var u = Unpack(c[i]);
                var l = linear ? ToLinear(u) : u;
                v[i] = new Vector4(l.x, l.y, l.z, 1);
            }
            return v;
        }

        public const int LiquidRows = 10;

        static Vector4 TintLook(MaterialLibrary lib)
        {
            var v = Vector4.zero;
            var slots = lib.TintSlots();
            for (int t = 0; t < slots.Length && t < LiquidDef.TintSlots; t++) v[t] = slots[t];
            return v;
        }

        static Vector4[] LiquidLook(MaterialLibrary lib)
        {
            var v = new Vector4[LiquidDef.Max * LiquidRows];
            for (int i = 0; i < lib.Liquids.Count && i < LiquidDef.Max; i++)
            {
                var w = lib.Liquids[i];
                float3 deep = ToLinear(Unpack(w.DeepColor)), shallow = ToLinear(Unpack(w.ShallowColor)), film = ToLinear(Unpack(w.FilmColor)), glow = ToLinear(Unpack(w.EmissionColor)) * w.Emission, spray = ToLinear(Unpack(w.SprayColor));
                int b = i * LiquidRows;
                v[b] = new Vector4(deep.x, deep.y, deep.z, 1f / w.ClarityM);
                v[b + 1] = new Vector4(shallow.x, shallow.y, shallow.z, 1f / w.ShoreM);
                v[b + 2] = new Vector4(1f / w.WaveScaleM, w.WaveSpeed, w.WaveStrength, w.FoamWidthM);
                v[b + 3] = new Vector4(1f / w.SwellScaleM, w.SwellStrength, 1f / w.BedFieldM, w.OpaqueDepthM > 0 ? 1f / w.OpaqueDepthM : -1f);
                v[b + 4] = new Vector4(w.FlowSpeed, 1f / w.RapidsSlope, w.LakeSwell, w.GroundTint);
                v[b + 5] = new Vector4(w.Opacity, w.Viscosity, w.Bubbles, w.Film);
                v[b + 6] = new Vector4(film.x, film.y, film.z, w.Silt);
                v[b + 7] = new Vector4(w.FallSpeed, w.SheetOpacity, w.Spray, w.Roughness);
                v[b + 8] = new Vector4(glow.x, glow.y, glow.z, w.Froth);
                v[b + 9] = new Vector4(spray.x, spray.y, spray.z, w.SprayAmount);
            }
            return v;
        }
    }
}
