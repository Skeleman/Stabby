using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public struct Rgb
    {
        public float R, G, B;

        public Rgb(float r, float g, float b) { R = r; G = g; B = b; }

        public static Rgb Lerp(Rgb a, Rgb b, float t) => new Rgb(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
    }

    public sealed class SkyStop
    {
        public float SunHeight;
        public Rgb Sky, Zenith, Glow, AmbientSky, AmbientGround, Sun;
        public float SunIntensity;
    }

    public sealed class Orbit
    {
        public float NoonElevationDeg, NoonAzimuthDeg, HourOffset;
    }

    public sealed class CloudDef
    {
        public const float NoisePeriod = 256;
        public float Coverage = 0.45f, Density = 1.2f, HeightM = 1800, ThicknessM = 700, ScaleM = 3200, WindDirDeg = 250, WindSpeedMs = 9, Churn = 1.6f;

        public void Drift(double clockS, out float x, out float z, out float churnX, out float churnZ)
        {
            double a = WindDirDeg * Math.PI / 180, d = clockS * WindSpeedMs / ScaleM;
            double dx = Math.Sin(a) * d, dz = Math.Cos(a) * d;
            x = Wrap(dx);
            z = Wrap(dz);
            churnX = Wrap(dx * Churn);
            churnZ = Wrap(dz * Churn);
        }

        static float Wrap(double v) => (float)(v - Math.Floor(v / NoisePeriod) * NoisePeriod);

        public static void Validate(List<string> e, CloudDef c)
        {
            if (c == null) { e.Add("sky.clouds: required"); return; }
            if (!(c.Coverage >= 0 && c.Coverage <= 1)) e.Add($"sky.clouds.coverage {c.Coverage} outside 0..1");
            if (!(c.Density >= 0 && c.Density <= 8)) e.Add($"sky.clouds.density {c.Density} outside 0..8");
            if (!(c.HeightM >= 200 && c.HeightM <= 12000)) e.Add($"sky.clouds.heightM {c.HeightM} outside 200..12000");
            if (!(c.ThicknessM >= 50 && c.ThicknessM <= 5000)) e.Add($"sky.clouds.thicknessM {c.ThicknessM} outside 50..5000");
            if (!(c.ScaleM >= 100 && c.ScaleM <= 50000)) e.Add($"sky.clouds.scaleM {c.ScaleM} outside 100..50000");
            if (!(c.WindDirDeg >= 0 && c.WindDirDeg < 360)) e.Add($"sky.clouds.windDirDeg {c.WindDirDeg} outside 0..360");
            if (!(c.WindSpeedMs >= 0 && c.WindSpeedMs <= 60)) e.Add($"sky.clouds.windSpeedMs {c.WindSpeedMs} outside 0..60");
            if (!(c.Churn >= 0 && c.Churn <= 4)) e.Add($"sky.clouds.churn {c.Churn} outside 0..4");
        }
    }

    public struct SkyState
    {
        public float Hour, SunHeight, MoonHeight;
        public float DirX, DirY, DirZ, SunX, SunY, SunZ, MoonX, MoonY, MoonZ;
        public Rgb Light, Sky, Zenith, Glow, AmbientSky, AmbientGround;
        public float Intensity, Phase, Illumination, MoonGlow, Stars;
        public bool Moon;
    }

    public sealed class SkyDef
    {
        public float DayLengthS = 1440, StartHour = 9;
        public Orbit Sun = new Orbit { NoonElevationDeg = 38, NoonAzimuthDeg = 320 };
        public Orbit Moon = new Orbit { NoonElevationDeg = 52, NoonAzimuthDeg = 20, HourOffset = 12 };
        public Rgb MoonColor = new Rgb(0.62f, 0.72f, 1f);
        public float MoonIntensity = 0.18f, MoonSwitchHeight = -0.06f, MoonFadeHeight = 0.1f;
        public float MoonCycleDays = 8, MoonStartPhase = 0.5f, NewMoonLight = 0.03f, NewMoonAmbient = 0.2f, MoonGlow = 0.02f, StarBrightness = 0.01f;
        public const float FullMoon = 0.5f;
        public CloudDef Clouds = new CloudDef();
        public WeatherDef Weather = new WeatherDef();
        public List<SkyStop> Stops = new List<SkyStop>();

        public float Hour(double clockS)
        {
            double h = StartHour + clockS / DayLengthS * 24.0;
            h -= Math.Floor(h / 24.0) * 24.0;
            return (float)h;
        }

        public float Phase(double clockS)
        {
            double p = MoonStartPhase + clockS / DayLengthS / MoonCycleDays;
            return (float)(p - Math.Floor(p));
        }

        public static float Illumination(float phase) => (float)((1 - Math.Cos(phase * 2 * Math.PI)) * 0.5);

        public static void Toward(Orbit o, float hour, out float x, out float y, out float z)
        {
            double e = o.NoonElevationDeg * Math.PI / 180, a = o.NoonAzimuthDeg * Math.PI / 180;
            double t = (hour + o.HourOffset - 12) / 24.0 * 2 * Math.PI;
            double c = Math.Cos(t), s = Math.Sin(t);
            double nx = Math.Sin(a) * Math.Cos(e), ny = Math.Sin(e), nz = Math.Cos(a) * Math.Cos(e);
            double wx = -Math.Cos(a), wz = Math.Sin(a);
            x = (float)(c * nx + s * wx);
            y = (float)(c * ny);
            z = (float)(c * nz + s * wz);
        }

        public SkyStop Blend(float sunHeight)
        {
            var r = new SkyStop();
            BlendInto(sunHeight, r);
            return r;
        }

        void BlendInto(float sunHeight, SkyStop r)
        {
            int n = Stops.Count;
            if (n == 0) { Fill(r, Empty, sunHeight); return; }
            if (sunHeight >= Stops[0].SunHeight) { Fill(r, Stops[0], sunHeight); return; }
            if (sunHeight <= Stops[n - 1].SunHeight) { Fill(r, Stops[n - 1], sunHeight); return; }
            int i = 1;
            while (Stops[i].SunHeight > sunHeight) i++;
            SkyStop hi = Stops[i - 1], lo = Stops[i];
            float t = (hi.SunHeight - sunHeight) / (hi.SunHeight - lo.SunHeight);
            r.SunHeight = sunHeight;
            r.Sky = Rgb.Lerp(hi.Sky, lo.Sky, t);
            r.Zenith = Rgb.Lerp(hi.Zenith, lo.Zenith, t);
            r.Glow = Rgb.Lerp(hi.Glow, lo.Glow, t);
            r.AmbientSky = Rgb.Lerp(hi.AmbientSky, lo.AmbientSky, t);
            r.AmbientGround = Rgb.Lerp(hi.AmbientGround, lo.AmbientGround, t);
            r.Sun = Rgb.Lerp(hi.Sun, lo.Sun, t);
            r.SunIntensity = hi.SunIntensity + (lo.SunIntensity - hi.SunIntensity) * t;
        }

        static readonly SkyStop Empty = new SkyStop();

        static void Fill(SkyStop r, SkyStop s, float h)
        {
            r.SunHeight = h; r.Sky = s.Sky; r.Zenith = s.Zenith; r.Glow = s.Glow; r.AmbientSky = s.AmbientSky; r.AmbientGround = s.AmbientGround; r.Sun = s.Sun; r.SunIntensity = s.SunIntensity;
        }

        [ThreadStatic] static SkyStop blended;

        public SkyState At(float hour) => At(hour, FullMoon);

        public SkyState At(float hour, float phase)
        {
            Toward(Sun, hour, out float sx, out float sy, out float sz);
            Toward(Moon, hour, out float mx, out float my, out float mz);
            var b = blended ??= new SkyStop();
            BlendInto(sy, b);
            float lit = Illumination(phase);
            float dark = Clamp01((MoonSwitchHeight - sy) / MoonFadeHeight);
            float ambient = 1 + (NewMoonAmbient + (1 - NewMoonAmbient) * lit - 1) * dark;
            var st = new SkyState
            {
                Hour = hour, SunHeight = sy, MoonHeight = my, Phase = phase, Illumination = lit,
                SunX = sx, SunY = sy, SunZ = sz, MoonX = mx, MoonY = my, MoonZ = mz,
                MoonGlow = MoonGlow * lit * dark * Clamp01(my / MoonFadeHeight + 0.5f), Stars = StarBrightness * dark * (1 - 0.6f * lit),
                Sky = Scale(b.Sky, ambient), Zenith = Scale(b.Zenith, ambient), Glow = Scale(b.Glow, ambient), AmbientSky = Scale(b.AmbientSky, ambient), AmbientGround = Scale(b.AmbientGround, ambient),
            };
            if (sy > MoonSwitchHeight)
            {
                st.DirX = -sx; st.DirY = -sy; st.DirZ = -sz;
                st.Light = b.Sun;
                st.Intensity = b.SunIntensity;
                return st;
            }
            float rise = dark * Clamp01(my / MoonFadeHeight);
            st.Moon = true;
            st.DirX = -mx; st.DirY = -my; st.DirZ = -mz;
            st.Light = MoonColor;
            st.Intensity = MoonIntensity * rise * (NewMoonLight + (1 - NewMoonLight) * lit);
            return st;
        }

        static Rgb Scale(Rgb c, float k) => new Rgb(c.R * k, c.G * k, c.B * k);

        static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

        public static List<string> Validate(SkyDef d)
        {
            var e = new List<string>();
            if (d == null) { e.Add("sky: required"); return e; }
            if (!(d.DayLengthS >= 10 && d.DayLengthS <= 86400)) e.Add($"sky.dayLengthS {d.DayLengthS} outside 10..86400");
            if (!(d.StartHour >= 0 && d.StartHour < 24)) e.Add($"sky.startHour {d.StartHour} outside 0..24");
            CheckOrbit(e, "sky.sun", d.Sun);
            CheckOrbit(e, "sky.moon", d.Moon);
            CheckColor(e, "sky.moonColor", d.MoonColor);
            CloudDef.Validate(e, d.Clouds);
            WeatherDef.Validate(e, d.Weather);
            if (!(d.MoonIntensity >= 0 && d.MoonIntensity <= 2)) e.Add($"sky.moonIntensity {d.MoonIntensity} outside 0..2");
            if (!(d.MoonSwitchHeight >= -0.5f && d.MoonSwitchHeight <= 0)) e.Add($"sky.moonSwitchHeight {d.MoonSwitchHeight} outside -0.5..0");
            if (!(d.MoonFadeHeight > 0 && d.MoonFadeHeight <= 1)) e.Add($"sky.moonFadeHeight {d.MoonFadeHeight} outside 0..1");
            if (!(d.MoonCycleDays >= 1 && d.MoonCycleDays <= 365)) e.Add($"sky.moonCycleDays {d.MoonCycleDays} outside 1..365");
            if (!(d.MoonStartPhase >= 0 && d.MoonStartPhase < 1)) e.Add($"sky.moonStartPhase {d.MoonStartPhase} outside 0..1");
            if (!(d.NewMoonLight >= 0 && d.NewMoonLight <= 1)) e.Add($"sky.newMoonLight {d.NewMoonLight} outside 0..1");
            if (!(d.NewMoonAmbient >= 0 && d.NewMoonAmbient <= 1)) e.Add($"sky.newMoonAmbient {d.NewMoonAmbient} outside 0..1");
            if (!(d.MoonGlow >= 0 && d.MoonGlow <= 0.2f)) e.Add($"sky.moonGlow {d.MoonGlow} outside 0..0.2");
            if (!(d.StarBrightness >= 0 && d.StarBrightness <= 0.05f)) e.Add($"sky.starBrightness {d.StarBrightness} outside 0..0.05");
            if (d.Stops == null || d.Stops.Count < 2) { e.Add("sky.stops: at least two stops required"); return e; }
            for (int i = 0; i < d.Stops.Count; i++)
            {
                var s = d.Stops[i];
                string p = $"sky.stops[{i}]";
                if (!(s.SunHeight >= -1 && s.SunHeight <= 1)) e.Add($"{p}.sunHeight {s.SunHeight} outside -1..1");
                if (i > 0 && !(s.SunHeight < d.Stops[i - 1].SunHeight)) e.Add($"{p}.sunHeight must fall below the previous stop");
                CheckColor(e, p + ".sky", s.Sky);
                CheckColor(e, p + ".zenith", s.Zenith);
                CheckColor(e, p + ".glow", s.Glow);
                CheckColor(e, p + ".ambientSky", s.AmbientSky);
                CheckColor(e, p + ".ambientGround", s.AmbientGround);
                CheckColor(e, p + ".sun", s.Sun);
                if (!(s.SunIntensity >= 0 && s.SunIntensity <= 2)) e.Add($"{p}.sunIntensity {s.SunIntensity} outside 0..2");
            }
            if (e.Count == 0 && d.Blend(d.MoonSwitchHeight).SunIntensity > 1e-4f) e.Add($"sky: sun intensity must reach 0 by moonSwitchHeight {d.MoonSwitchHeight}");
            return e;
        }

        static void CheckOrbit(List<string> e, string name, Orbit o)
        {
            if (o == null) { e.Add(name + ": required"); return; }
            if (!(o.NoonElevationDeg > 0 && o.NoonElevationDeg <= 90)) e.Add($"{name}.noonElevationDeg {o.NoonElevationDeg} outside 0..90");
            if (!(o.NoonAzimuthDeg >= 0 && o.NoonAzimuthDeg < 360)) e.Add($"{name}.noonAzimuthDeg {o.NoonAzimuthDeg} outside 0..360");
            if (!(o.HourOffset >= 0 && o.HourOffset < 24)) e.Add($"{name}.hourOffset {o.HourOffset} outside 0..24");
        }

        internal static void CheckColor(List<string> e, string name, Rgb c)
        {
            if (!(c.R >= 0 && c.R <= 1 && c.G >= 0 && c.G <= 1 && c.B >= 0 && c.B <= 1)) e.Add($"{name} channels outside 0..1");
        }
    }
}
