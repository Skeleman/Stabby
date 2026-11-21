using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class WindDef
    {
        public float DirectionDeg = 60f;
        public float Strength = 0.35f;
        public float GustStrength = 0.5f;
        public float GustScaleM = 24f;
        public float GustSpeed = 6f;
        public float SwayHz = 0.9f;
    }

    public sealed class GrassDef
    {
        public float PerM2 = 48f;
        public float NearM = 6f;
        public float ReachM = 45f;
        public float HeightMinM = 0.25f, HeightMaxM = 0.6f;
        public float WidthM = 0.035f;
        public float Bend = 0.35f;
        public float TipLight = 0.45f;
        public float Translucency = 0.35f;
        public float CoatMin = 0.35f;
        public float SlopeMax = 0.7f;
        public int MaxBlades = 262144;
    }

    public sealed class FoliageDef
    {
        public WindDef Wind = new WindDef();
        public GrassDef Grass = new GrassDef();

        public void Validate(List<string> errors)
        {
            var w = Wind;
            if (w.Strength < 0 || w.Strength > 2) errors.Add("foliage wind: strength must be 0-2");
            if (w.GustStrength < 0 || w.GustStrength > 2) errors.Add("foliage wind: gustStrength must be 0-2");
            if (w.GustScaleM <= 0 || w.GustScaleM > 1000) errors.Add("foliage wind: gustScaleM must be above 0, at most 1000");
            if (w.GustSpeed < 0 || w.GustSpeed > 50) errors.Add("foliage wind: gustSpeed must be 0-50");
            if (w.SwayHz < 0 || w.SwayHz > 5) errors.Add("foliage wind: swayHz must be 0-5");
            var g = Grass;
            if (g.PerM2 < 0 || g.PerM2 > 400) errors.Add("foliage grass: perM2 must be 0-400");
            if (g.NearM <= 0 || g.ReachM <= g.NearM || g.ReachM > 120) errors.Add("foliage grass: nearM must be above 0 and below reachM, at most 120");
            if (g.HeightMinM <= 0 || g.HeightMaxM < g.HeightMinM || g.HeightMaxM > 3) errors.Add("foliage grass: heightMinM/heightMaxM must be increasing, 0-3 m");
            if (g.WidthM <= 0 || g.WidthM > 0.5f) errors.Add("foliage grass: widthM must be above 0, at most 0.5");
            if (g.Bend < 0 || g.Bend > 1.5f) errors.Add("foliage grass: bend must be 0-1.5");
            if (g.TipLight < 0 || g.TipLight > 2) errors.Add("foliage grass: tipLight must be 0-2");
            if (g.Translucency < 0 || g.Translucency > 1) errors.Add("foliage grass: translucency must be 0-1");
            if (g.CoatMin < 0 || g.CoatMin >= 1) errors.Add("foliage grass: coatMin must be in 0-1");
            if (g.SlopeMax <= 0 || g.SlopeMax > 3) errors.Add("foliage grass: slopeMax must be above 0, at most 3");
            if (g.MaxBlades < 1024 || g.MaxBlades > 4194304) errors.Add("foliage grass: maxBlades must be 1024-4194304");
        }
    }
}
