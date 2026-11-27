using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public static class SeaStacks
    {
        public const float MinOffM = 20f, MaxOffM = 200f, MaxDepthM = 50f, BeachM = 4f, CliffM = 15f, CliffReachM = 45f, ReliefM = 30f, ReliefReachM = 140f;
        public const float ClearM = 250f, CheckClearM = 150f, ClusterGapM = 3000f, SpreadM = 70f, SinkM = 4f;
        public const float MinD = 2.5f, MaxD = 12.5f, MinRise = 8f, MaxRise = 40f, StumpMaxRise = 4f;
        public const float ArchOffM = 40f, ArchMinD = 10f, ArchMaxD = 13f, ArchMinP = 6.5f, ArchDeckM = 7f;
        public const int MaxClusters = 4, MinStacks = 2, MaxStacks = 6, Tries = 8;

        public static bool Beachy(GeneratedWorld w, int i)
        {
            if (w.Sea[i] || w.Height[i] >= BeachM) return false;
            int n = w.Config.Size, x = i % n, y = i / n;
            for (int k = 0; k < 8; k++)
            {
                int u = x + Grid.DX[k], v = y + Grid.DY[k];
                if (u >= 0 && v >= 0 && u < n && v < n && !w.Sea[v * n + u] && w.Height[v * n + u] >= 2f * BeachM) return false;
            }
            return true;
        }

        public static bool Clear(GeneratedWorld w, float x, float z, float reachM)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            int cx = (int)(x / cm), cz = (int)(z / cm), r = (int)Math.Ceiling(reachM / cm);
            for (int v = Math.Max(0, cz - r); v <= Math.Min(n - 1, cz + r); v++)
                for (int u = Math.Max(0, cx - r); u <= Math.Min(n - 1, cx + r); u++)
                {
                    int i = v * n + u;
                    if (w.Water.IsRiver[i] || w.Water.LakeId[i] >= 0 || Beachy(w, i)) return false;
                }
            return true;
        }

        public static bool Offshore(GeneratedWorld w, float x, float z, float reachM)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            int r = (int)Math.Ceiling(reachM / cm), cx = (int)(x / cm), cz = (int)(z / cm);
            for (int v = cz - r; v <= cz + r; v++)
                for (int u = cx - r; u <= cx + r; u++)
                {
                    float dx = (u + 0.5f) * cm - x, dz = (v + 0.5f) * cm - z;
                    if (dx * dx + dz * dz > reachM * reachM) continue;
                    if (u < 0 || v < 0 || u >= n || v >= n || !w.Sea[v * n + u]) return false;
                }
            return true;
        }

        public static float ArchReach(FeatureEdit e)
        {
            float len = (float)Math.Sqrt((e.Bx - e.Ax) * (e.Bx - e.Ax) + (e.Bz - e.Az) * (e.Bz - e.Az));
            return 0.5f * len + e.D * (1f + FeatureEdit.StackLobe) + FeatureEdit.StackFootM + ArchOffM;
        }

        public static bool Wet(GeneratedWorld w, TerrainDetail detail, float x, float z, float reachM)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            for (int k = -1; k < 8; k++)
            {
                float px = (x + (k < 0 ? 0f : Grid.DX[k] * reachM * (Grid.DX[k] != 0 && Grid.DY[k] != 0 ? 0.7071f : 1f))) / cm;
                float pz = (z + (k < 0 ? 0f : Grid.DY[k] * reachM * (Grid.DX[k] != 0 && Grid.DY[k] != 0 ? 0.7071f : 1f))) / cm;
                int u = (int)px, v = (int)pz;
                if (u < 0 || v < 0 || u >= n || v >= n || !w.Sea[v * n + u] || detail.HeightAt(px, pz) >= -0.5f) return false;
            }
            return true;
        }

        public static void Place(GeneratedWorld w, TerrainDetail detail, List<FeatureEdit> list)
        {
            if (w.Water == null) return;
            int n = w.Config.Size;
            float cm = w.Config.CellMeters;
            var r = Rng.Stream(w.Config.Seed, "seastacks");
            var src = Shore(w, (int)Math.Ceiling(MaxOffM / cm) + 1);
            var steep = new Dictionary<int, bool>();
            var sites = new List<int>();
            for (int i = 0; i < n * n; i++)
            {
                int s = src[i];
                if (s < 0 || !w.Sea[i] || w.Height[i] > -1f || w.Height[i] < -MaxDepthM) continue;
                float off = Off(n, i, s) * cm;
                if (off < MinOffM || off > MaxOffM) continue;
                if (!steep.TryGetValue(s, out bool ok)) steep[s] = ok = Steep(w, s);
                if (ok) sites.Add(i);
            }
            for (int k = sites.Count - 1; k > 0; k--)
            {
                int j = r.Range(0, k + 1), t = sites[k];
                sites[k] = sites[j]; sites[j] = t;
            }
            var centres = new List<int>();
            bool arch = true;
            var cluster = new List<FeatureEdit>();
            foreach (int i in sites)
            {
                if (centres.Count >= MaxClusters) break;
                float cx = (i % n + 0.5f) * cm, cz = (i / n + 0.5f) * cm;
                bool far = true;
                foreach (int c in centres) if (Off(n, i, c) * cm < ClusterGapM) far = false;
                if (!far || !Clear(w, cx, cz, ClearM)) continue;
                int s = src[i];
                float ox = cx - (s % n + 0.5f) * cm, oz = cz - (s / n + 0.5f) * cm, ol = (float)Math.Sqrt(ox * ox + oz * oz);
                ox /= ol; oz /= ol;
                var rc = new Rng(Rng.Mix(r.NextU64()));
                Cluster(w, detail, src, list, cluster, ref rc, cx, cz, ox, oz, arch);
                int big = 0;
                foreach (var e in cluster) if (e.By >= MinRise) big++;
                if (big < MinStacks && !(cluster.Count > 0 && cluster[0].P > 0f)) continue;
                if (cluster[0].P > 0f) arch = false;
                list.AddRange(cluster);
                centres.Add(i);
            }
        }

        static void Cluster(GeneratedWorld w, TerrainDetail detail, int[] src, List<FeatureEdit> list, List<FeatureEdit> output, ref Rng r, float cx, float cz, float ox, float oz, bool arch)
        {
            output.Clear();
            int stacks = r.Range(MinStacks, MaxStacks + 1), stumps = r.Range(1, 4);
            float tx = -oz, tz = ox;
            for (int k = 0; k < stacks + stumps; k++)
            {
                bool stump = k >= stacks, hole = arch && k == 0;
                for (int t = 0; t < Tries; t++)
                {
                    float d = stump ? r.Range(MinD, 4f) : hole ? r.Range(ArchMinD, ArchMaxD) : r.Range(k == 0 ? 6f : MinD + 1f, k == 0 ? MaxD : 9f);
                    float rise = stump ? r.Range(1.5f, StumpMaxRise) : r.Range(Math.Max(MinRise, d * 1.2f), Math.Min(MaxRise, 8f + d * 3f));
                    float along = k == 0 && t == 0 ? 0f : r.Range(-1f, 1f) * SpreadM, out_ = k == 0 && t == 0 ? 0f : r.Range(-0.6f, 1f) * SpreadM;
                    float x = cx + tx * along + ox * out_, z = cz + tz * along + oz * out_;
                    float half = hole ? 1.4f * d : 0f, rag = stump ? 0.6f : r.Range(0.4f, Math.Min(3f, 0.12f * rise)), taper = stump ? 0.1f : r.Range(0f, 0.3f);
                    var e = new FeatureEdit
                    {
                        Kind = FeatureEdit.Add, Shape = FeatureEdit.SeaStack, D = d, R = FeatureEdit.StackReach(d), H = SinkM, F = rag, N = taper, S = r.NextFloat(),
                        Ax = x - ox * half, Az = z - oz * half, Bx = x + ox * half, Bz = z + oz * half
                    };
                    if (hole)
                    {
                        e.P = Math.Max(ArchMinP, r.Range(0.6f, 0.72f) * d);
                        e.T = 0.3f * e.P;
                        rise = Math.Max(rise, e.T + 1.25f * e.P + ArchDeckM);
                    }
                    if (hole && !Offshore(w, 0.5f * (e.Ax + e.Bx), 0.5f * (e.Az + e.Bz), ArchReach(e))) continue;
                    if (!Site(w, detail, src, ref e, rise)) continue;
                    if (hole && e.Ay > e.T - e.P - 1f) continue;
                    if (!Features.Inside(e, w.Config.CellMeters) || Features.Overlaps(e, list, 0) || Features.Overlaps(e, output, 0)) continue;
                    output.Add(e);
                    break;
                }
                if (hole && output.Count == 0) hole = false;
            }
        }

        static bool Site(GeneratedWorld w, TerrainDetail detail, int[] src, ref FeatureEdit e, float rise)
        {
            int n = w.Config.Size;
            float cm = w.Config.CellMeters, foot = e.D * (1f + FeatureEdit.StackLobe) + FeatureEdit.StackFootM;
            float len = (float)Math.Sqrt((e.Bx - e.Ax) * (e.Bx - e.Ax) + (e.Bz - e.Az) * (e.Bz - e.Az));
            float mx = 0.5f * (e.Ax + e.Bx), mz = 0.5f * (e.Az + e.Bz);
            int u = (int)(mx / cm), v = (int)(mz / cm);
            if (u < 0 || v < 0 || u >= n || v >= n || src[v * n + u] < 0) return false;
            if (!Wet(w, detail, mx, mz, 0.5f * len + foot + MinOffM)) return false;
            if (!Wet(w, detail, e.Ax, e.Az, foot) || !Wet(w, detail, e.Bx, e.Bz, foot) || !Clear(w, mx, mz, ClearM)) return false;
            float lo = float.MaxValue;
            for (int s = 0; s <= 2; s++)
            {
                float px = s == 0 ? e.Ax : s == 1 ? mx : e.Bx, pz = s == 0 ? e.Az : s == 1 ? mz : e.Bz;
                lo = Math.Min(lo, detail.HeightAt(px / cm, pz / cm));
                for (int k = 0; k < 8; k++) lo = Math.Min(lo, detail.HeightAt((px + Grid.DX[k] * foot) / cm, (pz + Grid.DY[k] * foot) / cm));
            }
            if (lo > -1f) return false;
            e.Ay = lo;
            e.By = rise;
            return true;
        }

        static bool Steep(GeneratedWorld w, int s)
        {
            float cm = w.Config.CellMeters;
            return !Beachy(w, s) && Top(w, s, Math.Max(1, (int)Math.Ceiling(CliffReachM / cm))) >= CliffM && Top(w, s, Math.Max(2, (int)Math.Ceiling(ReliefReachM / cm))) >= ReliefM;
        }

        static float Top(GeneratedWorld w, int s, int r)
        {
            int n = w.Config.Size, x = s % n, y = s / n;
            float top = 0f;
            for (int v = Math.Max(0, y - r); v <= Math.Min(n - 1, y + r); v++)
                for (int u = Math.Max(0, x - r); u <= Math.Min(n - 1, x + r); u++)
                {
                    int i = v * n + u;
                    if (!w.Sea[i]) top = Math.Max(top, w.Height[i]);
                }
            return top;
        }

        static float Off(int n, int a, int b)
        {
            float dx = a % n - b % n, dy = a / n - b / n;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        static int[] Shore(GeneratedWorld w, int steps)
        {
            int n = w.Config.Size;
            var src = new int[n * n];
            var step = new int[n * n];
            var queue = new Queue<int>();
            for (int i = 0; i < n * n; i++)
            {
                src[i] = -1;
                if (w.Sea[i]) continue;
                int x = i % n, y = i / n;
                for (int k = 0; k < 8; k++)
                {
                    int u = x + Grid.DX[k], v = y + Grid.DY[k];
                    if (u < 0 || v < 0 || u >= n || v >= n || !w.Sea[v * n + u]) continue;
                    src[i] = i;
                    queue.Enqueue(i);
                    break;
                }
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % n, y = i / n;
                if (step[i] >= steps) continue;
                for (int k = 0; k < 8; k++)
                {
                    int u = x + Grid.DX[k], v = y + Grid.DY[k];
                    if (u < 0 || v < 0 || u >= n || v >= n) continue;
                    int j = v * n + u;
                    if (!w.Sea[j] || src[j] >= 0) continue;
                    src[j] = src[i];
                    step[j] = step[i] + 1;
                    queue.Enqueue(j);
                }
            }
            return src;
        }
    }
}
