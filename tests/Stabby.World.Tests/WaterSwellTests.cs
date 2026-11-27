using Stabby.World;

namespace Stabby.World.Tests;

public class WaterSwellTests
{
    static readonly SwellParams[] Both = { WaterSwell.Sea, WaterSwell.Lake };
    static readonly SwellParams[] ShoreOnly = { Calm(WaterSwell.Sea), Calm(WaterSwell.Lake) };

    static SwellParams Calm(SwellParams p) => new SwellParams(0, p.Length, p.ShorePeriod, p.ShoreHeight);

    [Fact]
    public void DeterministicAndBounded()
    {
        foreach (var p in Both)
            for (int k = 0; k < 4000; k++)
            {
                float x = k * 7.31f - 9000, z = k * 3.17f + 120, t = k * 0.173f, d = (k % 97) * 0.4f, s = d / 0.05f;
                float a = WaterSwell.Height(p, x, z, t, d, s), b = WaterSwell.Height(p, x, z, t, d, s);
                Assert.Equal(a, b);
                Assert.False(float.IsNaN(a));
                float open = p.Height * WaterSwell.OpenPeak * WaterSwell.OpenScale(p, d);
                Assert.True(Math.Abs(a) <= open + WaterSwell.ShoreAmplitude(p, d) + 1e-4f, $"|{a}| too high at depth {d}");
                Assert.True(WaterSwell.ShoreAmplitude(p, d) <= WaterSwell.Breaking * d + 1e-6f);
            }
    }

    [Fact]
    public void TrainsRunLongestFirstWithUnitHeadings()
    {
        int n = WaterSwell.TrainLength.Length;
        Assert.Equal(n, WaterSwell.TrainX.Length);
        Assert.Equal(n, WaterSwell.TrainZ.Length);
        Assert.Equal(n, WaterSwell.TrainPart.Length);
        Assert.Equal(n, WaterSwell.TrainSharp.Length);
        Assert.Equal(n, WaterSwell.TrainPhase.Length);
        for (int i = 0; i < n; i++)
        {
            float x = WaterSwell.TrainX[i], z = WaterSwell.TrainZ[i];
            Assert.InRange(x * x + z * z, 0.99f, 1.01f);
            Assert.InRange(WaterSwell.TrainSharp[i], 0f, 1f);
            if (i > 0)
            {
                Assert.True(WaterSwell.TrainLength[i] < WaterSwell.TrainLength[i - 1]);
                Assert.True(WaterSwell.TrainPart[i] < WaterSwell.TrainPart[i - 1]);
            }
        }
        Assert.InRange(WaterSwell.Sea.Length * WaterSwell.TrainLength[n - 1], 2f, 6f);
    }

    [Fact]
    public void OpenSwellHasNarrowCrestsAndBroadTroughs()
    {
        int n = 1000;
        foreach (float sharp in WaterSwell.TrainSharp)
        {
            int above = 0;
            for (int k = 0; k < n; k++)
                if (WaterSwell.Wave((k + 0.5f) / n, sharp, out _) > 0) above++;
            Assert.InRange(above, n / 4, n * 21 / 50);
        }
    }

    [Fact]
    public void ZeroOnDryGround()
    {
        foreach (var p in Both)
            for (int k = 0; k < 200; k++)
            {
                Assert.Equal(0f, WaterSwell.Height(p, k * 13.1f, k * 5.7f, k * 0.31f, 0, 0));
                Assert.Equal(0f, WaterSwell.Height(p, k * 13.1f, k * 5.7f, k * 0.31f, -1, 0));
                Assert.True(Math.Abs(WaterSwell.Height(p, k * 13.1f, k * 5.7f, k * 0.31f, 1e-4f, 2e-3f)) < 1e-4f);
            }
    }

    [Fact]
    public void ContinuousInSpaceAndTime()
    {
        foreach (var p in Both)
        {
            float step = 0.002f, worst = 0;
            for (int k = 0; k < 20000; k++)
            {
                float x = k * 0.05f, d = 0.3f + k * 0.0009f, s = d / 0.04f;
                float a = WaterSwell.Height(p, x, 17, 3.3f, d, s);
                float b = WaterSwell.Height(p, x + step, 17, 3.3f, d + step * 0.04f, s + step);
                float c = WaterSwell.Height(p, x, 17, 3.3f + step, d, s);
                worst = Math.Max(worst, Math.Max(Math.Abs(a - b), Math.Abs(a - c)));
            }
            Assert.True(worst < 0.02f, $"jump {worst}");
        }
    }

