using System;

namespace Stabby.World
{
    public static class BiomeBorder
    {
        public const float WarpCells = 0.3f, WarpWavelengthCells = 2.5f;

        public static int At(GeneratedWorld w, float x, float z) => At(w.Biome, w.Config.Size, w.Config.CellMeters, x, z);

        public static int At(int[] biome, int n, float cm, float x, float z)
        {
            float gx = x / cm, gz = z / cm;
            Warp(gx, gz, out float wx, out float wz);
            float top = n - 1;
            gx = Math.Max(0f, Math.Min(top, gx + wx));
            gz = Math.Max(0f, Math.Min(top, gz + wz));
            int x0 = Math.Min((int)gx, n - 2), z0 = Math.Min((int)gz, n - 2), i = z0 * n + x0;
            int a = biome[i], b = biome[i + 1], c = biome[i + n], d = biome[i + n + 1];
            if (a == b && a == c && a == d) return a;
            float tx = gx - x0, tz = gz - z0;
            float wa = (1f - tx) * (1f - tz), wb = tx * (1f - tz), wc = (1f - tx) * tz, wd = tx * tz;
            float sa = wa + (b == a ? wb : 0f) + (c == a ? wc : 0f) + (d == a ? wd : 0f);
            float sb = b == a ? -1f : wb + (c == b ? wc : 0f) + (d == b ? wd : 0f);
            float sc = c == a || c == b ? -1f : wc + (d == c ? wd : 0f);
            float sd = d == a || d == b || d == c ? -1f : wd;
            int best = a;
            float top2 = sa;
            if (sb > top2) { best = b; top2 = sb; }
            if (sc > top2) { best = c; top2 = sc; }
            if (sd > top2) best = d;
            return best;
        }

        public static void Warp(float gx, float gz, out float wx, out float wz)
        {
            float px = gx * (1f / WarpWavelengthCells), pz = gz * (1f / WarpWavelengthCells);
            wx = WarpCells * Value(px, pz, 0x2f6bu);
            wz = WarpCells * Value(px, pz, 0x9c41u);
        }

        static float Value(float px, float pz, uint salt)
        {
            float fx = (float)Math.Floor(px), fz = (float)Math.Floor(pz);
            int ix = (int)fx, iz = (int)fz;
            float tx = px - fx, tz = pz - fz;
            tx = tx * tx * (3f - 2f * tx);
            tz = tz * tz * (3f - 2f * tz);
            float v00 = Lattice(ix, iz, salt), v10 = Lattice(ix + 1, iz, salt), v01 = Lattice(ix, iz + 1, salt), v11 = Lattice(ix + 1, iz + 1, salt);
            float lo = v00 + (v10 - v00) * tx, hi = v01 + (v11 - v01) * tx;
            return lo + (hi - lo) * tz;
        }

        static float Lattice(int x, int z, uint salt)
        {
            uint h = unchecked((uint)x * 0x8da6b343u ^ (uint)z * 0xd8163841u ^ salt);
            h ^= h >> 16; h = unchecked(h * 0x7feb352du);
            h ^= h >> 15; h = unchecked(h * 0x846ca68bu);
            h ^= h >> 16;
            return (h >> 8) * (2f / 16777216f) - 1f;
        }
    }
}
