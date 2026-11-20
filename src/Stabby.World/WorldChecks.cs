using System;
using System.Collections.Generic;

namespace Stabby.World
{
    // Rules every generated world must satisfy. Shared by the tests and the CLI
    // sweep so "passes" means the same thing everywhere.
    public static class WorldChecks
    {
        public const float SurfaceTolerance = 1e-3f;
        // Share of a corridor's lower three quarters that must have a river or
        // lake within the corridor's width.
        public const float CorridorFollowShare = 0.7f;

        public static List<string> Run(GeneratedWorld w)
        {
            var e = new List<string>();
            int n = w.Config.Size;
            var hy = w.Water;

            for (int x = 0; x < n; x++)
                if (!w.Sea[x] || !w.Sea[(n - 1) * n + x] || !w.Sea[x * n] || !w.Sea[x * n + n - 1])
                { e.Add("land touches the map border"); break; }

            for (int i = 0; i < n * n; i++)
            {
                if (w.Sea[i]) continue;
                int c = i, steps = 0;
                while (!w.Sea[c] && steps++ < n * n) c = hy.Receiver[c];
                if (!w.Sea[c]) { e.Add($"cell {i} never drains to the sea"); break; }
            }

            for (int i = 0; i < n * n; i++)
            {
                if (w.Sea[i]) continue;
                int r = hy.Receiver[i];
                if (!Grid.Adjacent(n, i, r)) { e.Add($"cell {i} drains to non-neighbour {r}"); break; }
                if (hy.DischargeM3s[r] + 1e-6f < hy.DischargeM3s[i] && !w.Sea[r])
                { e.Add($"discharge drops from {i} to {r}"); break; }
            }

            foreach (var s in hy.Rivers)
            {
                for (int k = 1; k < s.Cells.Count; k++)
                {
                    if (!Grid.Adjacent(n, s.Cells[k - 1], s.Cells[k]))
                    { e.Add($"river {s.Id} has a gap at point {k}"); break; }
                    if (s.Surface[k] > s.Surface[k - 1] + SurfaceTolerance)
                    { e.Add($"river {s.Id} flows uphill at point {k}"); break; }
                }
                int last = s.Cells[s.Cells.Count - 1];
                switch (s.End)
                {
                    case RiverEnd.Sea:
                        if (!w.Sea[last]) e.Add($"river {s.Id} ends at sea but last cell is land");
                        break;
                    case RiverEnd.Lake:
                        if (hy.LakeId[last] != s.LakeId) e.Add($"river {s.Id} misses its lake");
                        else if (s.Surface[s.Surface.Count - 2] + SurfaceTolerance < hy.Lakes[s.LakeId].Level)
                            e.Add($"river {s.Id} enters lake {s.LakeId} below its level");
                        break;
                    case RiverEnd.Junction:
                        var down = hy.Rivers[s.Downstream];
                        if (down.Cells[0] != last) e.Add($"river {s.Id} does not meet river {down.Id}");
                        break;
                }
            }

            foreach (var lake in hy.Lakes)
            {
                if (lake.Outlet < 0) { e.Add($"lake {lake.Id} has no outlet"); continue; }
                int outlets = 0;
                foreach (int i in lake.Cells)
                {
                    if (hy.LakeId[hy.Receiver[i]] != lake.Id) outlets++;
                    if (w.Height[i] > lake.Level + SurfaceTolerance) e.Add($"lake {lake.Id} cell {i} above its surface");
                }
                if (outlets != 1) e.Add($"lake {lake.Id} has {outlets} outlets");
                int below = hy.Receiver[lake.Outlet];
                if (!w.Sea[below] && hy.WaterSurface[below] > lake.Level + SurfaceTolerance)
                    e.Add($"lake {lake.Id} spills uphill");
                if (!w.Sea[below] && hy.DischargeM3s[lake.Outlet] >= w.Config.RiverMinDischarge && !hy.IsRiver[below] && hy.LakeId[below] < 0)
                    e.Add($"lake {lake.Id} outflow has no river");
            }

            if (w.Biome != null)
                for (int i = 0; i < n * n; i++)
                    if (!w.Sea[i] && w.Biome[i] < 0 && hy.LakeId[i] < 0) { e.Add($"land cell {i} has no biome"); break; }

            CheckSketch(w, e);
            CheckDetail(w, e);
            return e;
        }

        static void CheckDetail(GeneratedWorld w, List<string> e)
        {
            int n = w.Config.Size;
            var hy = w.Water;
            var d = new TerrainDetail(w);
            for (int y = 0; y < n - 1; y++)
                for (int x = 0; x < n - 1; x++)
                {
                    int i = y * n + x;
                    bool wet = w.Sea[i] || hy.LakeId[i] >= 0 || hy.IsRiver[i];
                    if (wet && d.HeightAt(x, y) != w.Height[i]) { e.Add($"detail moves water cell {i}"); return; }
                    if (w.Height[i] > 0 && w.Height[i + 1] > 0 && w.Height[i + n] > 0 && w.Height[i + n + 1] > 0
                        && d.HeightAt(x + 0.5f, y + 0.5f) <= 0) { e.Add($"detail sinks land below the sea near cell {i}"); return; }
                }
        }

