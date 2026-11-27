using System;

namespace Stabby.World
{
    public sealed class ClimateField
    {
        public float[] TemperatureC;
        public float[] PrecipitationMm;

        public static ClimateField Compute(WorldConfig c, float[] height, bool[] sea, WorldSketch s = null)
        {
            int n = c.Size;
            var f = new ClimateField { TemperatureC = new float[n * n], PrecipitationMm = new float[n * n] };
            for (int i = 0; i < n * n; i++)
                f.TemperatureC[i] = c.SeaLevelTempC - c.LapseRatePerMeter * Math.Max(0, height[i]);
            f.PrecipitationMm = Sweep(c, height, sea);
            if (s != null) Drylands(c, s, f);
            return f;
        }

        public static float DryWeight(WorldConfig c, HighlandSketch hl, float sx, float sy)
        {
            float dx = sx - hl.X, dy = sy - hl.Y;
            float d = (float)Math.Sqrt(dx * dx + dy * dy) / Math.Max(1e-3f, hl.Radius);
            float t = Math.Max(0f, Math.Min(1f, (d - c.DryInner) / (c.DryOuter - c.DryInner)));
            return 1f - t * t * (3f - 2f * t);
        }

        static void Drylands(WorldConfig c, WorldSketch s, ClimateField f)
        {
            int n = c.Size;
            float scale = s.Size / (float)n, off = 0.5f * scale - 0.5f;
            foreach (var hl in s.Highlands)
            {
                if (!hl.Dry) continue;
                float reach = hl.Radius * c.DryOuter / scale + 1f;
                int x0 = Math.Max(0, (int)((hl.X - off) / scale - reach)), x1 = Math.Min(n - 1, (int)((hl.X - off) / scale + reach) + 1);
                int y0 = Math.Max(0, (int)((hl.Y - off) / scale - reach)), y1 = Math.Min(n - 1, (int)((hl.Y - off) / scale + reach) + 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float w = DryWeight(c, hl, x * scale + off, y * scale + off);
                        if (w <= 0f) continue;
                        int i = y * n + x;
                        f.TemperatureC[i] += c.DryWarmC * w;
                        f.PrecipitationMm[i] -= Math.Max(0f, f.PrecipitationMm[i] - c.DryRainMm) * w;
                    }
            }
        }

        public static ClimateField Uniform(WorldConfig c, float[] height, float rainMm)
        {
            var f = new ClimateField { TemperatureC = new float[height.Length], PrecipitationMm = new float[height.Length] };
            for (int i = 0; i < height.Length; i++)
            {
                f.TemperatureC[i] = c.SeaLevelTempC - c.LapseRatePerMeter * Math.Max(0, height[i]);
                f.PrecipitationMm[i] = rainMm;
            }
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
