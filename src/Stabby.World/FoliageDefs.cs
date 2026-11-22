using System;
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
        public float PerM2 = 160f;
        public float NearPixels = 112.24f;
        public float ReachPixels = 7.015f;
        public float ThinPow = 1.2f;
        public float Widen = 0.9f;
        public float WidthMaxM = 0.25f;
        public float Lod1Pixels = 56.12f, Lod2Pixels = 22.45f;
        public float HeightMinM = 0.25f, HeightMaxM = 0.6f;
        public float WidthM = 0.035f;
        public float Bend = 0.35f;
        public float TipLight = 0.45f;
        public float Translucency = 0.35f;
        public float CoatMin = 0.35f;
        public float SlopeMax = 0.7f;
        public int MaxBlades = 131072;

        public float ReachAt(float pixels, float pixelsPerMetre) => Detail.Reach(HeightMaxM, pixels, pixelsPerMetre);
    }

    public sealed class TerrainViewDef
    {
        public const float ContactSizeM = 1f, ContactMaxTexelM = 1f;
        public float QuadPixels = 10.8f, ContactPixels = 7.3f, ContactTexelPixels = 1.8f;
        public float MistFadePixels = 4f, MistFullPixels = 16f, SteamFadePixels = 3f, SteamFullPixels = 12f, ColumnFadePixels = 2f, ColumnFullPixels = 8f;

        public float Allowed(float pixelsPerMetre, float quality) => QuadPixels / (pixelsPerMetre * Math.Max(quality, 0.1f));

        public void ContactWindow(float pixelsPerMetre, float quality, int texels, out float radiusM, out float texelM)
        {
            float q = Math.Max(quality, 0.1f);
            radiusM = Math.Min(Detail.Reach(ContactSizeM, ContactPixels / q, pixelsPerMetre), 0.5f * texels * ContactMaxTexelM);
            texelM = Math.Max(ContactTexelPixels / q * radiusM / pixelsPerMetre, 2f * radiusM / texels);
        }

        public void MistWindow(float quality, out float fadePixels, out float fullPixels)
        {
            float q = Math.Max(quality, 0.1f);
            fadePixels = MistFadePixels / q;
            fullPixels = MistFullPixels / q;
        }

        public void SteamWindow(float quality, out float fadePixels, out float fullPixels)
        {
            float q = Math.Max(quality, 0.1f);
            fadePixels = SteamFadePixels / q;
            fullPixels = SteamFullPixels / q;
        }

        public void ColumnWindow(float quality, out float fadePixels, out float fullPixels)
        {
            float q = Math.Max(quality, 0.1f);
            fadePixels = ColumnFadePixels / q;
            fullPixels = ColumnFullPixels / q;
        }

        public void Validate(List<string> errors)
        {
            if (QuadPixels < 1 || QuadPixels > 100) errors.Add("terrain: quadPixels must be 1-100");
            if (ContactPixels < 1 || ContactPixels > 100) errors.Add("terrain: contactPixels must be 1-100");
            if (ContactTexelPixels < 0.1f || ContactTexelPixels > 20) errors.Add("terrain: contactTexelPixels must be 0.1-20");
            if (MistFadePixels <= 0 || MistFullPixels <= MistFadePixels || MistFullPixels > 200) errors.Add("terrain: mistFullPixels must exceed mistFadePixels > 0, at most 200");
            if (SteamFadePixels <= 0 || SteamFullPixels <= SteamFadePixels || SteamFullPixels > 200) errors.Add("terrain: steamFullPixels must exceed steamFadePixels > 0, at most 200");
            if (ColumnFadePixels <= 0 || ColumnFullPixels <= ColumnFadePixels || ColumnFullPixels > 200) errors.Add("terrain: columnFullPixels must exceed columnFadePixels > 0, at most 200");
        }
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
            if (g.ReachPixels <= 0 || g.NearPixels <= g.ReachPixels || g.ReachAt(g.ReachPixels, Detail.ReferencePixelsPerMetre) > 120) errors.Add("foliage grass: nearPixels must exceed reachPixels > 0, reach at most 120 m at the reference view");
            if (g.ThinPow < 0.5f || g.ThinPow > 3) errors.Add("foliage grass: thinPow must be 0.5-3");
            if (g.Widen < 0 || g.Widen > 1) errors.Add("foliage grass: widen must be 0-1");
            if (g.WidthMaxM < g.WidthM || g.WidthMaxM > 1) errors.Add("foliage grass: widthMaxM must be at least widthM, at most 1");
            if (g.Lod2Pixels <= 0 || g.Lod1Pixels < g.Lod2Pixels) errors.Add("foliage grass: lod1Pixels/lod2Pixels must be falling and above 0");
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
