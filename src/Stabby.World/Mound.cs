using System;

namespace Stabby.World
{
    public static class Mound
    {
        public const float Rise = 5f;
        public const int Radius = 10;

        public static int Raise(GeneratedWorld w, float rise = Rise, int radius = Radius)
        {
            int n = w.Config.Size, s = WorldChunks.Size, best = -1;
            long bestD = long.MaxValue;
            for (int y = s; y < n; y += s)
                for (int x = s; x < n; x += s)
                {
                    long d = (long)(x - n / 2) * (x - n / 2) + (long)(y - n / 2) * (y - n / 2);
                    if (d < bestD && Dry(w, x, y, radius)) { bestD = d; best = y * n + x; }
                }
            if (best < 0) return -1;
            int cx = best % n, cy = best / n;
            for (int y = cy - radius; y <= cy + radius; y++)
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    float f = 1f - (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                    if (f <= 0) continue;
                    w.Height[y * n + x] += rise * f;
                    w.Water.WaterSurface[y * n + x] += rise * f;
                }
            return best;
        }

        static bool Dry(GeneratedWorld w, int cx, int cy, int r)
        {
            int n = w.Config.Size;
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || y < 0 || x >= n || y >= n) return false;
                    int i = y * n + x;
                    if (w.Sea[i] || w.Water.LakeId[i] >= 0 || w.Water.IsRiver[i] || w.Height[i] < 2f) return false;
                }
            return true;
        }
    }
}
