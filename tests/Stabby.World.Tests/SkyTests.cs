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

    [Theory]
    [InlineData("\"stops\": []")]
    [InlineData("\"stops\": [{\"sunHeight\": 0.5, \"sky\": [1,1,1], \"ambientSky\": [1,1,1], \"ambientGround\": [1,1,1], \"sun\": [1,1,1], \"sunIntensity\": 1}, {\"sunHeight\": 0.6, \"sky\": [1,1,1], \"ambientSky\": [1,1,1], \"ambientGround\": [1,1,1], \"sun\": [1,1,1], \"sunIntensity\": 0}]")]
    [InlineData("\"stops\": [{\"sunHeight\": 0.5, \"sky\": [1,1], \"ambientSky\": [1,1,1], \"ambientGround\": [1,1,1], \"sun\": [1,1,1], \"sunIntensity\": 1}, {\"sunHeight\": -0.6, \"sky\": [1,1,1], \"ambientSky\": [1,1,1], \"ambientGround\": [1,1,1], \"sun\": [1,1,1], \"sunIntensity\": 0}]")]
    [InlineData("\"stops\": [{\"sunHeight\": 0.5, \"sky\": [1,1,1], \"ambientSky\": [1,1,1], \"ambientGround\": [1,1,1], \"sun\": [1,1,1], \"sunIntensity\": 1}, {\"sunHeight\": -0.6, \"sky\": [1,1,1], \"ambientSky\": [1,1,1], \"ambientGround\": [1,1,1], \"sun\": [1,1,1], \"sunIntensity\": 1}]")]
    public void BadSkyIsRejected(string stops)
    {
        var json = "{\"schemaVersion\": 1, \"dayLengthS\": 600, \"startHour\": 9, " +
            "\"sun\": {\"noonElevationDeg\": 38, \"noonAzimuthDeg\": 320, \"hourOffset\": 0}, " +
            "\"moon\": {\"noonElevationDeg\": 50, \"noonAzimuthDeg\": 20, \"hourOffset\": 12}, " +
            "\"moonColor\": [0.6, 0.7, 1], \"moonIntensity\": 0.2, \"moonSwitchHeight\": -0.06, \"moonFadeHeight\": 0.1, " + stops + "}";
        Assert.Throws<InvalidDataException>(() => ContentLoader.ParseSky(json, "sky.json"));
    }
}