    [Fact]
    public void SlopeMatchesFiniteDifferenceInOpenWater()
    {
        foreach (var p in Both)
            for (int k = 0; k < 500; k++)
            {
                float x = k * 3.7f, z = k * 1.9f - 300, t = k * 0.11f, e = 1e-2f;
                WaterSwell.Sample(p, x, z, t, 40, 800, 0, 0, out float gx, out float gz);
                float fx = (WaterSwell.Height(p, x + e, z, t, 40, 800) - WaterSwell.Height(p, x - e, z, t, 40, 800)) / (2 * e);
                float fz = (WaterSwell.Height(p, x, z + e, t, 40, 800) - WaterSwell.Height(p, x, z - e, t, 40, 800)) / (2 * e);
                Assert.True(Math.Abs(gx - fx) < 0.01f && Math.Abs(gz - fz) < 0.01f, $"({gx},{gz}) vs ({fx},{fz}) at {x},{z}");
            }
    }

    [Fact]
    public void CrestSpeedStaysNearlyConstantToTheShore()
    {
        const float beach = 0.04f, dt = 0.05f;
        foreach (var p in ShoreOnly)
        {
            float v0 = WaterSwell.ShoreWavelength(p) / (2 * p.ShorePeriod);
            int near = 0;
            for (int k = 0; k < 60; k++)
            {
                float t = k * p.ShorePeriod / 60;
                var after = UpCrossings(p, t + dt, beach);
                foreach (float s in UpCrossings(p, t, beach))
                {
                    if (s < 0.2f || s > 55) continue;
                    float want = s - v0 * dt, best = float.MaxValue;
                    foreach (float a in after)
                        if (Math.Abs(a - want) < Math.Abs(best - want)) best = a;
                    float r = (s - best) / (dt * v0);
                    if (s < 1) near++;
                    Assert.True(s < 1 ? r > 0.75f && r < 1.02f : Math.Abs(r - 1) < 0.02f, $"crest at {s} m moves at {r:F3} of the band speed");
                }
            }
            Assert.True(near > 0);
        }
    }

    static List<float> UpCrossings(in SwellParams p, float t, float beach)
    {
        const float step = 0.002f;
        var list = new List<float>();
        float prev = WaterSwell.Height(p, 0, 0, t, 0.1f * beach, 0.1f);
        for (int i = 1; i < 30000; i++)
        {
            float s = 0.1f + i * step, h = WaterSwell.Height(p, 0, 0, t, s * beach, s);
            if (prev < 0 && h >= 0) list.Add(s - step * h / (h - prev));
            prev = h;
        }
        return list;
    }

    [Fact]
    public void ShoreCrestsRunParallelToTheShore()
    {
        const float beach = 0.04f;
        foreach (var p in ShoreOnly)
            for (int k = 0; k < 300; k++)
            {
                float off = 5 + k * 0.25f, d = off * beach, t = k * 0.07f;
                float a = WaterSwell.Sample(p, off, -400, t, d, off, -1, 0, out _, out float gz);
                float b = WaterSwell.Height(p, off, 250, t, d, off);
                Assert.Equal(a, b);
                Assert.Equal(0f, gz);
            }
    }

    [Fact]
    public void ShoreWavesMoveTowardTheShore()
    {
        var p = ShoreOnly[0];
        float d = 2, dt = 0.2f, shift = WaterSwell.ShoreWavelength(p) * dt / (2 * p.ShorePeriod);
        for (int k = 0; k < 200; k++)
        {
            float s = 20 + k * 0.37f, t = k * 0.13f;
            float now = WaterSwell.Height(p, 0, 0, t, d, s);
            float later = WaterSwell.Height(p, 0, 0, t + dt, d, s - shift);
            Assert.True(Math.Abs(now - later) < 1e-3f, $"{now} vs {later} at {s}");
        }
    }

    [Fact]
    public void ShoreSlopeMatchesFiniteDifferenceAcrossTheBand()
    {
        foreach (var p in ShoreOnly)
            for (int k = 0; k < 300; k++)
            {
                float d = 1 + k * 0.01f, s = k < 100 ? 0.01f + k * 0.0098f : 30 + k * 0.29f, t = k * 0.21f, e = 1e-3f;
                WaterSwell.Sample(p, 0, 0, t, d, s, 1, 0, out float gx, out _);
                float fx = (WaterSwell.Height(p, 0, 0, t, d, s - e) - WaterSwell.Height(p, 0, 0, t, d, s + e)) / (2 * e);
                Assert.True(Math.Abs(gx - fx) < 0.02f * Math.Max(1, Math.Abs(fx)), $"{gx} vs {fx} at {s}");
            }
    }
}
