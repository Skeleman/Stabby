using System;

namespace Stabby.World
{
    public sealed class ClimateField
    {
        public float[] TemperatureC;
        public float[] PrecipitationMm;

        public static ClimateField Compute(WorldConfig c, float[] height, bool[] sea)
        {
            int n = c.Size;
            var f = new ClimateField { TemperatureC = new float[n * n], PrecipitationMm = new float[n * n] };
            for (int i = 0; i < n * n; i++)
                f.TemperatureC[i] = c.SeaLevelTempC - c.LapseRatePerMeter * Math.Max(0, height[i]);
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
            float km = c.CellMeters / 1000f;
            int back = Math.Max(1, (int)Math.Round(c.UpwindMeters / c.CellMeters));
            var line_h = new float[n];
            for (int line = 0; line < n; line++)
            {
                float moisture = 1f;
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
                    line_h[t] = hi;
                    if (sea[i])
                    {
                        moisture = Math.Min(1f, moisture + c.SeaRecharge * km);
                        p[i] = c.BaseRainMm * moisture;
                    }
                    else
                    {
                        float rise = Math.Max(0, hi - line_h[Math.Max(0, t - back)]) / (back * c.CellMeters);
                        float rain = moisture * Math.Min(c.MaxRainMm, c.BaseRainMm + c.OrographicRainMm * rise);
                        p[i] = rain;
                        moisture = Math.Max(0.05f, Math.Min(1f, moisture - rain * c.CellMeters / c.RainoutMm + c.LandRecharge * km));
                    }
                }
            }
            int r = Math.Max(1, (int)Math.Round(240f / c.CellMeters));
            return Blur(Blur(p, n, r), n, r);
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
