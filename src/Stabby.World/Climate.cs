using System;

namespace Stabby.World
{
    // Long-term climate only: mean temperature and annual precipitation.
    // Temperature follows latitude and a lapse rate; precipitation comes from a
    // moisture sweep along the prevailing wind that recharges over the sea and
    // rains out on upslopes, which produces rain shadows behind ranges.
    public sealed class ClimateField
    {
        public float[] TemperatureC;
        public float[] PrecipitationMm;

        public const float LapseRatePerMeter = 0.0065f;

        public static ClimateField Compute(WorldConfig c, float[] height, bool[] sea)
        {
            int n = c.Size;
            var f = new ClimateField { TemperatureC = new float[n * n], PrecipitationMm = new float[n * n] };
            for (int y = 0; y < n; y++)
            {
                float lat = c.LatitudeSouth + (c.LatitudeNorth - c.LatitudeSouth) * y / (n - 1f);
                float seaLevelT = 28f - 0.6f * (lat - 20f);
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    f.TemperatureC[i] = seaLevelT - LapseRatePerMeter * Math.Max(0, height[i]);
                }
            }
            f.PrecipitationMm = Sweep(c, height, sea);
            return f;
        }

        public static ClimateField Uniform(WorldConfig c, float[] height, float rainMm)
        {
            var f = Compute(c, height, new bool[height.Length]);
            for (int i = 0; i < height.Length; i++) f.PrecipitationMm[i] = rainMm;
            return f;
        }

        static float[] Sweep(WorldConfig c, float[] h, bool[] sea)
        {
            int n = c.Size;
            var p = new float[n * n];
            for (int line = 0; line < n; line++)
            {
                float moisture = 1f;
                float prevH = 0;
                for (int t = 0; t < n; t++)
                {
                    int x, y;
                    switch (c.PrevailingWind)
                    {
                        case Wind.FromWest: x = t; y = line; break;
                        case Wind.FromEast: x = n - 1 - t; y = line; break;
                        case Wind.FromSouth: x = line; y = t; break;
                        default: x = line; y = n - 1 - t; break;
                    }
                    int i = y * n + x;
                    float hi = Math.Max(0, h[i]);
                    if (sea[i])
                    {
                        moisture = Math.Min(1f, moisture + 0.2f);
                        p[i] = 900f * moisture;
                    }
                    else
                    {
                        float rise = Math.Max(0, hi - prevH) / c.CellMeters;
                        float rain = moisture * Math.Min(4500f, 700f + 30000f * rise);
                        p[i] = rain;
                        moisture = Math.Max(0.02f, moisture - rain / 90000f + 0.002f);
                    }
                    prevH = hi;
                }
            }
            return Blur(Blur(p, n, 4), n, 4);
        }

        static float[] Blur(float[] src, int n, int r)
        {
            var tmp = new float[n * n];
            var dst = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float s = 0; int k = 0;
                    for (int d = -r; d <= r; d++) { int xx = x + d; if (xx < 0 || xx >= n) continue; s += src[y * n + xx]; k++; }
                    tmp[y * n + x] = s / k;
                }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float s = 0; int k = 0;
                    for (int d = -r; d <= r; d++) { int yy = y + d; if (yy < 0 || yy >= n) continue; s += tmp[yy * n + x]; k++; }
                    dst[y * n + x] = s / k;
                }
            return dst;
        }
    }
}
