using System;
using System.Collections.Generic;

namespace Stabby.World
{
    public sealed class RiverDelta
    {
        public int Apex;
        public int Main = -1;
        public List<int> Heads = new List<int>();
        public List<int> Branches = new List<int>();
    }

    public static class RiverDeltas
    {
        public const float MinDischarge = 0.35f, Chance = 0.7f, ReachMinM = 1200f, ReachMaxM = 3200f, MaxApexRise = 14f, MinApexRise = 0.6f;
        public const float SpreadMin = 0.7f, SpreadMax = 1.1f, RadiusFactor = 1.25f, PlainCapM = 25f, EdgeFade = 0.3f;
        public const float PlainGrade = 0.0012f, IsletRiseM = 1.5f, IsletScaleM = 500f, WobbleM = 900f, Wobble = 0.45f;
        public const float MainShareMin = 0.4f, MainShareMax = 0.55f, ClearWidths = 1f, FreeClears = 3f, TurnCost = 3f;
        public const int MaxBranches = 3;

        sealed class Fan
        {
            public int Apex, Next;
            public float MainShare;
            public List<int> Heads = new List<int>();
        }

        public static void Carve(WorldConfig c, float[] h, bool[] sea, Hydrology hy, float[] runoff, bool[] keepDry)
        {
            int n = c.Size;
            float cm = c.CellMeters;
            var q = hy.DischargeM3s;
            var mouths = new List<int>();
            for (int i = 0; i < n * n; i++)
                if (hy.IsRiver[i] && !sea[i] && sea[hy.Receiver[i]] && q[i] >= MinDischarge) mouths.Add(i);
            if (mouths.Count == 0) return;
            mouths.Sort((a, b) => q[a] != q[b] ? q[b].CompareTo(q[a]) : a.CompareTo(b));
            var rng = Rng.Stream(c.Seed, "river-delta");
            var claimed = new bool[n * n];
            var stamp = new int[n * n];
            var target = new float[n * n];
            var value = new float[n * n];
            var parent = new int[n * n];
            var clear = new float[n * n];
            var cost = new float[n * n];
            var prev = new int[n * n];
            var heap = new MinHeap();
            var fans = new List<Fan>();
            int tag = 0;
            foreach (int mouth in mouths)
            {
                float roll = rng.NextFloat(), reachM = rng.Range(ReachMinM, ReachMaxM), spread = rng.Range(SpreadMin, SpreadMax);
                float mainShare = rng.Range(MainShareMin, MainShareMax), flip = rng.NextFloat() < 0.5f ? -1f : 1f;
                int extra = rng.Range(2, MaxBranches + 1);
                uint noise = rng.NextU32();
                if (roll >= Chance || claimed[mouth]) continue;

                var chain = new List<int> { mouth };
                float run = 0;
                int cur = mouth;
                while (run < reachM)
                {
                    int up = -1, x = cur % n, y = cur / n;
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = x + Grid.DX[k], ny = y + Grid.DY[k];
                        if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                        int j = ny * n + nx;
                        if (hy.IsRiver[j] && hy.Receiver[j] == cur && (up < 0 || q[j] > q[up])) up = j;
                    }
                    if (up < 0 || hy.WaterSurface[up] > MaxApexRise) break;
                    run += Drainage.StepLength(n, up, cur, cm);
                    cur = up;
                    chain.Add(cur);
                }
                int s = cur;
                float apexSurf = hy.WaterSurface[s];
                if (run < ReachMinM || apexSurf < Erosion.MinLandHeight + MinApexRise || claimed[s]) continue;
                bool crowded = false;
                for (int ci = 0; ci + 1 < chain.Count && !crowded; ci++)
                {
                    int a = chain[ci], ax = a % n, ay = a / n;
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = ax + Grid.DX[k], ny = ay + Grid.DY[k];
                        if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                        int j = ny * n + nx;
                        if (j != chain[ci + 1] && hy.IsRiver[j] && hy.Receiver[j] == a && q[j] > mainShare * q[s]) crowded = true;
                    }
                }
                if (crowded) continue;

