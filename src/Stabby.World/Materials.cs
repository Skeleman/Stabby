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

    public sealed class SurfaceDetail
    {
        public float StrataWobbleM, StrataWobbleWavelengthM;
        public float CoatPatchAmp, CoatPatchWavelengthM;
        public float CoatGrainAmp, CoatGrainWavelengthM;
        public float RockGrainAmp, RockGrainWavelengthM;
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
                foreach (var (name, a) in new[] { ("coatPatchAmp", d.CoatPatchAmp), ("coatGrainAmp", d.CoatGrainAmp), ("rockGrainAmp", d.RockGrainAmp) })
                    if (!(a >= 0 && a <= 1)) errors.Add($"surface.{name} {a} outside 0..1");
                foreach (var (name, l) in new[] { ("strataWobbleWavelengthM", d.StrataWobbleWavelengthM), ("coatPatchWavelengthM", d.CoatPatchWavelengthM), ("coatGrainWavelengthM", d.CoatGrainWavelengthM), ("rockGrainWavelengthM", d.RockGrainWavelengthM) })
                    if (!(l >= 0.1f && l <= 1000)) errors.Add($"surface.{name} {l} outside 0.1..1000");
            }
            return errors;
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
