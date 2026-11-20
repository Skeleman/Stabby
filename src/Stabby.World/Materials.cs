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
    }

    public sealed class MaterialLibrary
    {
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

        public float[] Offsets(GeneratedWorld w)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, scale = cm / Math.Max(1f, OffsetWavelengthM);
            var r = Rng.Stream(w.Config.Seed, "strata");
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
            }
            if (!(lib.OffsetAmplitudeM >= 0)) errors.Add("offsetAmplitudeM must be >= 0");
            if (!(lib.OffsetWavelengthM > 0)) errors.Add("offsetWavelengthM must be > 0");
            if (!(lib.TiltPerKm >= 0)) errors.Add("tiltPerKm must be >= 0");
            return errors;
        }
    }
}
