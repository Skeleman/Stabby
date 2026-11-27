using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Stabby.World
{
    public struct VentSite
    {
        public float X, Z;
        public int Mask;
    }

    public sealed class Geothermal
    {
        public const float HotspotsPerKm2 = 0.012f, MinRadiusM = 450f, MaxRadiusM = 800f, ApartRadii = 3f, ReachM = 2500f;
        public const int TriesPerHotspot = 32, CalmBatch = 16;
        public readonly List<float> X = new List<float>(), Z = new List<float>(), R = new List<float>();

        public int Count => X.Count;

        public static Geothermal Build(GeneratedWorld w, WorldSketch sk, Func<float, float, float, bool> calm = null)
        {
            var g = new Geothermal();
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, worldM = n * cm;
            if (sk.Hotspots.Count == 0) return g;
            var rng = Rng.Stream(w.Config.Seed, "geothermal");
            int target = Math.Max(1, (int)Math.Round(HotspotsPerKm2 * worldM * worldM * 1e-6f));
            var cand = new List<float>();
            var ok = new bool[CalmBatch];
            for (int t = 0, limit = target * TriesPerHotspot; t < limit && g.Count < target;)
            {
                cand.Clear();
                for (; t < limit && cand.Count < 3 * CalmBatch; t++)
                {
                    var spot = sk.Hotspots[rng.Range(0, sk.Hotspots.Count)];
                    float x = spot.X * cm + rng.Range(-1f, 1f) * ReachM, z = spot.Y * cm + rng.Range(-1f, 1f) * ReachM, radius = rng.Range(MinRadiusM, MaxRadiusM);
                    if (x < radius || z < radius || x > worldM - radius || z > worldM - radius) continue;
                    if (w.Sea[Math.Min(n - 1, (int)(z / cm + 0.5f)) * n + Math.Min(n - 1, (int)(x / cm + 0.5f))]) continue;
                    if (!g.Apart(x, z, radius)) continue;
                    cand.Add(x); cand.Add(z); cand.Add(radius);
                }
                int m = cand.Count / 3;
                Parallel.For(0, m, q => ok[q] = calm == null || calm(cand[3 * q], cand[3 * q + 1], cand[3 * q + 2]));
                for (int q = 0; q < m && g.Count < target; q++)
                {
                    float x = cand[3 * q], z = cand[3 * q + 1], radius = cand[3 * q + 2];
                    if (!g.Apart(x, z, radius) || !ok[q]) continue;
                    g.X.Add(x); g.Z.Add(z); g.R.Add(radius);
                }
            }
            return g;
        }

        bool Apart(float x, float z, float radius)
        {
            for (int k = 0; k < Count; k++)
            {
                float dx = X[k] - x, dz = Z[k] - z, gap = ApartRadii * Math.Max(radius, R[k]);
                if (dx * dx + dz * dz < gap * gap) return false;
            }
            return true;
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
    }
}