        static void CheckSketch(GeneratedWorld w, List<string> e)
        {
            int n = w.Config.Size;
            var s = w.Sketch;
            var hy = w.Water;

            var island = Components(n, w.Sea);
            for (int k = 0; k < s.Landmasses.Count; k++)
            {
                var m = s.Landmasses[k];
                int a = island[(int)m.Y * n + (int)m.X];
                if (a < 0) { e.Add($"landmass {k} centre is sea"); continue; }
                for (int j = 0; j < k; j++)
                {
                    var o = s.Landmasses[j];
                    if (island[(int)o.Y * n + (int)o.X] == a) e.Add($"landmasses {j} and {k} are joined");
                }
            }

            for (int k = 0; k < s.Corridors.Count; k++)
            {
                var cor = s.Corridors[k];
                float total = 0;
                for (int p = 1; p < cor.X.Length; p++) total += Length(cor, p);
                int samples = 0, near = 0; float along = 0;
                for (int p = 1; p < cor.X.Length; p++)
                {
                    float len = Length(cor, p);
                    for (float t = 0; t < len; t += 1f)
                    {
                        float pos = along + t;
                        if (pos < 0.25f * total) continue;
                        float x = cor.X[p - 1] + (cor.X[p] - cor.X[p - 1]) * t / len;
                        float y = cor.Y[p - 1] + (cor.Y[p] - cor.Y[p - 1]) * t / len;
                        int cx = (int)x, cy = (int)y;
                        if (cx < 0 || cy < 0 || cx >= n || cy >= n || w.Sea[cy * n + cx]) continue;
                        samples++;
                        if (WaterWithin(n, hy, w.Sea, cx, cy, (int)Math.Ceiling(cor.Width))) near++;
                    }
                    along += len;
                }
                if (samples > 0 && near < CorridorFollowShare * samples)
                    e.Add($"corridor {k}: river follows only {near} of {samples} points");
            }

            if (w.Biome != null)
                foreach (var r in s.Regions)
                {
                    if (Array.IndexOf(w.BiomeIds, r.Biome) < 0) { e.Add($"region biome {r.Biome} is not in the content"); continue; }
                    int bad = 0, total = 0;
                    for (int y = Math.Max(0, (int)(r.Y - r.Radius)); y < Math.Min(n, (int)(r.Y + r.Radius) + 1); y++)
                        for (int x = Math.Max(0, (int)(r.X - r.Radius)); x < Math.Min(n, (int)(r.X + r.Radius) + 1); x++)
                        {
                            int i = y * n + x;
                            float dx = x - r.X, dy = y - r.Y;
                            if (w.Sea[i] || hy.LakeId[i] >= 0 || dx * dx + dy * dy > Biomes.RegionCore * Biomes.RegionCore * r.Radius * r.Radius) continue;
                            total++;
                            if (w.Biome[i] < 0 || w.BiomeIds[w.Biome[i]] != r.Biome) bad++;
                        }
                    if (bad > 0) e.Add($"region {r.Biome}: {bad} of {total} core cells have another biome");
                }

            for (int k = 0; k < s.Landmarks.Count; k++)
            {
                var l = s.Landmarks[k];
                int i = (int)l.Y * n + (int)l.X;
                if (w.Sea[i]) e.Add($"landmark {k} ({l.Kind}) is in the sea");
                else if (hy.LakeId[i] >= 0) e.Add($"landmark {k} ({l.Kind}) is under lake {hy.LakeId[i]}");
            }
        }

        static float Length(CorridorSketch c, int p)
        {
            float dx = c.X[p] - c.X[p - 1], dy = c.Y[p] - c.Y[p - 1];
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        static bool WaterWithin(int n, Hydrology hy, bool[] sea, int cx, int cy, int r)
        {
            for (int y = Math.Max(0, cy - r); y <= Math.Min(n - 1, cy + r); y++)
                for (int x = Math.Max(0, cx - r); x <= Math.Min(n - 1, cx + r); x++)
                {
                    int i = y * n + x;
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > r * r) continue;
                    if (hy.IsRiver[i] || hy.LakeId[i] >= 0) return true;
                }
            return false;
        }

        // Labels 4-connected land components; sea cells get -1.
        static int[] Components(int n, bool[] sea)
        {
            var id = new int[n * n];
            for (int i = 0; i < id.Length; i++) id[i] = -1;
            var queue = new List<int>();
            int next = 0;
            for (int s = 0; s < n * n; s++)
            {
                if (sea[s] || id[s] >= 0) continue;
                queue.Clear(); queue.Add(s); id[s] = next;
                for (int q = 0; q < queue.Count; q++)
                {
                    int i = queue[q], x = i % n, y = i / n;
                    if (x > 0 && !sea[i - 1] && id[i - 1] < 0) { id[i - 1] = next; queue.Add(i - 1); }
                    if (x < n - 1 && !sea[i + 1] && id[i + 1] < 0) { id[i + 1] = next; queue.Add(i + 1); }
                    if (y > 0 && !sea[i - n] && id[i - n] < 0) { id[i - n] = next; queue.Add(i - n); }
                    if (y < n - 1 && !sea[i + n] && id[i + n] < 0) { id[i + n] = next; queue.Add(i + n); }
                }
                next++;
            }
            return id;
        }
    }
}