                tag++;
                int sx = s % n, sy = s / n;
                float dx = mouth % n - sx, dy = mouth / n - sy, len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len < 4) continue;
                float ux = dx / len, uy = dy / len, px = -uy, py = ux, radius = len * RadiusFactor;
                int r0 = (int)Math.Ceiling(radius) + 1;
                var region = new List<int>();
                for (int y = Math.Max(1, sy - r0); y <= Math.Min(n - 2, sy + r0); y++)
                    for (int x = Math.Max(1, sx - r0); x <= Math.Min(n - 2, sx + r0); x++)
                    {
                        int i = y * n + x;
                        if (sea[i] || claimed[i] || keepDry[i] || hy.LakeId[i] >= 0 || h[i] > apexSurf + PlainCapM) continue;
                        float ax = x - sx, ay = y - sy, along = ax * ux + ay * uy, side = Math.Abs(ax * px + ay * py);
                        float width = spread * along + 4f, dist = (float)Math.Sqrt(ax * ax + ay * ay);
                        if (along < 0 || dist > radius || side > width) continue;
                        bool nearLake = false;
                        for (int k = 0; k < 8 && !nearLake; k++) nearLake = hy.LakeId[i + Grid.DY[k] * n + Grid.DX[k]] >= 0;
                        if (nearLake) continue;
                        stamp[i] = tag;
                        region.Add(i);
                        float edge = Math.Max(side / width, dist / radius);
                        target[i] = 1f - Grid.Smoothstep(1f - EdgeFade, 1f, edge);
                    }
                bool In(int i) => stamp[i] == tag;

                heap.Clear();
                foreach (int i in region) value[i] = float.MaxValue;
                foreach (int i in region)
                {
                    int x = i % n, y = i / n;
                    for (int k = 0; k < 8; k++)
                        if (sea[i + Grid.DY[k] * n + Grid.DX[k]]) { value[i] = 0; heap.Push(0, i); break; }
                }
                while (heap.Count > 0)
                {
                    int i = heap.Pop(out float d);
                    if (d > value[i]) continue;
                    int x = i % n, y = i / n;
                    for (int k = 0; k < 8; k++)
                    {
                        int j = (y + Grid.DY[k]) * n + x + Grid.DX[k];
                        if (!In(j)) continue;
                        float nd = d + Grid.Dist[k] * cm;
                        if (nd < value[j]) { value[j] = nd; heap.Push(nd, j); }
                    }
                }
                foreach (int i in region)
                {
                    float fade = target[i], x = i % n * cm, y = i / n * cm;
                    if (value[i] == float.MaxValue) { target[i] = h[i]; continue; }
                    float bump = IsletRiseM * Math.Max(0, Noise.Fbm(noise, x / IsletScaleM, y / IsletScaleM, 3));
                    float plain = Erosion.MinLandHeight + PlainGrade * value[i] + bump;
                    target[i] = h[i] - fade * Math.Max(0, h[i] - plain);
                }

                float trunk = c.RiverWidthAtUnitFlow * (float)FMath.Pow(q[s], c.RiverWidthExponent) * (1f + c.RiverLowlandWiden);
                float clearM = ClearWidths * trunk + 2f * cm, freeM = FreeClears * clearM;
                var blocked = new List<int>();
                foreach (int i in region) if (hy.IsRiver[i]) blocked.Add(i);
                foreach (int i in chain) blocked.Add(i);
                Clearance(n, cm, blocked, region, stamp, tag, clear, heap, clearM);

                var onChain = new HashSet<int>(chain);
                var paths = new List<List<int>>();
                var taken = new HashSet<int>();
                float[] sides = extra == 1 ? new[] { 0.75f } : extra == 2 ? new[] { -0.75f, 0.75f } : new[] { -0.9f, -0.4f, 0.7f };
                for (int b = 0; b < sides.Length; b++)
                {
                    float t = sides[b] * flip * spread, ex = ux + t * px, ey = uy + t * py, el = (float)Math.Sqrt(ex * ex + ey * ey);
                    ex /= el; ey /= el;
                    var path = Route(n, cm, sea, hy, s, ex, ey, px, py, noise + (uint)b * 7919u, onChain, taken, clear, clearM, freeM, In, region, cost, prev, heap);
                    if (path == null) continue;
                    int head = path[0], hx = head % n, hy0 = head / n;
                    bool unique = true;
                    for (int k = 0; k < 8 && unique; k++)
                    {
                        int j = (hy0 + Grid.DY[k]) * n + hx + Grid.DX[k];
                        if (j != s && j != hy.Receiver[s] && hy.IsRiver[j] && q[j] >= q[s]) unique = false;
                    }
                    if (!unique) continue;
                    paths.Add(path);
                    foreach (int i in path) { taken.Add(i); blocked.Add(i); }
                    Clearance(n, cm, blocked, region, stamp, tag, clear, heap, clearM);
                }
                if (paths.Count == 0 || (1f - mainShare) * q[s] / paths.Count < 2f * c.RiverMinDischarge) continue;

