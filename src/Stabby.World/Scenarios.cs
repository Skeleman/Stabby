using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class Scenario
    {
        public string Name;
        public ulong Seed;
        public string Sketch;
        public Func<GeneratedWorld, int> Find;
    }

    public static class Scenarios
    {
        public const int Size = 256;

        public static readonly Scenario[] All =
        {
            new Scenario { Name = "river-through-range", Seed = 49, Find = RiverThroughRange },
            new Scenario { Name = "river-by-highland", Seed = 8, Find = RiverByHighland },
            new Scenario { Name = "lake-outlet-river", Seed = 3, Find = LakeOutletRiver },
            new Scenario { Name = "cliff-meets-beach-or-valley", Seed = 5, Find = CliffMeetsBeachOrValley },
            new Scenario { Name = "narrow-channel", Seed = 4, Find = NarrowChannel },
            new Scenario { Name = "authored-dry-region", Seed = 11, Sketch = "scenarios/authored-dry-region.json", Find = AuthoredRegionHolds },
        };

        public static int RiverThroughRange(GeneratedWorld w)
        {
            int n = w.Config.Size;
            var hy = w.Water;
            float q = 4 * w.Config.RiverMinDischarge;
            foreach (var range in w.Sketch.Ranges)
                for (int i = 0; i < n * n; i++)
                {
                    int r = hy.Receiver[i];
                    if (!hy.IsRiver[i] || !hy.IsRiver[r] || hy.DischargeM3s[i] < q) continue;
                    if (Crosses(range, i % n, i / n, r % n, r / n) && ReachesSea(w, r)) return i;
                }
            return -1;
        }

        public static int RiverByHighland(GeneratedWorld w)
        {
            int n = w.Config.Size;
            var hy = w.Water;
            foreach (var h in w.Sketch.Highlands)
                for (int i = 0; i < n * n; i++)
                {
                    int r = hy.Receiver[i];
                    if (!hy.IsRiver[i] || Inside(h, n, i) || !Inside(h, n, r)) continue;
                    int c = r, steps = 0;
                    while (Inside(h, n, c) && hy.Receiver[c] != c && steps++ < n * 4) c = hy.Receiver[c];
                    if (!Inside(h, n, c) && ReachesSea(w, c)) return r;
                }
            return -1;
        }

        public static int LakeOutletRiver(GeneratedWorld w)
        {
            var hy = w.Water;
            foreach (var lake in hy.Lakes)
            {
                if (lake.Outlet < 0) continue;
                int c = lake.Outlet;
                for (int k = 0; k < 4 && !w.Sea[c]; k++)
                {
                    if (hy.IsRiver[c] && hy.LakeId[c] < 0) return ReachesSea(w, c) ? lake.Outlet : -1;
                    c = hy.Receiver[c];
                }
            }
            return -1;
        }

        public static int CliffMeetsBeachOrValley(GeneratedWorld w)
        {
            int n = w.Config.Size, r = 6;
            var s = w.Sketch;
            for (int y = r; y < n - r; y++)
                for (int x = r; x < n - r; x++)
                {
                    int i = y * n + x;
                    if (w.Sea[i] || s.Cliff[i] < 0.6f || !Coastal(w, n, x, y)) continue;
                    for (int v = y - r; v <= y + r; v++)
                        for (int u = x - r; u <= x + r; u++)
                        {
                            int j = v * n + u;
                            if (w.Sea[j] || !Coastal(w, n, u, v)) continue;
                            if (s.Beach[j] >= 0.5f && s.Cliff[j] < 0.2f || w.Water.IsRiver[j]) return i;
                        }
                }
            return -1;
        }

        public static int NarrowChannel(GeneratedWorld w)
        {
            int n = w.Config.Size, max = 6;
            var part = Components(w);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    if (!w.Sea[y * n + x]) continue;
                    int a = Reach(w, part, n, x, y, -1, 0, max), b = Reach(w, part, n, x, y, 1, 0, max);
                    if (a >= 0 && b >= 0 && a != b) return y * n + x;
                    a = Reach(w, part, n, x, y, 0, -1, max); b = Reach(w, part, n, x, y, 0, 1, max);
                    if (a >= 0 && b >= 0 && a != b) return y * n + x;
                }
            return -1;
        }

        public static int AuthoredRegionHolds(GeneratedWorld w)
        {
            int n = w.Config.Size;
            var s = w.Sketch;
            if (w.Biome == null || s.Regions.Count == 0 || s.Landmarks.Count == 0) return -1;
            var r = s.Regions[0];
            int centre = (int)r.Y * n + (int)r.X;
            if (w.Sea[centre] || w.BiomeIds[w.Biome[centre]] != r.Biome) return -1;
            foreach (var l in s.Landmarks)
            {
                int i = (int)l.Y * n + (int)l.X;
                if (w.Sea[i] || w.Water.LakeId[i] >= 0 || w.Water.IsRiver[i]) return -1;
            }
            return centre;
        }

        static bool ReachesSea(GeneratedWorld w, int c)
        {
            int n = w.Config.Size, steps = 0;
            while (!w.Sea[c] && w.Water.Receiver[c] != c && steps++ < n * 4) c = w.Water.Receiver[c];
            return w.Sea[c];
        }

        static bool Inside(HighlandSketch h, int n, int i)
        {
            float dx = i % n - h.X, dy = i / n - h.Y;
            return dx * dx + dy * dy <= h.Radius * h.Radius;
        }

        static bool Crosses(RangeSketch r, float ax, float ay, float bx, float by)
        {
            for (int k = 0; k + 1 < r.X.Length; k++)
            {
                float cx = r.X[k], cy = r.Y[k], dx = r.X[k + 1], dy = r.Y[k + 1];
                if (Orient(ax, ay, bx, by, cx, cy) * Orient(ax, ay, bx, by, dx, dy) < 0 &&
                    Orient(cx, cy, dx, dy, ax, ay) * Orient(cx, cy, dx, dy, bx, by) < 0) return true;
            }
            return false;
        }

        static float Orient(float ax, float ay, float bx, float by, float px, float py) =>
            (bx - ax) * (py - ay) - (by - ay) * (px - ax);

        static bool Coastal(GeneratedWorld w, int n, int x, int y)
        {
            for (int k = 0; k < 8; k++)
            {
                int u = x + Grid.DX[k], v = y + Grid.DY[k];
                if (u >= 0 && v >= 0 && u < n && v < n && w.Sea[v * n + u]) return true;
            }
            return false;
        }

        static int Reach(GeneratedWorld w, int[] part, int n, int x, int y, int dx, int dy, int max)
        {
            for (int k = 1; k <= max; k++)
            {
                int u = x + dx * k, v = y + dy * k;
                if (u < 0 || v < 0 || u >= n || v >= n) return -1;
                if (!w.Sea[v * n + u]) return part[v * n + u];
            }
            return -1;
        }

        static int[] Components(GeneratedWorld w)
        {
            int n = w.Config.Size;
            var part = new int[n * n];
            for (int i = 0; i < part.Length; i++) part[i] = -1;
            var queue = new Queue<int>();
            int id = 0;
            for (int i = 0; i < part.Length; i++)
            {
                if (w.Sea[i] || part[i] >= 0) continue;
                part[i] = id;
                queue.Enqueue(i);
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue(), x = c % n, y = c / n;
                    for (int k = 0; k < 8; k++)
                    {
                        int u = x + Grid.DX[k], v = y + Grid.DY[k];
                        if (u < 0 || v < 0 || u >= n || v >= n) continue;
                        int j = v * n + u;
                        if (w.Sea[j] || part[j] >= 0) continue;
                        part[j] = id;
                        queue.Enqueue(j);
                    }
                }
                id++;
            }
            return part;
        }
    }
}
