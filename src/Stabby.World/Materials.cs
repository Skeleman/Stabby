using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class StratumDef
    {
        public string Id;
        public string Name;
        public uint Color;
        public float ThicknessM;
        public float Hardness;
    }

    public sealed class RockSurface
    {
        public float GrainAmp;
        public float BumpAmp, BumpWavelengthM;
        public float BedSpacingM, BedDepthM;
        public float JointSpacingM, JointDepthM;
        public float Parallax;
    }

    public sealed class CoatDef
    {
        public string Id;
        public List<string> Biomes = new List<string>();
        public float BumpAmp, BumpWavelengthM;
        public float GrainAmp;
        public float FeatureAmp, FeatureWavelengthM;
        public float Parallax, AlbedoVar, Roughness = float.NaN;
    }

    public sealed class SinterDef
    {
        public uint White = 0xd6cfbf, Ochre = 0xc99a4a, Orange = 0xc4642a;
        public float Roughness = 0.55f;
    }

    public sealed class MudDef
    {
        public uint Surface = 0x6c675d, Clay = 0xa49a84, Grey = 0x8c897f;
        public float Roughness = 0.3f;
    }

    public sealed class PeatDef
    {
        public uint Wet = 0x2a2016, Dark = 0x17110b, Fibre = 0x5a4630;
        public float Roughness = 0.25f;
    }

    public sealed class LookDef
    {
        public float NoiseContrast = 0.12f, AlbedoVar = 0.08f, DetailWavelengthM = 0.35f, DetailLacunarity = 2.7f, DetailGain = 0.55f;
        public int DetailOctaves = 5;
        public float Saturation = 0.65f, Roughness = 0.85f, Specular = 0.04f, RoughnessVar;

        public void Fill(CoatDef c)
        {
            if (float.IsNaN(c.GrainAmp)) c.GrainAmp = NoiseContrast;
            if (float.IsNaN(c.AlbedoVar)) c.AlbedoVar = AlbedoVar;
            if (float.IsNaN(c.Roughness)) c.Roughness = Roughness;
        }
    }

    public sealed class SurfaceDetail
    {
        public static readonly string[] CoatKinds = { "grass", "soil", "sand", "snow" };
        public const int Sand = 2;

        public float FadePixels, ParallaxFootprintM, WindDeg;
        public float StrataWobbleM, StrataWobbleWavelengthM;
        public float CoatPatchAmp, CoatPatchWavelengthM;
        public RockSurface Rock = new RockSurface();
        public CoatDef[] Coats = new CoatDef[CoatKinds.Length];

        public int CoatOf(string biomeId)
        {
            for (int k = 0; k < Coats.Length; k++)
                if (Coats[k] != null && Coats[k].Biomes.Contains(biomeId)) return k;
            return -1;
        }

        public byte[] CoatWeights(GeneratedWorld w)
        {
            int cells = w.Height.Length;
            var kind = new int[w.BiomeIds == null ? 0 : w.BiomeIds.Length];
            for (int b = 0; b < kind.Length; b++) kind[b] = CoatOf(w.BiomeIds[b]);
            var px = new byte[cells * 4];
            for (int i = 0; i < cells; i++)
            {
                int k = w.Sea[i] || w.Biome == null || w.Biome[i] < 0 ? Sand : kind[w.Biome[i]];
                px[i * 4 + (k < 0 ? 0 : k)] = 255;
            }
            return px;
        }

        public static byte[] ForestWeights(GeneratedWorld w, ObjectLibrary objects, IReadOnlyList<BiomeDef> defs)
        {
            int n = w.Config.Size, cells = w.Height.Length, biomes = w.BiomeIds == null ? 0 : w.BiomeIds.Length;
            var px = new byte[cells * 4];
            if (objects == null || w.Biome == null || biomes == 0) return px;
            var trees = new List<ObjectDef>();
            var leaf = new List<bool>();
            foreach (var d in objects.Objects)
                foreach (var p in d.Parts)
                    if (p.Shape == ObjectPart.Broadleaf || p.Shape == ObjectPart.Conifer) { trees.Add(d); leaf.Add(p.Shape == ObjectPart.Broadleaf); break; }
            var reach = new float[trees.Count * biomes];
            for (int t = 0; t < trees.Count; t++)
            {
                var pl = trees[t].Placement;
                float a = pl.SizeMinM, b = pl.SizeMaxM, spread = (float)Math.PI * 0.25f * (a * a + a * b + b * b) / 3f;
                for (int k = 0; k < biomes; k++)
                {
                    if (pl.Biomes.Count > 0 && !pl.Biomes.Contains(w.BiomeIds[k])) continue;
                    float scale = 1f;
                    foreach (var bd in pl.BiomeDensity) if (bd.Biome == w.BiomeIds[k]) scale = bd.Scale;
                    reach[t * biomes + k] = pl.PerHectare * scale * spread / 10000f;
                }
            }
            var wets = new float[biomes];
            for (int k = 0; k < biomes; k++)
                foreach (var bd in defs)
                    if (bd.Id == w.BiomeIds[k])
                    {
                        float rain = 0.5f * (bd.PrecipMinMm + bd.PrecipMaxMm), temp = 0.5f * (bd.TempMinC + bd.TempMaxC);
                        wets[k] = Saturate((rain - 900f) / 1700f) * Saturate((temp + 2f) / 6f) * Saturate((26f - temp) / 6f);
                    }
            var raw = new float[cells * 3];
            float cm = w.Config.CellMeters;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x, k = w.Biome[i];
                    if (w.Sea[i] || k < 0) continue;
                    float gx = (w.Height[y * n + Math.Min(x + 1, n - 1)] - w.Height[y * n + Math.Max(x - 1, 0)]) / (2 * cm);
                    float gz = (w.Height[Math.Min(y + 1, n - 1) * n + x] - w.Height[Math.Max(y - 1, 0) * n + x]) / (2 * cm);
                    float slope = (float)Math.Sqrt(gx * gx + gz * gz), broad = 0, needle = 0;
                    for (int t = 0; t < trees.Count; t++)
                    {
                        float r = reach[t * biomes + k];
                        if (r <= 0) continue;
                        r *= Saturate((trees[t].Placement.SlopeMax * 1.2f - slope) / (trees[t].Placement.SlopeMax * 0.6f));
                        if (leaf[t]) broad += r; else needle += r;
                    }
                    float cover = 1f - (float)FMath.Exp(-(broad + needle));
                    float sum = Math.Max(broad + needle, 1e-6f);
                    raw[i * 3] = cover * broad / sum;
                    raw[i * 3 + 1] = cover * needle / sum;
                    raw[i * 3 + 2] = wets[k];
                }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    for (int c = 0; c < 3; c++)
                    {
                        float s = 0, wsum = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = Math.Min(Math.Max(x + dx, 0), n - 1), yy = Math.Min(Math.Max(y + dy, 0), n - 1);
                                float wt = (dx == 0 ? 2 : 1) * (dy == 0 ? 2 : 1);
                                s += raw[(yy * n + xx) * 3 + c] * wt; wsum += wt;
                            }
                        px[i * 4 + c] = (byte)Math.Round(Saturate(s / wsum) * 255f);
                    }
                    px[i * 4 + 3] = 255;
                }
            return px;
        }

        static float Saturate(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }

    public sealed class LiquidDef
    {
        public const int SprayNone = 0, SprayMist = 1, SprayDust = 2, SprayShimmer = 3, Max = 8, TintSlots = 3;
        public static readonly string[] SprayNames = { "none", "mist", "dust", "shimmer" };
        public string Id;
        public List<string> Biomes = new List<string>();
        public uint DeepColor, ShallowColor, FilmColor, EmissionColor;
        public float ClarityM, ShoreM, FoamWidthM;
        public float WaveScaleM, WaveSpeed, WaveStrength;
        public float SwellScaleM, SwellStrength, LakeSwell;
        public float FlowSpeed, RapidsSlope, BedFieldM;
        public float Silt, Opacity, Viscosity, Bubbles, Film, Emission, Roughness;
        public int Spray;
        public float FallSpeed, SheetOpacity, SprayAmount, Froth;
        public uint SprayColor;
    }

    public sealed class LibraryMaterial
    {
        public string Id, Name;
        public uint Color;
        public float Glow, Translucency;
        public int GlowPattern;
        public static readonly string[] GlowPatterns = { "facet", "cap" };
    }

    public sealed class MaterialLibrary
    {
        public const float RockTypePeriodM = 40f;

        public static float Sedimentary(float offset)
        {
            float p = offset / RockTypePeriodM;
            return Grid.Smoothstep(0.3f, 0.7f, Math.Abs(p - (float)Math.Floor(p) - 0.5f) * 2f);
        }

        public SurfaceDetail Surface = new SurfaceDetail();
        public List<LiquidDef> Liquids = new List<LiquidDef>();
        public LiquidDef Water => Liquids != null && Liquids.Count > 0 ? Liquids[0] : null;

        public int Liquid(string id)
        {
            for (int i = 0; Liquids != null && i < Liquids.Count; i++) if (Liquids[i].Id == id) return i;
            return -1;
        }

        public int[] TintSlots()
        {
            var slots = new List<int>();
            for (int i = 0; Liquids != null && i < Liquids.Count; i++) if (Liquids[i].Biomes.Count > 0) slots.Add(i);
            return slots.ToArray();
        }

        public LookDef Look = new LookDef();
        public SinterDef Sinter = new SinterDef();
        public MudDef Mud = new MudDef();
        public PeatDef Peat = new PeatDef();
        public List<StratumDef> Strata = new List<StratumDef>();
        public List<LibraryMaterial> Materials = new List<LibraryMaterial>();
        public static readonly string[] KitNames = { "timber", "plank", "plaster", "thatch", "iron", "cloth", "ash", "fungus", "turf", "ember" };
        public static readonly int[] KitSlots = { 6, 1, 3, 4, 2, 4, 5, 10, 2, 2 };
        public const int KitColors = 39;
        public Dictionary<string, List<string>> Kit = new Dictionary<string, List<string>>();

        public LibraryMaterial Find(string id)
        {
            foreach (var m in Materials)
                if (m.Id == id) return m;
            return null;
        }

        public uint[] KitPalette()
        {
            var c = new uint[KitColors];
            int k = 0;
            foreach (var name in KitNames)
                foreach (var id in Kit[name]) c[k++] = Find(id).Color;
            return c;
        }
        public float OffsetAmplitudeM;
        public float OffsetWavelengthM;
        public float TiltPerKm;

        public void FillCoats()
        {
            if (Look == null || Surface == null || Surface.Coats == null) return;
            foreach (var c in Surface.Coats)
                if (c != null) Look.Fill(c);
        }

        public float PeriodM
        {
            get
            {
                float p = 0;
                foreach (var s in Strata) p += s.ThicknessM;
                return p;
            }
        }

        public int[] Bands(float stepM)
        {
            float period = PeriodM;
            var bands = new int[Math.Max(1, (int)Math.Round(period / stepM))];
            int layer = 0;
            float top = Strata[0].ThicknessM;
            for (int k = 0; k < bands.Length; k++)
            {
                float y = (k + 0.5f) * period / bands.Length;
                while (y > top && layer < Strata.Count - 1) top += Strata[++layer].ThicknessM;
                bands[k] = layer;
            }
            return bands;
        }

        public float[] Offsets(GeneratedWorld w) => Offsets(w.Config);

        public float[] Offsets(WorldConfig c)
        {
            int n = c.Size;
            float cm = c.CellMeters, scale = cm / Math.Max(1f, OffsetWavelengthM);
            var r = Rng.Stream(c.Seed, "strata");
            uint seed = r.NextU32();
            float tx = r.Range(-1f, 1f), ty = r.Range(-1f, 1f);
            float norm = (float)Math.Sqrt(tx * tx + ty * ty) + 1e-6f;
            tx *= TiltPerKm * cm / 1000f / norm;
            ty *= TiltPerKm * cm / 1000f / norm;
            var off = new float[n * n];
            Parallel.For(0, n, y =>
            {
                for (int x = 0; x < n; x++)
                    off[y * n + x] = OffsetAmplitudeM * Noise.Fbm(seed, x * scale, y * scale, 3) + tx * x + ty * y;
            });
            return off;
        }

        public static List<string> Validate(MaterialLibrary lib)
        {
            var errors = new List<string>();
            var look = lib.Look;
            if (look == null) errors.Add("look: required");
            else
            {
                Amp(errors, "look.noiseContrast", look.NoiseContrast);
                Amp(errors, "look.albedoVar", look.AlbedoVar);
                Len(errors, "look.detailWavelengthM", look.DetailWavelengthM);
                if (!(look.DetailLacunarity >= 1.5f && look.DetailLacunarity <= 4)) errors.Add($"look.detailLacunarity {look.DetailLacunarity} outside 1.5..4");
                if (look.DetailOctaves < 1 || look.DetailOctaves > 6) errors.Add($"look.detailOctaves {look.DetailOctaves} outside 1..6");
                Amp(errors, "look.detailGain", look.DetailGain);
                Amp(errors, "look.saturation", look.Saturation);
                Amp(errors, "look.roughness", look.Roughness);
                Amp(errors, "look.specular", look.Specular);
                Amp(errors, "look.roughnessVar", look.RoughnessVar);
            }
            if (lib.Sinter == null) errors.Add("sinter: required");
            else
            {
                Amp(errors, "sinter.roughness", lib.Sinter.Roughness);
            }
            if (lib.Mud == null) errors.Add("mud: required");
            else Amp(errors, "mud.roughness", lib.Mud.Roughness);
            if (lib.Peat == null) errors.Add("peat: required");
            else Amp(errors, "peat.roughness", lib.Peat.Roughness);
            if (lib.Strata.Count == 0) errors.Add("strata: at least one stratum required");
            var ids = new HashSet<string>();
            foreach (var s in lib.Strata)
            {
                if (string.IsNullOrEmpty(s.Id) || !ids.Add(s.Id)) errors.Add($"strata: id '{s.Id}' missing or repeated");
                if (!(s.ThicknessM >= 0.5f && s.ThicknessM <= 500f)) errors.Add($"strata {s.Id}: thicknessM {s.ThicknessM} outside 0.5..500");
                if (!(s.Hardness >= 0 && s.Hardness <= 0.99f)) errors.Add($"strata {s.Id}: hardness {s.Hardness} outside 0..0.99");
            }
            var names = new HashSet<string>();
            foreach (var m in lib.Materials)
            {
                if (string.IsNullOrEmpty(m.Id) || !names.Add(m.Id)) errors.Add($"materials: id '{m.Id}' missing or repeated");
                if (!(m.Glow >= 0f && m.Glow <= 1f)) errors.Add($"materials {m.Id}: glow {m.Glow} outside 0..1");
                if (!(m.Translucency >= 0f && m.Translucency <= 1f) || (m.Glow > 0f && m.Translucency > 0f)) errors.Add($"materials {m.Id}: translucency {m.Translucency} outside 0..1 or set with glow");
                if (m.GlowPattern < 0 || m.GlowPattern >= LibraryMaterial.GlowPatterns.Length) errors.Add($"materials {m.Id}: unknown glowPattern");
            }
            for (int i = 0; i < KitNames.Length; i++)
            {
                if (lib.Kit == null || !lib.Kit.TryGetValue(KitNames[i], out var kitIds) || kitIds == null) { errors.Add($"kit.{KitNames[i]}: required"); continue; }
                if (kitIds.Count != KitSlots[i]) errors.Add($"kit.{KitNames[i]}: {kitIds.Count} materials, expected {KitSlots[i]}");
                foreach (var id in kitIds)
                    if (!names.Contains(id)) errors.Add($"kit.{KitNames[i]}: unknown material '{id}'");
            }
            if (lib.Kit != null)
                foreach (var key in lib.Kit.Keys)
                    if (Array.IndexOf(KitNames, key) < 0) errors.Add($"kit: unknown kit material '{key}'");
            if (!(lib.OffsetAmplitudeM >= 0)) errors.Add("offsetAmplitudeM must be >= 0");
            if (!(lib.OffsetWavelengthM > 0)) errors.Add("offsetWavelengthM must be > 0");
            if (!(lib.TiltPerKm >= 0)) errors.Add("tiltPerKm must be >= 0");
            var d = lib.Surface;
            if (d == null) errors.Add("surface: required");
            else
            {
                if (!(d.StrataWobbleM >= 0 && d.StrataWobbleM <= 50)) errors.Add($"surface.strataWobbleM {d.StrataWobbleM} outside 0..50");
                if (!(d.FadePixels >= 0.5f && d.FadePixels <= 16)) errors.Add($"surface.fadePixels {d.FadePixels} outside 0.5..16");
                if (!(d.ParallaxFootprintM >= 0.001f && d.ParallaxFootprintM <= 1)) errors.Add($"surface.parallaxFootprintM {d.ParallaxFootprintM} outside 0.001..1");
                if (!(d.WindDeg >= 0 && d.WindDeg < 360)) errors.Add($"surface.windDeg {d.WindDeg} outside 0..360");
                Amp(errors, "surface.coatPatchAmp", d.CoatPatchAmp);
                Len(errors, "surface.strataWobbleWavelengthM", d.StrataWobbleWavelengthM);
                Len(errors, "surface.coatPatchWavelengthM", d.CoatPatchWavelengthM);
                var r = d.Rock;
                if (r == null) errors.Add("surface.rock: required");
                else
                {
                    Amp(errors, "surface.rock.grainAmp", r.GrainAmp);
                    Amp(errors, "surface.rock.bumpAmp", r.BumpAmp);
                    Len(errors, "surface.rock.bumpWavelengthM", r.BumpWavelengthM);
                    Len(errors, "surface.rock.bedSpacingM", r.BedSpacingM);
                    Len(errors, "surface.rock.jointSpacingM", r.JointSpacingM);
                    Depth(errors, "surface.rock.bedDepthM", r.BedDepthM);
                    Depth(errors, "surface.rock.jointDepthM", r.JointDepthM);
                    if (!(r.Parallax >= 0 && r.Parallax <= 2)) errors.Add($"surface.rock.parallax {r.Parallax} outside 0..2");
                }
                var seen = new HashSet<string>();
                for (int k = 0; k < SurfaceDetail.CoatKinds.Length; k++)
                {
                    var c = d.Coats == null || d.Coats.Length <= k ? null : d.Coats[k];
                    string n = "surface.coats." + SurfaceDetail.CoatKinds[k];
                    if (c == null) { errors.Add(n + ": required"); continue; }
                    foreach (var b in c.Biomes)
                        if (!seen.Add(b)) errors.Add($"{n}: biome '{b}' already has a coat");
                    Amp(errors, n + ".bumpAmp", c.BumpAmp);
                    Amp(errors, n + ".grainAmp", c.GrainAmp);
                    Amp(errors, n + ".featureAmp", c.FeatureAmp);
                    Amp(errors, n + ".albedoVar", c.AlbedoVar);
                    Amp(errors, n + ".roughness", c.Roughness);
                    Len(errors, n + ".bumpWavelengthM", c.BumpWavelengthM);
                    Len(errors, n + ".featureWavelengthM", c.FeatureWavelengthM);
                    if (!(c.Parallax >= 0 && c.Parallax <= 2)) errors.Add($"{n}.parallax {c.Parallax} outside 0..2");
                }
            }
            if (lib.Liquids == null || lib.Liquids.Count == 0) errors.Add("liquids: required");
            else
            {
                if (lib.Liquids[0].Id != "water") errors.Add("liquids: first entry must be water");
                if (lib.Liquids.Count > LiquidDef.Max) errors.Add($"liquids: {lib.Liquids.Count} entries, at most {LiquidDef.Max}");
                if (lib.Liquids[0].Biomes.Count > 0) errors.Add("liquids: water is the default and takes no biomes");
                if (lib.TintSlots().Length > LiquidDef.TintSlots) errors.Add($"liquids: {lib.TintSlots().Length} biome tints, at most {LiquidDef.TintSlots}");
                var tinted = new HashSet<string>();
                foreach (var l in lib.Liquids)
                    foreach (var b in l.Biomes)
                        if (!tinted.Add(b)) errors.Add($"liquids: biome {b} tinted twice");
                for (int i = 0; i < lib.Liquids.Count; i++)
                {
                    var w = lib.Liquids[i];
                    string n = w.Id ?? "";
                    if (n.Length == 0) errors.Add($"liquids[{i}].id required");
                    if (lib.Liquid(n) != i) errors.Add($"liquids: duplicate id {n}");
                    if (!(w.ClarityM >= 0.1f && w.ClarityM <= 100)) errors.Add($"{n}.clarityM {w.ClarityM} outside 0.1..100");
                    if (!(w.ShoreM >= 0.01f && w.ShoreM <= 5)) errors.Add($"{n}.shoreM {w.ShoreM} outside 0.01..5");
                    if (!(w.FoamWidthM >= 0 && w.FoamWidthM <= 50)) errors.Add($"{n}.foamWidthM {w.FoamWidthM} outside 0..50");
                    Len(errors, n + ".waveScaleM", w.WaveScaleM);
                    Len(errors, n + ".swellScaleM", w.SwellScaleM);
                    if (!(w.WaveSpeed >= 0 && w.WaveSpeed <= 10)) errors.Add($"{n}.waveSpeed {w.WaveSpeed} outside 0..10");
                    if (!(w.FlowSpeed >= 0 && w.FlowSpeed <= 10)) errors.Add($"{n}.flowSpeed {w.FlowSpeed} outside 0..10");
                    Amp(errors, n + ".waveStrength", w.WaveStrength);
                    Amp(errors, n + ".swellStrength", w.SwellStrength);
                    Amp(errors, n + ".lakeSwell", w.LakeSwell);
                    if (!(w.RapidsSlope > 0 && w.RapidsSlope <= 1)) errors.Add($"{n}.rapidsSlope {w.RapidsSlope} outside 0..1");
                    if (!(w.BedFieldM >= 1 && w.BedFieldM <= 5000)) errors.Add($"{n}.bedFieldM {w.BedFieldM} outside 1..5000");
                    Amp(errors, n + ".silt", w.Silt);
                    Amp(errors, n + ".opacity", w.Opacity);
                    Amp(errors, n + ".viscosity", w.Viscosity);
                    Amp(errors, n + ".bubbles", w.Bubbles);
                    Amp(errors, n + ".film", w.Film);
                    if (!(w.Emission >= 0 && w.Emission <= 8)) errors.Add($"{n}.emission {w.Emission} outside 0..8");
                    Amp(errors, n + ".roughness", w.Roughness);
                    Amp(errors, n + ".fall.sheetOpacity", w.SheetOpacity);
                    Amp(errors, n + ".fall.froth", w.Froth);
                    if (!(w.SprayAmount >= 0 && w.SprayAmount <= 2)) errors.Add($"{n}.fall.sprayAmount {w.SprayAmount} outside 0..2");
                    if (!(w.Spray >= 0 && w.Spray < LiquidDef.SprayNames.Length)) errors.Add($"{n}.fall.spray {w.Spray} unknown");
                    if (!(w.FallSpeed >= 0.05f && w.FallSpeed <= 4)) errors.Add($"{n}.fall.speed {w.FallSpeed} outside 0.05..4");
                }
            }
            return errors;
        }

        static void Amp(List<string> e, string name, float a)
        {
            if (!(a >= 0 && a <= 1)) e.Add($"{name} {a} outside 0..1");
        }

        static void Len(List<string> e, string name, float l)
        {
            if (!(l >= 0.01f && l <= 1000)) e.Add($"{name} {l} outside 0.01..1000");
        }

        static void Depth(List<string> e, string name, float d)
        {
            if (!(d >= 0 && d <= 2)) e.Add($"{name} {d} outside 0..2");
        }
    }

    public sealed class StrataField
    {
        const float BandStepM = 0.5f, BandBlendM = 4f;
        readonly float[] off, band;
        readonly float period, norm;

        public StrataField(WorldConfig c, MaterialLibrary lib)
        {
            off = lib.Offsets(c);
            period = lib.PeriodM;
            var ids = lib.Bands(BandStepM);
            int n = ids.Length, reach = (int)(BandBlendM / BandStepM);
            band = new float[n];
            for (int k = 0; k < n; k++)
            {
                float sum = 0;
                for (int d = -reach; d <= reach; d++) sum += lib.Strata[ids[((k + d) % n + n) % n]].Hardness;
                band[k] = sum / (2 * reach + 1) * c.StrataContrast;
            }
            double soft = 0, total = 0;
            foreach (var st in lib.Strata) { soft += (1 - st.Hardness * c.StrataContrast) * st.ThicknessM; total += st.ThicknessM; }
            norm = (float)(total / soft);
        }

        public float Hardness(int i, float h)
        {
            float u = (h + off[i]) / period;
            u = (u - (float)Math.Floor(u)) * band.Length;
            int k = Math.Min(band.Length - 1, (int)u);
            float t = u - k;
            return band[k] + (band[(k + 1) % band.Length] - band[k]) * t;
        }

        public float Softness(float hard) => (1 - hard) * norm;
    }
}