                heap.Clear();
                var isHead = new HashSet<int>();
                foreach (int i in region) { value[i] = float.MaxValue; parent[i] = -1; }
                foreach (var path in paths)
                {
                    float total = Drainage.StepLength(n, s, path[0], cm);
                    for (int k = 1; k < path.Count; k++) total += Drainage.StepLength(n, path[k - 1], path[k], cm);
                    float along = Drainage.StepLength(n, s, path[0], cm), drop = apexSurf - Erosion.MinLandHeight;
                    for (int k = 0; k < path.Count; k++)
                    {
                        if (k > 0) along += Drainage.StepLength(n, path[k - 1], path[k], cm);
                        int i = path[k];
                        value[i] = apexSurf - drop * along / total;
                        parent[i] = k + 1 < path.Count ? path[k + 1] : Seaward(n, sea, i);
                    }
                    isHead.Add(path[0]);
                    for (int k = 1; k < path.Count; k++) heap.Push(value[path[k]], path[k]);
                }
                var seeded = new HashSet<int>();
                foreach (int i in region)
                {
                    int x = i % n, y = i / n;
                    for (int k = 0; k < 8; k++)
                    {
                        int j = (y + Grid.DY[k]) * n + x + Grid.DX[k];
                        if (In(j) && !hy.IsRiver[j]) continue;
                        if ((sea[j] || hy.IsRiver[j]) && seeded.Add(j)) heap.Push(sea[j] ? 0 : hy.WaterSurface[j], j);
                    }
                }
                var done = new HashSet<int>(taken);
                while (heap.Count > 0)
                {
                    int i = heap.Pop(out float v);
                    int x = i % n, y = i / n;
                    for (int k = 0; k < 8; k++)
                    {
                        int j = (y + Grid.DY[k]) * n + x + Grid.DX[k];
                        if (!In(j) || hy.IsRiver[j] || done.Contains(j)) continue;
                        done.Add(j);
                        value[j] = Math.Max(target[j], v + 1e-3f);
                        parent[j] = i;
                        heap.Push(value[j], j);
                    }
                }
                foreach (int i in done)
                {
                    h[i] = value[i];
                    hy.WaterSurface[i] = value[i];
                    hy.Receiver[i] = parent[i];
                }
                foreach (int i in region) claimed[i] = true;
                foreach (int i in chain) claimed[i] = true;
                var fan = new Fan { Apex = s, Next = hy.Receiver[s], MainShare = mainShare };
                foreach (var path in paths) fan.Heads.Add(path[0]);
                fans.Add(fan);
            }
            if (fans.Count == 0) return;

            var stack = Drainage.Stack(hy.Receiver, n);
            hy.DischargeM3s = q = Drainage.Accumulate(stack, hy.Receiver, runoff);
            foreach (var fan in fans)
            {
                float total = q[fan.Apex], each = (1f - fan.MainShare) * total / fan.Heads.Count;
                foreach (int head in fan.Heads)
                    for (int i = head; !sea[i]; i = hy.Receiver[i]) q[i] += each;
                for (int i = fan.Next; !sea[i]; i = hy.Receiver[i]) q[i] -= each * fan.Heads.Count;
            }
            for (int i = 0; i < n * n; i++)
                hy.IsRiver[i] = !sea[i] && hy.LakeId[i] < 0 && q[i] >= c.RiverMinDischarge;
        }

        static int Seaward(int n, bool[] sea, int i)
        {
            int x = i % n, y = i / n;
            for (int k = 0; k < 8; k += 2)
            {
                int j = (y + Grid.DY[k]) * n + x + Grid.DX[k];
                if (sea[j]) return j;
            }
            for (int k = 1; k < 8; k += 2)
            {
                int j = (y + Grid.DY[k]) * n + x + Grid.DX[k];
                if (sea[j]) return j;
            }
            return -1;
        }

        static void Clearance(int n, float cm, List<int> blocked, List<int> region, int[] stamp, int tag, float[] clear, MinHeap heap, float limit)
        {
            foreach (int i in region) clear[i] = float.MaxValue;
            heap.Clear();
            foreach (int i in blocked) { clear[i] = 0; heap.Push(0, i); }
            while (heap.Count > 0)
            {
                int i = heap.Pop(out float d);
                if (d > clear[i] || d > limit) continue;
                int x = i % n, y = i / n;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + Grid.DX[k], ny = y + Grid.DY[k];
                    if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                    int j = ny * n + nx;
                    if (stamp[j] != tag) continue;
                    float nd = d + Grid.Dist[k] * cm;
                    if (nd < clear[j]) { clear[j] = nd; heap.Push(nd, j); }
                }
            }
        }

        static List<int> Route(int n, float cm, bool[] sea, Hydrology hy, int s, float ex, float ey, float px, float py, uint noise,
            HashSet<int> chain, HashSet<int> taken, float[] clear, float clearM, float freeM, Func<int, bool> inRegion,
            List<int> region, float[] cost, int[] prev, MinHeap heap)
        {
            foreach (int i in region) { cost[i] = float.MaxValue; prev[i] = -1; }
            int sx = s % n, sy = s / n;
            heap.Clear();
            cost[s] = 0;
            heap.Push(0, s);
            int end = -1;
            while (heap.Count > 0)
            {
                int i = heap.Pop(out float d);
                if (i != s && d > cost[i]) continue;
                int x = i % n, y = i / n;
                if (i != s)
                {
                    bool coast = false;
                    for (int k = 0; k < 8 && !coast; k++) coast = sea[(y + Grid.DY[k]) * n + x + Grid.DX[k]];
                    if (coast) { end = i; break; }
                }
                float w = Wobble * Noise.Gradient(noise, x * cm / WobbleM, y * cm / WobbleM);
                float dx = ex + w * px, dy = ey + w * py, dl = (float)Math.Sqrt(dx * dx + dy * dy);
                dx /= dl; dy /= dl;
                for (int k = 0; k < 8; k++)
                {
                    int jx = x + Grid.DX[k], jy = y + Grid.DY[k], j = jy * n + jx;
                    if (j == s || !inRegion(j) || hy.IsRiver[j] || chain.Contains(j) || taken.Contains(j)) continue;
                    float ox = jx - sx, oy = jy - sy, fromS = (float)Math.Sqrt(ox * ox + oy * oy) * cm;
                    if (clear[j] < clearM * Grid.Smoothstep(2f * cm, freeM, fromS)) continue;
                    float turn = 1f - (Grid.DX[k] * dx + Grid.DY[k] * dy) / Grid.Dist[k];
                    float nd = d + Grid.Dist[k] * (1f + TurnCost * turn);
                    if (nd < cost[j]) { cost[j] = nd; prev[j] = i; heap.Push(nd, j); }
                }
            }
            if (end < 0) return null;
            var path = new List<int>();
            for (int i = end; i != s; i = prev[i]) path.Add(i);
            path.Add(s);
            path.Reverse();
            var cut = new List<int>();
            for (int k = 0; k < path.Count; k++)
            {
                cut.Add(path[k]);
                int far = k + 1;
                for (int j = path.Count - 1; j > k + 1; j--)
                    if (Grid.Adjacent(n, path[k], path[j])) { far = j; break; }
                k = far - 1;
            }
            cut.RemoveAt(0);
            return cut;
        }

        public static void Find(WorldConfig c, bool[] sea, Hydrology hy, int[] apexOf)
        {
            int n = c.Size;
            var q = hy.DischargeM3s;
            var inflow = new float[n * n];
            for (int i = 0; i < n * n; i++)
            {
                apexOf[i] = -1;
                int r = hy.Receiver[i];
                if (!sea[i] && r != i) inflow[r] += q[i];
            }
            hy.Deltas.Clear();
            var byApex = new Dictionary<int, RiverDelta>();
            for (int i = 0; i < n * n; i++)
            {
                if (!hy.IsRiver[i] || q[i] - inflow[i] <= c.RiverMinDischarge) continue;
                int x = i % n, y = i / n, apex = -1;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + Grid.DX[k], ny = y + Grid.DY[k];
                    if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                    int j = ny * n + nx;
                    if (hy.IsRiver[j] && hy.Receiver[j] != i && (apex < 0 || q[j] > q[apex])) apex = j;
                }
                if (apex < 0) continue;
                if (!byApex.TryGetValue(apex, out var d))
                {
                    d = new RiverDelta { Apex = apex };
                    byApex[apex] = d;
                    hy.Deltas.Add(d);
                }
                d.Heads.Add(i);
                apexOf[i] = apex;
            }
        }
    }
}
