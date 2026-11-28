using System.Text.Json;
using Stabby.Content;
using Stabby.World;

namespace Stabby.World.Tests;

public class SkyTests
{
    static readonly string Root = ContentLoader.FindContentRoot(AppContext.BaseDirectory);
    static readonly SkyDef Sky = ContentLoader.LoadSky(Root);

    [Fact]
    public void SkyContentMatchesSchema()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "schemas", "sky.schema.json")));
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "sky.json")));
        void Same(JsonElement s, JsonElement d)
        {
            var names = d.EnumerateObject().Select(p => p.Name).ToList();
            Assert.Empty(s.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).Except(names));
            Assert.Empty(names.Except(s.GetProperty("properties").EnumerateObject().Select(p => p.Name)));
        }
        var top = schema.RootElement;
        Same(top, data.RootElement);
        Assert.Equal(ContentLoader.SkySchemaVersion, top.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32());
        var orbit = top.GetProperty("$defs").GetProperty("orbit");
        Same(orbit, data.RootElement.GetProperty("sun"));
        Same(orbit, data.RootElement.GetProperty("moon"));
        var stop = top.GetProperty("properties").GetProperty("stops").GetProperty("items");
        foreach (var s in data.RootElement.GetProperty("stops").EnumerateArray()) Same(stop, s);
        Same(top.GetProperty("properties").GetProperty("clouds"), data.RootElement.GetProperty("clouds"));
        Assert.Equal(data.RootElement.GetProperty("clouds").GetProperty("coverage").GetSingle(), Sky.Clouds.Coverage);
        Assert.Equal(data.RootElement.GetProperty("stops").GetArrayLength(), Sky.Stops.Count);
    }

    [Fact]
    public void MiddayMatchesTheFixedSun()
    {
        var s = Sky.At(12);
        double e = 38 * Math.PI / 180, y = 140 * Math.PI / 180;
        Assert.False(s.Moon);
        Assert.Equal(Math.Sin(y) * Math.Cos(e), s.DirX, 4);
        Assert.Equal(-Math.Sin(e), s.DirY, 4);
        Assert.Equal(Math.Cos(y) * Math.Cos(e), s.DirZ, 4);
        Assert.Equal(0.8f, s.Intensity, 5);
        Assert.Equal(0.62f, s.Sky.R, 5);
        Assert.Equal(0.62f * 0.45f, s.AmbientSky.R, 5);
        Assert.Equal(0.88f, s.Light.B, 5);
    }

    [Fact]
    public void NightHandsTheLightToTheMoon()
    {
        var night = Sky.At(0);
        Assert.True(night.Moon);
        Assert.True(night.SunHeight < Sky.MoonSwitchHeight);
        Assert.True(night.MoonHeight > 0);
        Assert.True(night.DirY < 0);
        Assert.True(night.Intensity > 0 && night.Intensity < Sky.At(12).Intensity);
        Assert.True(night.Sky.R + night.Sky.G + night.Sky.B < 0.3f);
        for (float h = 0; h < 24; h += 0.05f)
        {
            var s = Sky.At(h);
            Assert.True(s.Intensity >= 0);
            Assert.True(s.Moon == s.SunHeight <= Sky.MoonSwitchHeight);
        }
    }

    [Fact]
    public void LightFadesThroughTheSwitch()
    {
        float prev = Sky.At(16).Intensity;
        for (float h = 16.01f; h < 21; h += 0.01f)
        {
            var s = Sky.At(h);
            Assert.True(Math.Abs(s.Intensity - prev) < 0.02f, $"jump at {h}: {prev} -> {s.Intensity}");
            prev = s.Intensity;
        }
    }

    [Fact]
    public void HourFollowsTheClock()
    {
        Assert.Equal(Sky.StartHour, Sky.Hour(0), 4);
        Assert.Equal((Sky.StartHour + 6) % 24, Sky.Hour(Sky.DayLengthS / 4), 3);
        Assert.Equal(Sky.StartHour, Sky.Hour(Sky.DayLengthS * 3), 3);
        Assert.InRange(Sky.Hour(-1), 0, 24);
    }

    [Fact]
    public void IlluminationFollowsThePhase()
    {
        Assert.Equal(0f, SkyDef.Illumination(0), 5);
        Assert.Equal(0.5f, SkyDef.Illumination(0.25f), 5);
        Assert.Equal(1f, SkyDef.Illumination(SkyDef.FullMoon), 5);
        Assert.Equal(0.5f, SkyDef.Illumination(0.75f), 5);
        float prev = -1;
        for (float p = 0; p <= SkyDef.FullMoon; p += 0.01f)
        {
            float i = SkyDef.Illumination(p);
            Assert.True(i >= prev - 1e-6f);
            Assert.Equal(i, SkyDef.Illumination(1 - p), 5);
            prev = i;
        }
        Assert.Equal(Sky.MoonStartPhase, Sky.Phase(0), 5);
        Assert.Equal(Sky.MoonStartPhase, Sky.Phase(Sky.DayLengthS * Sky.MoonCycleDays), 4);
        Assert.InRange(Sky.Phase(-1), 0, 1);
        Assert.True(Sky.MoonCycleDays >= 4);
    }

    [Fact]
    public void FullMoonIsTheDefaultNight()
    {
        var full = Sky.At(0, SkyDef.FullMoon);
        var plain = Sky.At(0);
        Assert.Equal(plain.Intensity, full.Intensity, 6);
        Assert.Equal(plain.AmbientSky.B, full.AmbientSky.B, 6);
        Assert.Equal(1f, full.Illumination, 5);
    }

    [Fact]
    public void NewMoonNightsAreNearlyBlack()
    {
        var full = Sky.At(0, SkyDef.FullMoon);
        var dark = Sky.At(0, 0);
        Assert.Equal(full.Intensity * Sky.NewMoonLight, dark.Intensity, 5);
        Assert.True(dark.Intensity < 0.01f);
        Assert.True(dark.AmbientSky.R + dark.AmbientSky.G + dark.AmbientSky.B < 0.03f);
        Assert.True(dark.Sky.R + dark.Sky.G + dark.Sky.B < 0.04f);
        Assert.True(dark.Sky.B > 0);
        Assert.True(full.Intensity > 5 * dark.Intensity);
        var noon = Sky.At(12, 0);
        Assert.Equal(Sky.At(12).Intensity, noon.Intensity, 6);
        Assert.Equal(Sky.At(12).Sky.R, noon.Sky.R, 6);
        for (float h = 0; h < 24; h += 0.05f)
            Assert.True(Sky.At(h, 0).Intensity <= Sky.At(h, SkyDef.FullMoon).Intensity + 1e-6f);
    }

    [Fact]
    public void NightSkyStaysDarkAndDuskGlowsTowardTheSun()
    {
        var full = Sky.At(0, SkyDef.FullMoon);
        var dark = Sky.At(0, 0);
        Assert.True(dark.Zenith.R + dark.Zenith.G + dark.Zenith.B < 0.02f);
        Assert.Equal(0f, dark.MoonGlow, 6);
        Assert.True(full.MoonGlow > 0 && full.MoonGlow <= Sky.MoonGlow);
        Assert.True(full.Stars < dark.Stars && dark.Stars <= Sky.StarBrightness);
        Assert.True(full.Zenith.B < full.Sky.B);
        Assert.Equal(0f, Sky.At(12).Stars, 6);
        Assert.Equal(0f, Sky.At(12).MoonGlow, 6);
        Assert.Equal(-full.MoonX, full.DirX, 5);
        Assert.Equal(-Sky.At(12).SunY, Sky.At(12).DirY, 5);
        var dusk = Sky.At(18.15f);
        Assert.True(dusk.Glow.R > dusk.Glow.B && dusk.Glow.R > Sky.At(12).Glow.R);
    }

    [Theory]
    [InlineData("\"stops\": []")]
    [InlineData("\"stops\": [{\"sunHeight\": 0.5, \"sky\": [1,1,1], \"ambientSky\": [1,1,1], \"ambientGround\": [1,1,1], \"sun\": [1,1,1], \"sunIntensity\": 1}, {\"sunHeight\": 0.6, \"sky\": [1,1,1], \"ambientSky\": [1,1,1], \"ambientGround\": [1,1,1], \"sun\": [1,1,1], \"sunIntensity\": 0}]")]
    [InlineData("\"stops\": [{\"sunHeight\": 0.5, \"sky\": [1,1], \"ambientSky\": [1,1,1], \"ambientGround\": [1,1,1], \"sun\": [1,1,1], \"sunIntensity\": 1}, {\"sunHeight\": -0.6, \"sky\": [1,1,1], \"ambientSky\": [1,1,1], \"ambientGround\": [1,1,1], \"sun\": [1,1,1], \"sunIntensity\": 0}]")]
    [InlineData("\"stops\": [{\"sunHeight\": 0.5, \"sky\": [1,1,1], \"ambientSky\": [1,1,1], \"ambientGround\": [1,1,1], \"sun\": [1,1,1], \"sunIntensity\": 1}, {\"sunHeight\": -0.6, \"sky\": [1,1,1], \"ambientSky\": [1,1,1], \"ambientGround\": [1,1,1], \"sun\": [1,1,1], \"sunIntensity\": 1}]")]
    [InlineData("\"clouds\": {\"coverage\": 1.5, \"density\": 1, \"heightM\": 1800, \"thicknessM\": 700, \"scaleM\": 3000, \"windDirDeg\": 0, \"windSpeedMs\": 5, \"churn\": 1}, " + GoodStops)]
    [InlineData("\"clouds\": {\"coverage\": 0.5, \"density\": 1, \"heightM\": 1800, \"thicknessM\": 700, \"scaleM\": 3000, \"windDirDeg\": 0, \"windSpeedMs\": 5}, " + GoodStops)]
    [InlineData("\"noClouds\": 0, " + GoodStops)]
    public void BadSkyIsRejected(string stops)
    {
        if (!stops.Contains("\"clouds\"") && !stops.Contains("\"noClouds\"")) stops = GoodClouds + stops;
        stops = stops.Replace("\"noClouds\": 0, ", "");
        var json = "{\"schemaVersion\": 1, \"dayLengthS\": 600, \"startHour\": 9, " +
            "\"sun\": {\"noonElevationDeg\": 38, \"noonAzimuthDeg\": 320, \"hourOffset\": 0}, " +
            "\"moon\": {\"noonElevationDeg\": 50, \"noonAzimuthDeg\": 20, \"hourOffset\": 12}, " +
            "\"moonColor\": [0.6, 0.7, 1], \"moonIntensity\": 0.2, \"moonSwitchHeight\": -0.06, \"moonFadeHeight\": 0.1, " +
            "\"moonCycleDays\": 8, \"moonStartPhase\": 0.5, \"newMoonLight\": 0.03, \"newMoonAmbient\": 0.2, \"moonGlow\": 0.02, \"starBrightness\": 0.01, " +
            stops.Replace("\"ambientSky\"", "\"zenith\": [0.3,0.4,0.6], \"glow\": [0,0,0], \"ambientSky\"") + "}";
        Assert.Throws<InvalidDataException>(() => ContentLoader.ParseSky(json, "sky.json"));
    }

    const string GoodClouds = "\"clouds\": {\"coverage\": 0.5, \"density\": 1, \"heightM\": 1800, \"thicknessM\": 700, \"scaleM\": 3000, \"windDirDeg\": 0, \"windSpeedMs\": 5, \"churn\": 1}, ";
    const string GoodStops = "\"stops\": [{\"sunHeight\": 0.5, \"sky\": [1,1,1], \"ambientSky\": [1,1,1], \"ambientGround\": [1,1,1], \"sun\": [1,1,1], \"sunIntensity\": 1}, {\"sunHeight\": -0.06, \"sky\": [1,1,1], \"ambientSky\": [1,1,1], \"ambientGround\": [1,1,1], \"sun\": [1,1,1], \"sunIntensity\": 0}]";

    [Fact]
    public void GoodSkyWithCloudsLoads()
    {
        var json = "{\"schemaVersion\": 1, \"dayLengthS\": 600, \"startHour\": 9, " +
            "\"sun\": {\"noonElevationDeg\": 38, \"noonAzimuthDeg\": 320, \"hourOffset\": 0}, " +
            "\"moon\": {\"noonElevationDeg\": 50, \"noonAzimuthDeg\": 20, \"hourOffset\": 12}, " +
            "\"moonColor\": [0.6, 0.7, 1], \"moonIntensity\": 0.2, \"moonSwitchHeight\": -0.06, \"moonFadeHeight\": 0.1, " +
            "\"moonCycleDays\": 8, \"moonStartPhase\": 0.5, \"newMoonLight\": 0.03, \"newMoonAmbient\": 0.2, \"moonGlow\": 0.02, \"starBrightness\": 0.01, " +
            GoodClouds + GoodStops.Replace("\"ambientSky\"", "\"zenith\": [0.3,0.4,0.6], \"glow\": [0,0,0], \"ambientSky\"") + "}";
        Assert.Equal(0.5f, ContentLoader.ParseSky(json, "sky.json").Clouds.Coverage);
    }

    [Fact]
    public void CloudsDriftDownwindAndWrapSmoothly()
    {
        var c = Sky.Clouds;
        c.Drift(0, out float x0, out float z0, out _, out _);
        Assert.Equal(0f, x0);
        Assert.Equal(0f, z0);
        float P = CloudDef.NoisePeriod;
        c.Drift(100, out float x, out float z, out float cx, out _);
        double a = c.WindDirDeg * Math.PI / 180, d = 100.0 * c.WindSpeedMs / c.ScaleM;
        Assert.Equal(Math.Sin(a) * d, x > P / 2 ? x - P : x, 4);
        Assert.Equal(Math.Cos(a) * d, z > P / 2 ? z - P : z, 4);
        Assert.Equal(Math.Sin(a) * d * c.Churn, cx > P / 2 ? cx - P : cx, 3);
        c.Drift(1e7, out float px, out float pz, out _, out _);
        for (double t = 1e7 + 0.5; t < 1e7 + 1000; t += 0.5)
        {
            c.Drift(t, out float nx, out float nz, out _, out _);
            Assert.InRange(nx, 0, P);
            Assert.InRange(nz, 0, P);
            float sx = Math.Abs(nx - px), sz = Math.Abs(nz - pz);
            Assert.True(Math.Min(sx, P - sx) < 0.01f && Math.Min(sz, P - sz) < 0.01f, $"jump at {t}");
            (px, pz) = (nx, nz);
        }
    }
}
