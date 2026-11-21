using System;
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
        public float GrainAmp, GrainWavelengthM;
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
        public float GrainAmp, GrainWavelengthM;
        public float FeatureAmp, FeatureWavelengthM;
        public float Parallax, AlbedoVar;
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
    }

    public sealed class MaterialLibrary
    {
        public SurfaceDetail Surface = new SurfaceDetail();
        public List<StratumDef> Strata = new List<StratumDef>();
        public float OffsetAmplitudeM;
        public float OffsetWavelengthM;
        public float TiltPerKm;

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
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    off[y * n + x] = OffsetAmplitudeM * Noise.Fbm(seed, x * scale, y * scale, 3) + tx * x + ty * y;
            return off;
        }

        public static List<string> Validate(MaterialLibrary lib)
        {
            var errors = new List<string>();
            if (lib.Strata.Count == 0) errors.Add("strata: at least one stratum required");
            var ids = new HashSet<string>();
            foreach (var s in lib.Strata)
            {
                if (string.IsNullOrEmpty(s.Id) || !ids.Add(s.Id)) errors.Add($"strata: id '{s.Id}' missing or repeated");
                if (!(s.ThicknessM >= 0.5f && s.ThicknessM <= 500f)) errors.Add($"strata {s.Id}: thicknessM {s.ThicknessM} outside 0.5..500");
                if (!(s.Hardness >= 0 && s.Hardness <= 0.99f)) errors.Add($"strata {s.Id}: hardness {s.Hardness} outside 0..0.99");
            }
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
                    Len(errors, "surface.rock.grainWavelengthM", r.GrainWavelengthM);
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
                    Len(errors, n + ".bumpWavelengthM", c.BumpWavelengthM);
                    Len(errors, n + ".grainWavelengthM", c.GrainWavelengthM);
                    Len(errors, n + ".featureWavelengthM", c.FeatureWavelengthM);
                    if (!(c.Parallax >= 0 && c.Parallax <= 2)) errors.Add($"{n}.parallax {c.Parallax} outside 0..2");
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
