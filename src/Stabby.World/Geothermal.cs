using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class Geothermal
    {
        public const float HotspotsPerKm2 = 0.012f, MinRadiusM = 450f, MaxRadiusM = 800f, ApartRadii = 3f, RangeReach = 2f, HighlandReach = 0.8f, VolcanicWeight = 2f;
        public const int TriesPerHotspot = 32;
        public readonly List<float> X = new List<float>(), Z = new List<float>(), R = new List<float>();

        public int Count => X.Count;

        public static Geothermal Build(GeneratedWorld w, WorldSketch sk)
        {
            var g = new Geothermal();
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, worldM = n * cm, total = 0;
            var weight = new float[sk.Ranges.Count + sk.Highlands.Count];
            for (int k = 0; k < sk.Ranges.Count; k++)
            {
                var r = sk.Ranges[k];
                weight[k] = r.Width > 0 && r.X.Length > 1 ? Length(r.X, r.Y) * Math.Max(0f, r.Strength) : 0;
                total += weight[k];
            }
            for (int k = 0; k < sk.Highlands.Count; k++)
            {
                var h = sk.Highlands[k];
                weight[sk.Ranges.Count + k] = h.Plateau || h.Radius <= 0 ? 0 : VolcanicWeight * 2 * h.Radius;
                total += weight[sk.Ranges.Count + k];
            }
            if (!(total > 0)) return g;
            var rng = Rng.Stream(w.Config.Seed, "geothermal");
            int target = Math.Max(1, (int)Math.Round(HotspotsPerKm2 * worldM * worldM * 1e-6f));
            for (int t = 0; t < target * TriesPerHotspot && g.Count < target; t++)
            {
                float pick = rng.NextFloat() * total, along = rng.NextFloat(), ja = rng.Range(-1f, 1f), jb = rng.Range(-1f, 1f), radius = rng.Range(MinRadiusM, MaxRadiusM);
                int s = 0;
                while (s < weight.Length - 1 && (weight[s] <= 0 || pick >= weight[s])) { pick -= weight[s]; s++; }
                float px, pz;
                if (s < sk.Ranges.Count)
                {
                    var r = sk.Ranges[s];
                    PointAlong(r.X, r.Y, along, out px, out pz);
                    px += ja * RangeReach * r.Width; pz += jb * RangeReach * r.Width;
                }
                else
                {
                    var h = sk.Highlands[s - sk.Ranges.Count];
                    px = h.X + ja * HighlandReach * h.Radius; pz = h.Y + jb * HighlandReach * h.Radius;
                }
                float x = px * cm, z = pz * cm;
                if (x < radius || z < radius || x > worldM - radius || z > worldM - radius) continue;
                if (w.Sea[Math.Min(n - 1, (int)(pz + 0.5f)) * n + Math.Min(n - 1, (int)(px + 0.5f))]) continue;
                bool apart = true;
                for (int k = 0; k < g.Count && apart; k++)
                {
                    float dx = g.X[k] - x, dz = g.Z[k] - z, gap = ApartRadii * Math.Max(radius, g.R[k]);
                    apart = dx * dx + dz * dz >= gap * gap;
                }
                if (!apart) continue;
                g.X.Add(x); g.Z.Add(z); g.R.Add(radius);
            }
            return g;
        }

        public float Heat(float x, float z)
        {
            float best = 0;
            for (int k = 0; k < X.Count; k++)
            {
                float dx = X[k] - x, dz = Z[k] - z;
                best = Math.Max(best, 1 - (float)Math.Sqrt(dx * dx + dz * dz) / R[k]);
            }
            return best;
        }

        public int Index(float x, float z)
        {
            int best = -1;
            float heat = 0;
            for (int k = 0; k < X.Count; k++)
            {
                float dx = X[k] - x, dz = Z[k] - z, h = 1 - (float)Math.Sqrt(dx * dx + dz * dz) / R[k];
                if (h > heat) { heat = h; best = k; }
            }
            return best;
        }

        static float Length(float[] xs, float[] ys)
        {
            float total = 0;
            for (int k = 1; k < xs.Length; k++)
            {
                float dx = xs[k] - xs[k - 1], dy = ys[k] - ys[k - 1];
                total += (float)Math.Sqrt(dx * dx + dy * dy);
            }
            return total;
        }

        static void PointAlong(float[] xs, float[] ys, float u, out float x, out float y)
        {
            float left = u * Length(xs, ys);
            for (int k = 1; k < xs.Length; k++)
            {
                float dx = xs[k] - xs[k - 1], dy = ys[k] - ys[k - 1], len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (left <= len && len > 0)
                {
                    x = xs[k - 1] + dx * left / len; y = ys[k - 1] + dy * left / len;
                    return;
                }
                left -= len;
            }
            x = xs[xs.Length - 1]; y = ys[ys.Length - 1];
        }
    }
}
